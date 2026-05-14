#pragma once
#include "FdmSlicer.h"

namespace fdm {

// 生成直线填充
// 在 contour 构成的区域内，以 angle 度角和 spacing 间距生成平行扫描线
std::vector<Segment> GenerateLineInfill(
    const std::vector<Contour>& contours,
    float spacing,
    float angle);

} // namespace fdm
