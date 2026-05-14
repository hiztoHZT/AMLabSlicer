#pragma once
#include "FdmSlicer.h"
#include <string>
#include <sstream>

namespace fdm {

// 将切片结果写成 G-Code 字符串
std::string GenerateGCode(
    const std::vector<SliceLayer>& layers,
    const SliceParams& params);

} // namespace fdm
