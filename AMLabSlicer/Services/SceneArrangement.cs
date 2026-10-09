using System.Numerics;
using AMLabSlicer.Commands;
using AMLabSlicer.Core.Commands;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

public static class SceneArrangement
{
    public static ICommandAction CreateCommand(IEnumerable<SceneNode> nodes)
    {
        const float bedSize = 225, padding = 5;
        var bounds = new List<(SceneNode Node, Vector3 Min, Vector3 Max)>();
        foreach (var node in nodes)
        {
            if (!SceneTransforms.TryComputeWorldAabb(node, out var min, out var max))
                throw new InvalidOperationException("模型包围盒无效，无法自动摆放。");
            bounds.Add((node, min, max));
        }
        bounds.Sort((a, b) => ((b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y))
            .CompareTo((a.Max.X - a.Min.X) * (a.Max.Y - a.Min.Y)));
        var commands = new List<ICommandAction>();
        float x = 0, y = 0, rowHeight = 0;
        foreach (var item in bounds)
        {
            float width = item.Max.X - item.Min.X, depth = item.Max.Y - item.Min.Y;
            if (x > 0 && x + width > bedSize) { x = 0; y += rowHeight + padding; rowHeight = 0; }
            if (width > bedSize || y + depth > bedSize)
                throw new InvalidOperationException("模型无法全部放入 225 × 225 mm 平台，摆放未执行。");
            var parentWorld = item.Node.Parent == null ? Matrix4x4.Identity : SceneTransforms.GetWorldMatrix(item.Node.Parent);
            if (!Matrix4x4.Invert(parentWorld, out var inverseParent))
                throw new InvalidOperationException("模型父节点变换不可逆，无法摆放。");
            var translation = Matrix4x4.CreateTranslation(x - item.Min.X, y - item.Min.Y, -item.Min.Z);
            var matrix = item.Node.ModelMatrix * parentWorld * translation * inverseParent;
            commands.Add(new TransformCommand(item.Node, item.Node.ModelMatrix, matrix, "摆放"));
            x += width + padding;
            rowHeight = Math.Max(rowHeight, depth);
        }
        return new BatchCommand(commands, "自动摆放");
    }
}
