using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using AMLabSlicer.Grpc;

namespace AMLabSlicer.Engine.Atom;

public static class AtomGeometry
{
    // Upstream SDF sampling assumes a zero minimum corner. Translate the entire scene
    // together, then restore PLY positions, preserving relative object placements.
    public static Vector3 WriteStl(SliceRequest request, string path, double cellSize, long maxCells)
    {
        if (request.Objects.Count == 0) throw new ArgumentException("没有输入模型。");
        // Geometric toolpaths are machine independent; machine execution is checked in AtomRunner.
        var meshes = new List<(float[] Vertices, int[] Indices)>();
        var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
        long triangles = 0;
        foreach (var obj in request.Objects)
        {
            if (obj.Units != "mm" || !obj.TransformApplied || obj.CoordinateSystem != "world")
                throw new ArgumentException("AtomSlicer 输入必须是已应用变换的世界坐标毫米网格。");
            if (obj.OverrideParameters.Count != 0) throw new ArgumentException("AtomSlicer 整体场求解不支持逐对象覆盖参数。");
            if (obj.Vertices.Length == 0 || obj.Vertices.Length % 12 != 0 || obj.Indices.Length == 0 || obj.Indices.Length % 12 != 0)
                throw new ArgumentException("网格字节布局无效。");
            var vertices = new float[obj.Vertices.Length / 4]; var indices = new int[obj.Indices.Length / 4];
            Buffer.BlockCopy(obj.Vertices.ToByteArray(), 0, vertices, 0, obj.Vertices.Length);
            Buffer.BlockCopy(obj.Indices.ToByteArray(), 0, indices, 0, obj.Indices.Length);
            if (vertices.Any(v => !float.IsFinite(v)) || indices.Any(i => i < 0 || i >= vertices.Length / 3))
                throw new ArgumentException("模型存在非有限坐标或越界索引。");
            foreach (var i in indices)
            {
                var v = new Vector3(vertices[i * 3], vertices[i * 3 + 1], vertices[i * 3 + 2]);
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
            }
            meshes.Add((vertices, indices)); triangles += indices.Length / 3;
        }
        var span = max - min;
        var cells = (Math.Ceiling(span.X / cellSize) + 4) * (Math.Ceiling(span.Y / cellSize) + 4) * (Math.Ceiling(span.Z / cellSize) + 4);
        if (!double.IsFinite(cells) || cells > maxCells)
            throw new ArgumentException($"预计 SDF 网格 {cells:N0} 个单元，超过当前上限 {maxCells:N0}。请增大层高或缩小模型。配置项：Atom:MaxGridCells。");
        if (triangles > uint.MaxValue || span.X <= 0 || span.Y <= 0 || span.Z <= 0)
            throw new ArgumentException("模型需要有效的三维体积范围。");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(new byte[80]); writer.Write((uint)triangles);
        foreach (var (vertices, indices) in meshes)
            for (var i = 0; i < indices.Length; i += 3)
            {
                writer.Write(0f); writer.Write(0f); writer.Write(0f);
                for (var j = 0; j < 3; j++)
                {
                    var k = indices[i + j] * 3;
                    writer.Write(vertices[k] - min.X); writer.Write(vertices[k + 1] - min.Y); writer.Write(vertices[k + 2] - min.Z);
                }
                writer.Write((ushort)0);
            }
        return min;
    }
    public static void ValidateAndRestorePly(byte[] bytes, Vector3 translation)
    {
        var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
        var end = Regex.Match(header, @"end_header\r?\n");
        var vertex = Regex.Match(header, @"element vertex (\d+)");
        var edge = Regex.Match(header, @"element edge (\d+)");
        if (!end.Success || !vertex.Success || !edge.Success || !header.Contains("format binary_little_endian 1.0"))
            throw new InvalidDataException("AtomSlicer PLY 头无效。");
        var count = int.Parse(vertex.Groups[1].Value); var edges = int.Parse(edge.Groups[1].Value);
        var offset = end.Index + end.Length;
        if (count == 0 || edges == 0 || bytes.LongLength != offset + count * 28L + edges * 8L)
            throw new InvalidDataException("AtomSlicer 返回空路径或 PLY 长度不匹配。");
        for (var i = 0; i < count; i++)
            for (var j = 0; j < 6; j++)
            {
                var position = offset + i * 28 + j * 4;
                var value = BitConverter.ToSingle(bytes, position);
                if (j < 3) value += j == 0 ? translation.X : j == 1 ? translation.Y : translation.Z;
                if (!float.IsFinite(value)) throw new InvalidDataException("AtomSlicer PLY 包含非有限位置或法向。");
                BitConverter.TryWriteBytes(bytes.AsSpan(position, 4), value);
            }
        for (var i = 0; i < edges * 2; i++)
        {
            var index = BitConverter.ToInt32(bytes, offset + count * 28 + i * 4);
            if (index < 0 || index >= count) throw new InvalidDataException("AtomSlicer PLY 边索引无效。");
        }
    }
}
