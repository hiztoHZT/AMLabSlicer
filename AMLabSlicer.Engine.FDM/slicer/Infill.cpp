#include "Infill.h"
#include <cmath>
#include <algorithm>
#include <limits>

namespace fdm {

// 射线与线段求交（用于扫描线裁剪）
static bool RaySegmentIntersectX(float y, const Vec2& a, const Vec2& b, float& x)
{
    if ((a.y < y && b.y < y) || (a.y > y && b.y > y)) return false;
    if (std::abs(b.y - a.y) < 1e-8f) return false;

    float t = (y - a.y) / (b.y - a.y);
    if (t < 0.0f || t > 1.0f) return false;

    x = a.x + t * (b.x - a.x);
    return true;
}

std::vector<Segment> GenerateLineInfill(
    const std::vector<Contour>& contours,
    float spacing,
    float angle)
{
    if (contours.empty() || spacing <= 0.0f) return {};

    // 计算所有轮廓的包围盒
    float minX = std::numeric_limits<float>::max();
    float maxX = std::numeric_limits<float>::lowest();
    float minY = std::numeric_limits<float>::max();
    float maxY = std::numeric_limits<float>::lowest();

    for (const auto& c : contours)
    {
        for (const auto& p : c)
        {
            minX = std::min(minX, p.x);
            maxX = std::max(maxX, p.x);
            minY = std::min(minY, p.y);
            maxY = std::max(maxY, p.y);
        }
    }

    std::vector<Segment> result;

    // 简化版：只做水平扫描线（angle 暂不旋转坐标系，后续可加）
    // 从 minY 到 maxY，每隔 spacing 发一条水平扫描线
    for (float y = minY + spacing * 0.5f; y < maxY; y += spacing)
    {
        // 收集所有轮廓边与扫描线的交点 X 坐标
        std::vector<float> xIntersections;

        for (const auto& contour : contours)
        {
            int n = (int)contour.size();
            for (int i = 0; i < n; ++i)
            {
                int j = (i + 1) % n;
                float x;
                if (RaySegmentIntersectX(y, contour[i], contour[j], x))
                {
                    xIntersections.push_back(x);
                }
            }
        }

        // 排序后，每两个交点之间是一条填充线段
        std::sort(xIntersections.begin(), xIntersections.end());

        for (size_t i = 0; i + 1 < xIntersections.size(); i += 2)
        {
            result.push_back({{xIntersections[i], y}, {xIntersections[i + 1], y}});
        }
    }

    return result;
}

} // namespace fdm
