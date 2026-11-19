using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AMLabSlicer.Core.Parameters;
using AMLabSlicer.Core.Printers;
using AMLabSlicer.ViewModel;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

public sealed class ProjectDocument
{
    public int Version { get; set; } = 1;
    public PrinterProfile Printer { get; set; } = new();
    public string AlgorithmId { get; set; } = "";
    public string AlgorithmName { get; set; } = "";
    public List<SliceParameter> Parameters { get; set; } = new();
    public List<ProjectNode> Models { get; set; } = new();
}

public sealed class ProjectNode
{
    public string Name { get; set; } = "";
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool IsGroup { get; set; }
    public bool InOutliner { get; set; }
    public Vector3[]? Positions { get; set; }
    public Vector3[]? Normals { get; set; }
    public int[]? Indices { get; set; }
    public List<ProjectNode> Children { get; set; } = new();
}

public static class ProjectFile
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(SliceParameter))
                foreach (var property in info.Properties.Where(p => p.Name == nameof(SliceParameter.ParameterType)).ToArray())
                    info.Properties.Remove(property);
        });
        return new() { IncludeFields = true, TypeInfoResolver = resolver };
    }

    public static ProjectNode Capture(SceneNode node, IReadOnlyDictionary<SceneNode, OutlinerNodeViewModel> outline)
    {
        outline.TryGetValue(node, out var item);
        var result = new ProjectNode { Name = item?.Name ?? node.Name ?? "模型", Transform = node.ModelMatrix,
            Visible = node.Visible, Locked = item?.IsLocked ?? false, IsGroup = item?.IsGroup ?? false, InOutliner = item != null };
        if (node is MeshNode mesh && mesh.Geometry is MeshGeometry3D geometry)
        {
            result.Positions = (geometry.Positions ?? throw new InvalidDataException("模型缺少顶点。")).ToArray();
            result.Indices = (geometry.Indices ?? throw new InvalidDataException("模型缺少三角形索引。")).ToArray();
            result.Normals = geometry.Normals?.ToArray();
        }
        else if (node is not GroupNode) throw new InvalidOperationException("工程暂不支持保存此模型节点：" + result.Name);
        foreach (var child in node.Items) result.Children.Add(Capture(child, outline));
        return result;
    }

    public static (SceneNode Node, OutlinerNodeViewModel? Outline) Restore(ProjectNode data)
    {
        SceneNode node;
        if (data.Positions != null)
        {
            var geometry = new MeshGeometry3D { Positions = new(data.Positions), Indices = new(data.Indices!) };
            if (data.Normals?.Length == data.Positions.Length) geometry.Normals = new(data.Normals);
            else geometry.UpdateNormals();
            node = new MeshNode { Geometry = geometry, Material = new PhongMaterialCore {
                DiffuseColor = new(225f/255,225f/255,225f/255,1), AmbientColor = new(220f/255,220f/255,220f/255,1),
                SpecularColor = new(30f/255,30f/255,30f/255,1), SpecularShininess = 5 }, CullMode = SharpDX.Direct3D11.CullMode.None };
        }
        else node = new GroupNode();
        node.Name = data.Name; node.ModelMatrix = data.Transform; node.Visible = data.Visible;
        var outline = data.InOutliner ? new OutlinerNodeViewModel(node, data.Name, data.IsGroup) { IsLocked = data.Locked } : null;
        foreach (var child in data.Children)
        {
            var restored = Restore(child);
            ((GroupNode)node).AddChildNode(restored.Node);
            if (restored.Outline != null) outline?.Children.Add(restored.Outline);
        }
        return (node, outline);
    }

    public static ProjectDocument Clone(ProjectDocument document) => Decode(JsonSerializer.SerializeToUtf8Bytes(document, Options));
    private static ProjectDocument Decode(byte[] bytes)
    {
        var document = JsonSerializer.Deserialize<ProjectDocument>(bytes, Options) ?? throw new InvalidDataException("工程文件为空。");
        if (document.Version != 1) throw new InvalidDataException("不支持此工程版本。");
        PrinterValidation.ThrowIfInvalid(document.Printer);
        if (string.IsNullOrWhiteSpace(document.AlgorithmId)) throw new InvalidDataException("工程缺少切片算法。");
        if (document.Parameters.Select(p => p.Key).Distinct().Count() != document.Parameters.Count) throw new InvalidDataException("工程参数键重复。");
        foreach (var parameter in document.Parameters)
            if (parameter.Value is JsonElement value)
                parameter.Value = value.ValueKind switch { JsonValueKind.Number => value.GetDouble(), JsonValueKind.True => true,
                    JsonValueKind.False => false, JsonValueKind.String => value.GetString(), JsonValueKind.Null => null,
                    _ => throw new InvalidDataException("工程参数类型无效。") };
        foreach (var node in document.Models) ValidateNode(node);
        return document;
    }
    private static void ValidateNode(ProjectNode node)
    {
        var m = node.Transform;
        if (new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("模型位姿无效。");
        if (node.Positions != null && (node.Children.Count > 0 || node.Indices == null || node.Indices.Length % 3 != 0 ||
            node.Indices.Any(i => i < 0 || i >= node.Positions.Length) || node.Positions.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))))
            throw new InvalidDataException("模型网格无效。");
        foreach (var child in node.Children) ValidateNode(child);
    }
    public static void Save(string path, ProjectDocument document)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            using (var stream = archive.CreateEntry("project.json", CompressionLevel.Fastest).Open())
                JsonSerializer.Serialize(stream, document, Options);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static ProjectDocument Load(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("project.json") ?? throw new InvalidDataException("不是 AMLab 工程文件。");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Decode(buffer.ToArray());
    }
}
