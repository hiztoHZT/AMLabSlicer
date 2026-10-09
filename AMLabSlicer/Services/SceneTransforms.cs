using System.Numerics;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Services;

public static class SceneTransforms
{
    public static Matrix4x4 GetWorldMatrix(SceneNode node)
    {
        // System.Numerics uses row vectors: apply local, then each parent.
        var matrix = Matrix4x4.Identity;
        for (SceneNode? current = node; current != null; current = current.Parent)
            matrix *= current.ModelMatrix;
        return matrix;
    }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Vector3[]> _geometryVertexCache = new();

        /// <summary>
        /// 避免 BoundsWithTransform 在某些状态下返回非有限值（NaN/Infinity）导致推飞模型。
        /// 开户 fast: true 模式时，仅对网格自身原本的 8 个本地包围盒角点进行矩阵变换并做极值提取(极快且适合渲染高亮边框，但多重旋转会虚胖)。
        /// fast: false 时，通过缓存顶点极速遍历计算精确的 World AABB(适用贴地)。
        /// </summary>
        public static bool TryComputeWorldAabb(SceneNode node, out Vector3 min, out Vector3 max, bool fast = false)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            if (node == null) return false;

            try
            {
                foreach (var n in node.Traverse())
                {
                    if (n is MeshNode mn && mn.Geometry != null)
                    {
                        var worldM = GetWorldMatrix(mn);
                        
                        if (fast)
                        {
                            var bound = mn.Geometry.Bound;
                            Vector3[] corners = new Vector3[] {
                                new Vector3(bound.Minimum.X, bound.Minimum.Y, bound.Minimum.Z),
                                new Vector3(bound.Minimum.X, bound.Minimum.Y, bound.Maximum.Z),
                                new Vector3(bound.Minimum.X, bound.Maximum.Y, bound.Minimum.Z),
                                new Vector3(bound.Minimum.X, bound.Maximum.Y, bound.Maximum.Z),
                                new Vector3(bound.Maximum.X, bound.Minimum.Y, bound.Minimum.Z),
                                new Vector3(bound.Maximum.X, bound.Minimum.Y, bound.Maximum.Z),
                                new Vector3(bound.Maximum.X, bound.Maximum.Y, bound.Minimum.Z),
                                new Vector3(bound.Maximum.X, bound.Maximum.Y, bound.Maximum.Z)
                            };
                            for (int i = 0; i < 8; i++)
                            {
                                var wp = Vector3.Transform(corners[i], worldM);
                                if (wp.X < min.X) min.X = wp.X; if (wp.X > max.X) max.X = wp.X;
                                if (wp.Y < min.Y) min.Y = wp.Y; if (wp.Y > max.Y) max.Y = wp.Y;
                                if (wp.Z < min.Z) min.Z = wp.Z; if (wp.Z > max.Z) max.Z = wp.Z;
                            }
                            any = true;
                        }
                        else
                        {
                            if (!_geometryVertexCache.TryGetValue(mn.Geometry, out Vector3[]? pts) || pts == null)
                            {
                                pts = mn.Geometry.Positions?.ToArray() ?? Array.Empty<Vector3>();
                                _geometryVertexCache.Add(mn.Geometry, pts);
                            }

                            int count = pts.Length;
                            for (int i = 0; i < count; i++)
                            {
                                var wp = Vector3.Transform(pts[i], worldM);
                                if (wp.X < min.X) min.X = wp.X; if (wp.X > max.X) max.X = wp.X;
                                if (wp.Y < min.Y) min.Y = wp.Y; if (wp.Y > max.Y) max.Y = wp.Y;
                                if (wp.Z < min.Z) min.Z = wp.Z; if (wp.Z > max.Z) max.Z = wp.Z;
                            }
                            if (count > 0) any = true;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            if (!any) return false;

            bool finite =
                !(float.IsNaN(min.X) || float.IsInfinity(min.X)) &&
                !(float.IsNaN(min.Y) || float.IsInfinity(min.Y)) &&
                !(float.IsNaN(min.Z) || float.IsInfinity(min.Z)) &&
                !(float.IsNaN(max.X) || float.IsInfinity(max.X)) &&
                !(float.IsNaN(max.Y) || float.IsInfinity(max.Y)) &&
                !(float.IsNaN(max.Z) || float.IsInfinity(max.Z));

            return finite;
        }

}
