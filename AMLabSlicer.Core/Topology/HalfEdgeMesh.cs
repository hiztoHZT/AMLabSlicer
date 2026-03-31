using System.Collections.Generic;
using System.Numerics;

namespace AMLabSlicer.Core.Topology
{
    /// <summary>
    /// 轻量级半边数据结构，用于面模式的高级拓扑选择（连通分量、环选等）
    /// </summary>
    public class HalfEdgeMesh
    {
        private int _faceCount;
        // 每个面的三条边对应的半边索引: halfEdge = faceIdx*3 + localEdge(0/1/2)
        // twin[he]: 与当前半边同边、反方向的半边索引(-1=边界)
        private int[] _twin = Array.Empty<int>();
        // 每个半边的目标顶点
        private int[] _vertex = Array.Empty<int>();
        // 邻接表: 面 -> 邻接面列表（包含共边面）
        private List<int>[] _adjFaces = Array.Empty<List<int>>();

        public int FaceCount => _faceCount;

        /// <summary>
        /// 从三角网格构建半边结构
        /// </summary>
        /// <param name="indices">三角形列表（每3个为一组 v0,v1,v2）</param>
        public void Build(IList<int> indices)
        {
            _faceCount = indices.Count / 3;
            _twin = new int[_faceCount * 3];
            _vertex = new int[_faceCount * 3];
            _adjFaces = new List<int>[_faceCount];

            for (int i = 0; i < _faceCount; i++)
                _adjFaces[i] = new List<int>();

            // 建立边 -> 半边 的映射 (v0,v1) -> halfEdgeIdx
            var edgeMap = new Dictionary<(int, int), int>(_faceCount * 3);

            for (int f = 0; f < _faceCount; f++)
            {
                int b = f * 3;
                int v0 = indices[b], v1 = indices[b + 1], v2 = indices[b + 2];
                int[] verts = { v0, v1, v2 };

                for (int k = 0; k < 3; k++)
                {
                    int heIdx = b + k;
                    int vFrom = verts[k];
                    int vTo = verts[(k + 1) % 3];
                    _vertex[heIdx] = vTo;
                    _twin[heIdx] = -1; // 默认边界
                    edgeMap[(vFrom, vTo)] = heIdx;
                }
            }

            // 匹配孪生半边
            foreach (var kvp in edgeMap)
            {
                var (va, vb) = kvp.Key;
                int heIdx = kvp.Value;
                if (edgeMap.TryGetValue((vb, va), out int twinIdx))
                {
                    _twin[heIdx] = twinIdx;
                }
            }

            // 构建邻接面列表
            for (int f = 0; f < _faceCount; f++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int tw = _twin[f * 3 + k];
                    if (tw >= 0)
                    {
                        int neighborFace = tw / 3;
                        _adjFaces[f].Add(neighborFace);
                    }
                }
            }
        }

        /// <summary>
        /// BFS 获取从 startFace 出发的连通分量（用于 L 键）
        /// </summary>
        public HashSet<int> GetConnectedComponent(int startFace)
        {
            if (startFace < 0 || startFace >= _faceCount)
                return new HashSet<int>();

            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startFace);
            visited.Add(startFace);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int nb in _adjFaces[cur])
                {
                    if (visited.Add(nb))
                        queue.Enqueue(nb);
                }
            }
            return visited;
        }

        /// <summary>
        /// 获取所有独立的连通分量（用于拆分为对象 B）
        /// </summary>
        public List<HashSet<int>> GetAllConnectedComponents()
        {
            var result = new List<HashSet<int>>();
            var globalVisited = new HashSet<int>();

            for (int f = 0; f < _faceCount; f++)
            {
                if (!globalVisited.Contains(f))
                {
                    var component = GetConnectedComponent(f);
                    result.Add(component);
                    globalVisited.UnionWith(component);
                }
            }
            return result;
        }

        /// <summary>
        /// Face Loop 环选：从一个面出发，沿"对边"方向找到循环带。
        /// 简化实现：返回从指定面穿越孪生边可访问的一排面。
        /// </summary>
        public List<int> GetFaceLoop(int startFace, int localEdge = 0)
        {
            var loop = new List<int>();
            var visited = new HashSet<int>();

            int curFace = startFace;
            int curEdge = localEdge;

            while (curFace >= 0 && visited.Add(curFace))
            {
                loop.Add(curFace);

                // 取当前半边的孪生，跳到相邻面
                int twinHe = _twin[curFace * 3 + curEdge];
                if (twinHe < 0) break; // 到达边界

                int nextFace = twinHe / 3;
                int nextLocEdge = twinHe % 3;
                // 继续沿下一条边前进（取 loop 对边）
                int continueEdge = (nextLocEdge + 2) % 3;

                curFace = nextFace;
                curEdge = continueEdge;
            }

            return loop;
        }

        /// <summary>
        /// 获取指定面的邻接面
        /// </summary>
        public IEnumerable<int> GetAdjacentFaces(int faceIdx)
        {
            if (faceIdx < 0 || faceIdx >= _faceCount)
                return Enumerable.Empty<int>();
            return _adjFaces[faceIdx];
        }
    }
}
