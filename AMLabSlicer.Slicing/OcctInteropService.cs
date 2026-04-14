using System.Runtime.InteropServices;
using System.Numerics;
using HelixToolkit;
using HelixToolkit.SharpDX;

namespace AMLabSlicer.Slicing
{
    /// <summary>
    /// OCCT 引擎互操作层。
    /// 通过 P/Invoke 调用 AMLabSlicer.Engine.dll 中的 C-API 函数，
    /// 实现 STEP 文件加载与曲面细分，返回 HelixToolkit 可直接渲染的 MeshGeometry3D。
    /// </summary>
    public class OcctInteropService
    {
        // ── Native C-API 声明 ────────────────────────────────

        [DllImport("AMLabSlicer.Engine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool LoadStepAndTessellate(
            [MarshalAs(UnmanagedType.LPStr)] string filePath,
            double linearDeflection,
            double angularDeflection,
            out IntPtr outVertices,
            out int    outVertexCount,
            out IntPtr outNormals,
            out IntPtr outIndices,
            out int    outIndexCount
        );

        [DllImport("AMLabSlicer.Engine.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FreeMeshData(IntPtr vertices, IntPtr normals, IntPtr indices);

        [DllImport("AMLabSlicer.Engine.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetLastEngineError();

        // ── 默认细分参数 ─────────────────────────────────────

        /// <summary>
        /// 默认线性偏差（mm）。值越小三角面片越密，精度越高。
        /// 切片预览推荐 0.1 ~ 0.5 mm。
        /// </summary>
        public double DefaultLinearDeflection  { get; set; } = 0.1;

        /// <summary>
        /// 默认角度偏差（弧度）。控制曲面圆滑程度，推荐 0.5 rad。
        /// </summary>
        public double DefaultAngularDeflection { get; set; } = 0.5;

        // ── Public API ───────────────────────────────────────

        /// <summary>
        /// 加载 STEP 文件并返回可渲染的 MeshGeometry3D。
        /// 此方法为纯 CPU 密集型操作，请在后台线程调用（Task.Run）。
        /// </summary>
        /// <param name="filePath">STEP 文件的完整路径</param>
        /// <returns>包含顶点、法线和索引的 MeshGeometry3D，失败时返回 null</returns>
        /// <exception cref="InvalidOperationException">当 native 调用失败时抛出</exception>
        public MeshGeometry3D? LoadStepModel(string filePath)
        {
            return LoadStepModel(filePath, DefaultLinearDeflection, DefaultAngularDeflection);
        }

        /// <summary>
        /// 加载 STEP 文件并返回可渲染的 MeshGeometry3D（可指定细分参数）。
        /// </summary>
        public MeshGeometry3D? LoadStepModel(string filePath, double linearDeflection, double angularDeflection)
        {
            IntPtr verticesPtr = IntPtr.Zero;
            IntPtr normalsPtr  = IntPtr.Zero;
            IntPtr indicesPtr  = IntPtr.Zero;
            int    vertexCount = 0;
            int    indexCount  = 0;

            try
            {
                bool success = LoadStepAndTessellate(
                    filePath,
                    linearDeflection,
                    angularDeflection,
                    out verticesPtr,
                    out vertexCount,
                    out normalsPtr,
                    out indicesPtr,
                    out indexCount
                );

                if (!success)
                {
                    string error = GetNativeError();
                    throw new InvalidOperationException($"STEP 加载失败: {error}");
                }

                if (vertexCount <= 0 || indexCount <= 0)
                    throw new InvalidOperationException("STEP 文件解析成功但未生成任何几何数据。");

                // ── 从非托管内存复制到托管数组 ──────────────
                int floatCount = vertexCount * 3;

                float[] vertices = new float[floatCount];
                float[] normals  = new float[floatCount];
                int[]   indices  = new int[indexCount];

                Marshal.Copy(verticesPtr, vertices, 0, floatCount);
                Marshal.Copy(normalsPtr,  normals,  0, floatCount);
                Marshal.Copy(indicesPtr,  indices,  0, indexCount);

                // ── 构造 MeshGeometry3D ─────────────────────
                var positions = new Vector3Collection(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    int idx = i * 3;
                    positions.Add(new Vector3(vertices[idx], vertices[idx + 1], vertices[idx + 2]));
                }

                var indexCollection = new IntCollection(indexCount);
                for (int i = 0; i < indexCount; i++)
                    indexCollection.Add(indices[i]);

                var mesh = new MeshGeometry3D
                {
                    Positions = positions,
                    Indices   = indexCollection,
                };

                // Always recalculate normals in C# for reliability.
                // OCCT tessellation may produce zero or incorrectly oriented normals
                // depending on face topology. Cross-product recalc is guaranteed correct.
                mesh.Normals = RecalculateNormals(positions, indexCollection);

                return mesh;
            }
            finally
            {
                // ── 关键：无论成功与否立即释放非托管内存 ────
                FreeMeshData(verticesPtr, normalsPtr, indicesPtr);
            }
        }

        /// <summary>
        /// 判断文件扩展名是否为 STEP 格式。
        /// </summary>
        public static bool IsStepFile(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".step" || ext == ".stp";
        }

        // ── Private Helpers ──────────────────────────────────

        private static string GetNativeError()
        {
            IntPtr ptr = GetLastEngineError();
            if (ptr == IntPtr.Zero) return "Unknown error";
            return Marshal.PtrToStringAnsi(ptr) ?? "Unknown error";
        }

        /// <summary>
        /// Recalculate per-vertex normals from geometry (cross product, angle-weighted average).
        /// Used as fallback when C++ engine returns zero normals.
        /// </summary>
        private static Vector3Collection RecalculateNormals(
            Vector3Collection positions, IntCollection indices)
        {
            int vCount = positions.Count;
            var accum  = new Vector3[vCount]; // zero-initialized

            int triCount = indices.Count / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = indices[t * 3];
                int i1 = indices[t * 3 + 1];
                int i2 = indices[t * 3 + 2];

                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= vCount || i1 >= vCount || i2 >= vCount)
                    continue;

                Vector3 a = positions[i0];
                Vector3 b = positions[i1];
                Vector3 c = positions[i2];

                Vector3 edge1 = b - a;
                Vector3 edge2 = c - a;
                Vector3 faceN = Vector3.Cross(edge1, edge2);
                // faceN magnitude is proportional to triangle area → natural area weighting

                accum[i0] += faceN;
                accum[i1] += faceN;
                accum[i2] += faceN;
            }

            var result = new Vector3Collection(vCount);
            for (int i = 0; i < vCount; i++)
            {
                float len = accum[i].Length();
                result.Add(len > 1e-10f ? accum[i] / len : Vector3.UnitZ);
            }
            return result;
        }
    }
}
