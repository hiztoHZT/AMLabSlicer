#pragma once
#include "FdmSlicer.h"
#include <unordered_map>

namespace fdm {

// 用 Z 平面切割三角网格，产生线段集
std::vector<Segment> CutMeshAtZ(
    const std::vector<Triangle>& triangles,
    float z);

// 将零散线段连接成闭合轮廓环
std::vector<Contour> BuildContours(const std::vector<Segment>& segments);

// 简易向内偏移轮廓（每条边沿法线方向内缩 offset 距离）
Contour OffsetContour(const Contour& contour, float offset);

} // namespace fdm
