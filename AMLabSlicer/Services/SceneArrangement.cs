using System.Numerics;
using AMLabSlicer.Commands;
using AMLabSlicer.Core.Commands;
using AMLabSlicer.Core.Printers;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

public static class SceneArrangement
{
    private const float Gap = 5;
    private const float Epsilon = 0.0001f;
    private readonly record struct Footprint(float Left, float Bottom, float Right, float Top);

    // Plan every transform before applying any of them, so a failed arrangement is atomic.
    // Input order is retained. Fixed nodes are obstacles and never receive a command.
    public static ICommandAction CreateCommand(IEnumerable<SceneNode> nodes, PrinterProfile? printer = null,
        IEnumerable<SceneNode>? fixedNodes = null)
    {
        float width = printer == null ? 225 : (float)printer.Number("machine_width");
        float depth = printer == null ? 225 : (float)printer.Number("machine_depth");
        var shape = printer?.Machine.GetValueOrDefault("machine_shape") ?? "rectangular";
        if (!float.IsFinite(width) || !float.IsFinite(depth) || width <= 0 || depth <= 0)
            throw new InvalidOperationException("平台尺寸无效，无法自动排列。");
        if (shape is not ("rectangular" or "elliptic"))
            throw new InvalidOperationException($"不支持的平台形状：{shape}。");
        bool centered = printer?.Machine.GetValueOrDefault("machine_center_is_zero") == "true";
        var center = centered ? Vector2.Zero : new Vector2(width / 2, depth / 2);
        var moving = nodes.Distinct().ToArray();
        var movingSet = moving.ToHashSet();
        var occupied = new List<Footprint>();
        foreach (var node in (fixedNodes ?? []).Distinct().Where(node => !movingSet.Contains(node)))
        {
            var (min, max) = Bounds(node);
            occupied.Add(new(min.X, min.Y, max.X, max.Y));
        }

        var commands = new List<ICommandAction>();
        foreach (var node in moving)
        {
            var (min, max) = Bounds(node);
            float halfX = (max.X - min.X) / 2, halfY = (max.Y - min.Y) / 2;
            bool Fits(Vector2 p)
            {
                float x = Math.Abs(p.X - center.X) + halfX;
                float y = Math.Abs(p.Y - center.Y) + halfY;
                return shape == "elliptic"
                    ? Math.Pow(x / (width / 2), 2) + Math.Pow(y / (depth / 2), 2) <= 1 + 1e-7
                    : x <= width / 2 + Epsilon && y <= depth / 2 + Epsilon;
            }
            // Each obstacle excludes a rectangle of candidate centers. The closest free
            // center lies at the bed center or an exclusion edge on each axis. Testing
            // their Cartesian product avoids grid quantization, including narrow gaps.
            var xs = new HashSet<float> { center.X };
            var ys = new HashSet<float> { center.Y };
            foreach (var box in occupied)
            {
                xs.Add(box.Left - Gap - halfX); xs.Add(box.Right + Gap + halfX);
                ys.Add(box.Bottom - Gap - halfY); ys.Add(box.Top + Gap + halfY);
            }
            Vector2? best = null;
            double bestDistance = double.PositiveInfinity;
            var orderedYs = ys.OrderBy(y => Math.Abs(y - center.Y)).ThenByDescending(y => y).ToArray();
            foreach (float x in xs.OrderBy(x => Math.Abs(x - center.X)).ThenByDescending(x => x))
            foreach (float y in orderedYs)
            {
                var candidate = new Vector2(x, y);
                double distance = Math.Pow(x - center.X, 2) + Math.Pow(y - center.Y, 2);
                if (distance >= bestDistance || !Fits(candidate)) continue;
                var box = new Footprint(x - halfX, y - halfY, x + halfX, y + halfY);
                if (occupied.Any(other => box.Left < other.Right + Gap - Epsilon &&
                    box.Right > other.Left - Gap + Epsilon && box.Bottom < other.Top + Gap - Epsilon &&
                    box.Top > other.Bottom - Gap + Epsilon)) continue;
                best = candidate; bestDistance = distance;
            }
            if (best is not { } position)
                throw new InvalidOperationException($"无法在当前平台为模型“{node.Name}”找到保留 {Gap} mm 间距的空位。请调整模型大小、方向或数量后重试。");
            var parentWorld = node.Parent == null ? Matrix4x4.Identity : SceneTransforms.GetWorldMatrix(node.Parent);
            if (!Matrix4x4.Invert(parentWorld, out var inverseParent))
                throw new InvalidOperationException("模型父节点变换不可逆，无法排列。");
            var translation = Matrix4x4.CreateTranslation(position.X - (min.X + max.X) / 2,
                position.Y - (min.Y + max.Y) / 2, -min.Z);
            var matrix = node.ModelMatrix * parentWorld * translation * inverseParent;
            commands.Add(new TransformCommand(node, node.ModelMatrix, matrix, "排列"));
            occupied.Add(new(position.X - halfX, position.Y - halfY, position.X + halfX, position.Y + halfY));
        }
        return new BatchCommand(commands, "自动排列");
    }

    private static (Vector3 Min, Vector3 Max) Bounds(SceneNode node)
    {
        if (!SceneTransforms.TryComputeWorldAabb(node, out var min, out var max))
            throw new InvalidOperationException($"模型“{node.Name}”包围盒无效，无法自动排列。");
        return (min, max);
    }
}
