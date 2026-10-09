using System.Numerics;
using AMLabSlicer.Occt;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Assimp;
using HelixToolkit.SharpDX.Model.Scene;
using SharpAssimp;

namespace AMLabSlicer.Services;

public interface IModelImportService
{
    Task<SceneNode?> LoadAsync(string filePath);
}

public sealed class ModelImportService : IModelImportService
{
    public Task<SceneNode?> LoadAsync(string filePath) => Task.Run(() =>
    {
        var meshRootNode = OcctInteropService.IsStepFile(filePath) ? LoadStepFile(filePath) : LoadMeshFileViaAssimp(filePath);
        if (meshRootNode == null) throw new InvalidOperationException("模型未包含可读取的场景。");
        var modelName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        if (!SceneTransforms.TryComputeWorldAabb(meshRootNode, out var min, out var max))
        {
            meshRootNode.Dispose();
            throw new InvalidOperationException("模型没有有效的有限坐标顶点。");
        }
        var center = (min + max) * 0.5f;
        var pivotNode = new GroupNode
        {
            Name = modelName,
            ModelMatrix = Matrix4x4.CreateTranslation(112.5f, 112.5f, (max.Z - min.Z) * 0.5f)
        };
        meshRootNode.ModelMatrix *= Matrix4x4.CreateTranslation(-center);
        meshRootNode.Name = modelName + "_mesh";
        pivotNode.AddChildNode(meshRootNode);
        return (SceneNode?)pivotNode;
    });
        private static SceneNode? LoadStepFile(string filePath)
        {
            var occtService = new OcctInteropService();
            // 使用推荐的细分参数：0.1mm 线性偏差，0.5rad 角度偏差
            var meshGeometry = occtService.LoadStepModel(filePath, 0.1, 0.5);
            if (meshGeometry == null) return null;

            // 用 MeshGeometry3D 构造一个 MeshNode
            var meshNode = new MeshNode
            {
                Name     = System.IO.Path.GetFileNameWithoutExtension(filePath),
                Geometry = meshGeometry,
                Material = CreateDefaultMaterial(),
                CullMode = SharpDX.Direct3D11.CullMode.None,  // 双面渲染避免翻转时漏面
            };

            var rootGroup = new GroupNode { Name = meshNode.Name + "_root" };
            rootGroup.AddChildNode(meshNode);
            return rootGroup;
        }

        // ── Assimp 网格加载 ──────────────────────────────────

        private static SceneNode? LoadMeshFileViaAssimp(string filePath)
        {
            var importer = new Importer();
            importer.Configuration.AssimpPostProcessSteps =
                PostProcessSteps.JoinIdenticalVertices |
                PostProcessSteps.GenerateSmoothNormals |
                PostProcessSteps.CalculateTangentSpace;

            var scene = importer.Load(filePath);
            if (scene == null || scene.Root == null)
                return null;

            var mat = CreateDefaultMaterial();
            foreach (var node in scene.Root.Traverse())
                if (node is MeshNode mn) mn.Material = mat;

            return scene.Root;
        }

        // ── 材质工厂 ─────────────────────────────────────────

        private static HelixToolkit.SharpDX.Model.PhongMaterialCore CreateDefaultMaterial()
            => new HelixToolkit.SharpDX.Model.PhongMaterialCore
            {
                DiffuseColor      = new HelixToolkit.Maths.Color4(225f/255f, 225f/255f, 225f/255f, 1f),
                AmbientColor      = new HelixToolkit.Maths.Color4(220f/255f, 220f/255f, 220f/255f, 1f),
                SpecularColor     = new HelixToolkit.Maths.Color4(30f/255f,  30f/255f,  30f/255f,  1f),
                SpecularShininess = 5f,
            };

}
