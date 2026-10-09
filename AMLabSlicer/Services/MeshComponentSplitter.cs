using AMLabSlicer.Core.Topology;
using HelixToolkit;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

public static class MeshComponentSplitter
{
    public static IReadOnlyList<MeshNode> Split(MeshNode source)
    {
        if (source.Geometry is not MeshGeometry3D geometry ||
            geometry.Positions is not { Count: > 0 } positions ||
            geometry.Indices is not { Count: > 0 } indices || indices.Count % 3 != 0)
            throw new InvalidOperationException("模型不包含有效的三角网格。");

        var topology = new HalfEdgeMesh();
        topology.Build(indices);
        var components = topology.GetAllConnectedComponents();
        if (components.Count <= 1) return Array.Empty<MeshNode>();

        var result = new List<MeshNode>(components.Count);
        for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            var componentIndices = new IntCollection(components[componentIndex].Count * 3);
            foreach (var faceIndex in components[componentIndex].OrderBy(index => index))
            {
                var offset = faceIndex * 3;
                componentIndices.Add(indices[offset]);
                componentIndices.Add(indices[offset + 1]);
                componentIndices.Add(indices[offset + 2]);
            }

            result.Add(new MeshNode
            {
                Name = $"{source.Name ?? "Part"}_{componentIndex + 1}",
                Geometry = new MeshGeometry3D
                {
                    Positions = positions,
                    Normals = geometry.Normals,
                    TextureCoordinates = geometry.TextureCoordinates,
                    Tangents = geometry.Tangents,
                    BiTangents = geometry.BiTangents,
                    Colors = geometry.Colors,
                    Indices = componentIndices
                },
                Material = source.Material,
                ModelMatrix = source.ModelMatrix,
                CullMode = source.CullMode
            });
        }
        return result;
    }
}
