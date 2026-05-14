#include "MeshCutter.h"
#include <cmath>
#include <map>

namespace fdm {

// ─── 平面切割 ─────────────────────────────────────────
// 对每个三角形，用 Z=z 平面与三条边求交。
// 如果平面穿过三角形，恰好产生两个交点 → 一条线段。

static bool EdgeIntersectZ(const Vec3& a, const Vec3& b, float z, Vec2& out)
{
    if ((a.z < z && b.z < z) || (a.z > z && b.z > z)) return false;
    if (std::abs(b.z - a.z) < 1e-8f) return false;

    float t = (z - a.z) / (b.z - a.z);
    if (t < 0.0f || t > 1.0f) return false;

    out.x = a.x + t * (b.x - a.x);
    out.y = a.y + t * (b.y - a.y);
    return true;
}

std::vector<Segment> CutMeshAtZ(const std::vector<Triangle>& triangles, float z)
{
    std::vector<Segment> segments;
    segments.reserve(triangles.size() / 4);

    for (const auto& tri : triangles)
    {
        if (z < tri.zMin || z > tri.zMax) continue;

        Vec2 points[3];
        int count = 0;

        // 三条边
        const Vec3* v = tri.v;
        for (int i = 0; i < 3 && count < 2; ++i)
        {
            int j = (i + 1) % 3;
            if (EdgeIntersectZ(v[i], v[j], z, points[count]))
                ++count;
        }

        if (count == 2)
        {
            segments.push_back({points[0], points[1]});
        }
    }
    return segments;
}

// ─── 轮廓拼接 ─────────────────────────────────────────
// 将线段的端点用哈希表匹配，首尾相连形成闭合环。

// 端点量化键（精度 0.001mm）
static int64_t PointKey(float x, float y)
{
    int32_t ix = static_cast<int32_t>(std::round(x * 1000.0f));
    int32_t iy = static_cast<int32_t>(std::round(y * 1000.0f));
    return (static_cast<int64_t>(ix) << 32) | static_cast<uint32_t>(iy);
}

std::vector<Contour> BuildContours(const std::vector<Segment>& segments)
{
    if (segments.empty()) return {};

    // 用邻接表存储：每个端点 → 它能连到的另一端
    std::multimap<int64_t, size_t> adjacency;  // key → segment index
    for (size_t i = 0; i < segments.size(); ++i)
    {
        adjacency.insert({PointKey(segments[i].a.x, segments[i].a.y), i});
        adjacency.insert({PointKey(segments[i].b.x, segments[i].b.y), i});
    }

    std::vector<bool> used(segments.size(), false);
    std::vector<Contour> contours;

    for (size_t start = 0; start < segments.size(); ++start)
    {
        if (used[start]) continue;

        Contour contour;
        used[start] = true;
        contour.push_back(segments[start].a);
        contour.push_back(segments[start].b);

        Vec2 current = segments[start].b;

        // 沿着链一直走
        for (int safety = 0; safety < (int)segments.size(); ++safety)
        {
            int64_t key = PointKey(current.x, current.y);
            auto range = adjacency.equal_range(key);

            bool found = false;
            for (auto it = range.first; it != range.second; ++it)
            {
                size_t idx = it->second;
                if (used[idx]) continue;

                used[idx] = true;
                const auto& seg = segments[idx];

                // 判断是哪个端点与 current 相连
                Vec2 next;
                if (std::abs(seg.a.x - current.x) < 0.002f && std::abs(seg.a.y - current.y) < 0.002f)
                    next = seg.b;
                else
                    next = seg.a;

                contour.push_back(next);
                current = next;
                found = true;
                break;
            }

            if (!found) break;
        }

        if (contour.size() >= 3)
            contours.push_back(std::move(contour));
    }

    return contours;
}

// ─── 轮廓偏移 ─────────────────────────────────────────
// 简易版：沿每条边的内法线方向平移 offset

Contour OffsetContour(const Contour& contour, float offset)
{
    if (contour.size() < 3) return contour;

    Contour result;
    result.reserve(contour.size());
    int n = (int)contour.size();

    for (int i = 0; i < n; ++i)
    {
        // 前一条边和后一条边的法线取平均
        int prev = (i - 1 + n) % n;
        int next = (i + 1) % n;

        Vec2 e1 = contour[i] - contour[prev];
        Vec2 e2 = contour[next] - contour[i];

        // 内法线：(dy, -dx) 归一化
        Vec2 n1 = {e1.y, -e1.x};
        Vec2 n2 = {e2.y, -e2.x};
        float len1 = n1.length(), len2 = n2.length();
        if (len1 > 1e-6f) { n1.x /= len1; n1.y /= len1; }
        if (len2 > 1e-6f) { n2.x /= len2; n2.y /= len2; }

        // 平均法线
        Vec2 avg = {(n1.x + n2.x) * 0.5f, (n1.y + n2.y) * 0.5f};
        float aLen = avg.length();
        if (aLen > 1e-6f) { avg.x /= aLen; avg.y /= aLen; }

        result.push_back({contour[i].x + avg.x * offset, contour[i].y + avg.y * offset});
    }

    return result;
}

} // namespace fdm
