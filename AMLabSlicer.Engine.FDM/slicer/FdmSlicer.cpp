#include "FdmSlicer.h"
#include "MeshCutter.h"
#include "Infill.h"
#include "GCodeWriter.h"
#include <algorithm>
#include <cmath>

namespace fdm {

// ── 主切片流程 ──────────────────────────────────────────
// 输入: 三角网格 (vertices + indices) + 参数
// 输出: G-Code 字符串
// 通过 progressCb 回报进度

std::string SliceMesh(
    const float* vertices, int vertexCount,
    const int*   indices,  int indexCount,
    const SliceParams& params,
    ProgressCallback progressCb)
{
    // 1. 构建三角形列表
    std::vector<Triangle> triangles;
    int triCount = indexCount / 3;
    triangles.reserve(triCount);

    float globalZMin = 1e30f, globalZMax = -1e30f;

    for (int t = 0; t < triCount; ++t)
    {
        Triangle tri;
        for (int k = 0; k < 3; ++k)
        {
            int idx = indices[t * 3 + k];
            tri.v[k] = { vertices[idx * 3], vertices[idx * 3 + 1], vertices[idx * 3 + 2] };
        }
        tri.zMin = std::min({tri.v[0].z, tri.v[1].z, tri.v[2].z});
        tri.zMax = std::max({tri.v[0].z, tri.v[1].z, tri.v[2].z});

        globalZMin = std::min(globalZMin, tri.zMin);
        globalZMax = std::max(globalZMax, tri.zMax);

        triangles.push_back(tri);
    }

    if (progressCb) progressCb(0.05f, "网格预处理完成");

    // 2. 计算分层
    std::vector<float> zHeights;
    float z = globalZMin + params.initialLayerHeight * 0.5f;
    zHeights.push_back(z);
    z = globalZMin + params.initialLayerHeight;

    while (z < globalZMax)
    {
        z += params.layerHeight;
        zHeights.push_back(z);
    }

    int totalLayers = (int)zHeights.size();
    if (progressCb) progressCb(0.1f, "分层计算完成, 共 " + std::to_string(totalLayers) + " 层");

    // 3. 逐层切割
    std::vector<SliceLayer> layers;
    layers.reserve(totalLayers);

    for (int i = 0; i < totalLayers; ++i)
    {
        SliceLayer layer;
        layer.z = zHeights[i];
        layer.layerIndex = i;

        // 3a. 平面切割，得到线段
        auto segments = CutMeshAtZ(triangles, zHeights[i]);

        // 3b. 线段拼接为闭合轮廓
        auto contours = BuildContours(segments);

        // 3c. 外壁 = 原始轮廓
        layer.outerContours = contours;

        // 3d. 内壁偏移
        std::vector<Contour> innermostWalls;
        for (const auto& c : contours)
        {
            Contour prev = c;
            for (int w = 1; w < params.wallLineCount; ++w)
            {
                Contour inner = OffsetContour(prev, -params.lineWidth);
                if (inner.size() >= 3)
                {
                    layer.innerContours.push_back(inner);
                    prev = inner;
                }
            }
            innermostWalls.push_back(prev);
        }

        // 3e. 填充（在最内壁之内）
        if (params.infillDensity > 0.01f)
        {
            float spacing = params.lineWidth / (params.infillDensity / 100.0f);
            layer.infillLines = GenerateLineInfill(innermostWalls, spacing, 0.0f);
        }

        layers.push_back(std::move(layer));

        // 进度汇报
        if (progressCb && (i % 10 == 0 || i == totalLayers - 1))
        {
            float p = 0.1f + 0.7f * ((float)(i + 1) / totalLayers);
            progressCb(p, "切层 " + std::to_string(i + 1) + "/" + std::to_string(totalLayers));
        }
    }

    if (progressCb) progressCb(0.85f, "正在生成 G-Code");

    // 4. 生成 GCode
    std::string gcode = GenerateGCode(layers, params);

    if (progressCb) progressCb(1.0f, "切片完成");

    return gcode;
}

} // namespace fdm
