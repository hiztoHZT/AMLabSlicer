#pragma once
#include <vector>
#include <string>
#include <cmath>
#include <algorithm>
#include <functional>

namespace fdm {

// 二维点
struct Vec2 {
    float x, y;
    Vec2() : x(0), y(0) {}
    Vec2(float x, float y) : x(x), y(y) {}
    Vec2 operator-(const Vec2& o) const { return {x - o.x, y - o.y}; }
    Vec2 operator+(const Vec2& o) const { return {x + o.x, y + o.y}; }
    Vec2 operator*(float s) const { return {x * s, y * s}; }
    float dot(const Vec2& o) const { return x * o.x + y * o.y; }
    float length() const { return std::sqrt(x * x + y * y); }
};

// 三维点
struct Vec3 {
    float x, y, z;
    Vec3() : x(0), y(0), z(0) {}
    Vec3(float x, float y, float z) : x(x), y(y), z(z) {}
};

// 三角形（引用顶点数组的三个索引）
struct Triangle {
    Vec3 v[3];
    float zMin, zMax;
};

// 一条线段（平面切割三角形的结果）
struct Segment {
    Vec2 a, b;
};

// 闭合轮廓（有序点环）
using Contour = std::vector<Vec2>;

// 一层的所有数据
struct SliceLayer {
    float z;
    int layerIndex;
    std::vector<Contour> outerContours;  // 外壁轮廓
    std::vector<Contour> innerContours;  // 内壁（偏移后）
    std::vector<Segment> infillLines;    // 填充线
};

// 切片参数
struct SliceParams {
    float layerHeight      = 0.2f;
    float initialLayerHeight = 0.3f;
    float lineWidth        = 0.4f;
    int   wallLineCount    = 3;
    int   topLayers        = 4;
    int   bottomLayers     = 4;
    float infillDensity    = 20.0f;   // 百分比
    std::string infillPattern = "lines";
    float speedPrint       = 60.0f;   // mm/s
    float speedTravel      = 120.0f;
    float speedLayer0      = 20.0f;
    float nozzleTemp       = 200.0f;
    float bedTemp          = 60.0f;
    bool  retractionEnable = true;
    float retractionAmount = 5.0f;
    float retractionSpeed  = 45.0f;
    float nozzleSize       = 0.4f;
    float filamentDiameter = 1.75f;
    float fanSpeed         = 100.0f;
};

// 进度回调
using ProgressCallback = std::function<void(float progress, const std::string& stage)>;

} // namespace fdm
