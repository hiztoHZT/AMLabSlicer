using System.Numerics;
using AMLabSlicer.Commands;
using AMLabSlicer.Core.Commands;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

/// <summary>Nearby supporting-plane search; no global print-orientation optimization.</summary>
public static class BottomFacePlacement
{
    public sealed record Mesh(Vector3[] Positions, int[] Indices, Matrix4x4 World);
    public sealed record Snapshot(SceneNode Node, Matrix4x4 Before, Matrix4x4 ParentWorld, Mesh[] Meshes);
    private sealed class Plane(Vector3 normal, float offset)
    {
        public Vector3 Normal = normal;
        public float Offset = offset;
        public double Area;
        public double Score => Area * Math.Pow(-Normal.Z, 4);
    }

    // Capture on the UI thread; the numerical search below only reads copied geometry.
    public static Snapshot Capture(SceneNode node) => new(node, node.ModelMatrix,
        node.Parent == null ? Matrix4x4.Identity : SceneTransforms.GetWorldMatrix(node.Parent),
        node.Traverse().OfType<MeshNode>().Where(n => n.Geometry?.Positions != null).Select(n =>
            new Mesh(n.Geometry!.Positions!.ToArray(), n.Geometry.Indices?.ToArray() ?? [], SceneTransforms.GetWorldMatrix(n))).ToArray());

    public static ICommandAction CreateCommand(Snapshot snapshot)
    {
        var meshes = snapshot.Meshes.Select(m => (Points: m.Positions.Select(p => Vector3.Transform(p, m.World)).ToArray(), m.Indices)).ToArray();
        var points = meshes.SelectMany(m => m.Points).ToArray();
        if (points.Length < 3 || points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
            throw new InvalidOperationException("模型没有有效的三角网格。");
        var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
        var center = (min + max) / 2;
        // Use centered coordinates for plane tests to avoid precision loss far from the origin.
        float tolerance = Math.Max(0.0001f, (max - min).Length() * 0.000001f);
        var planes = new Dictionary<(int, int, int, long), Plane>();
        foreach (var mesh in meshes)
        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            int ia = mesh.Indices[i], ib = mesh.Indices[i+1], ic = mesh.Indices[i+2];
            if ((uint)ia >= mesh.Points.Length || (uint)ib >= mesh.Points.Length || (uint)ic >= mesh.Points.Length) continue;
            var a = mesh.Points[ia]; var b = mesh.Points[ib]; var c = mesh.Points[ic];
            var cross = Vector3.Cross(b-a, c-a); float length = cross.Length();
            if (!float.IsFinite(length) || length < 1e-10f) continue;
            var normal = cross / length;
            // Winding and imported shading normals do not determine the supporting side.
            if (normal.Z > 0) normal = -normal;
            if (-normal.Z < 0.5f) continue; // At most 60 degrees from the current downward direction.
            float offset = Vector3.Dot(normal, a-center);
            var key = ((int)MathF.Round(normal.X*100000), (int)MathF.Round(normal.Y*100000),
                (int)MathF.Round(normal.Z*100000), (long)Math.Round(offset/tolerance));
            if (!planes.TryGetValue(key, out var plane)) planes.Add(key, plane = new Plane(normal, offset));
            plane.Area += length/2;
        }
        Plane? best = null;
        // Bound the work for highly tessellated curved surfaces. Reject interior/overhanging
        // planes: all vertices must lie on the interior side of the proposed support plane.
        foreach (var plane in planes.Values.OrderByDescending(p => p.Score).Take(256))
        {
            if (points.Any(p => Vector3.Dot(plane.Normal, p-center) > plane.Offset + tolerance)) continue;
            best = plane; break;
        }
        if (best == null) throw new InvalidOperationException("当前朝向附近没有找到合适的支撑底面，请先稍微旋转模型再试。");
        var axis = Vector3.Cross(best.Normal, -Vector3.UnitZ);
        var rotation = axis.LengthSquared() < 1e-12f ? Matrix4x4.Identity :
            Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(-best.Normal.Z, -1f, 1f)));
        var delta = Matrix4x4.CreateTranslation(-center) * rotation * Matrix4x4.CreateTranslation(center);
        var rotated = points.Select(p => Vector3.Transform(p, delta)).ToArray();
        var afterMin = rotated.Aggregate(Vector3.Min); var afterMax = rotated.Aggregate(Vector3.Max);
        // Preserve the object's XY footprint center; only rotate and ground it.
        delta *= Matrix4x4.CreateTranslation(center.X-(afterMin.X+afterMax.X)/2,
            center.Y-(afterMin.Y+afterMax.Y)/2, -afterMin.Z);
        if (!Matrix4x4.Invert(snapshot.ParentWorld, out var inverseParent))
            throw new InvalidOperationException("模型父节点变换不可逆。");
        var result = snapshot.Before * snapshot.ParentWorld * delta * inverseParent;
        return new TransformCommand(snapshot.Node, snapshot.Before, result, "自动底面");
    }
}
