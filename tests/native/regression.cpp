#include "FdmSlicer.h"
#include <iostream>
#include <limits>
#include <stdexcept>

namespace fdm {
std::string SliceMesh(const float*, int, const int*, int, const SliceParams&, ProgressCallback);
}

int main()
{
    float vertices[] = {0,0,0, 10,0,0, 0,10,0, 0,0,10};
    int indices[] = {0,2,1, 0,1,3, 1,2,3, 2,0,3};
    int failures = 0;
    auto rejects = [&](const char* name, const fdm::SliceParams& params) {
        try {
            fdm::SliceMesh(vertices, 4, indices, 12, params, {});
            std::cerr << "FAIL " << name << '\n'; ++failures;
        } catch (const std::invalid_argument&) { std::cout << "PASS " << name << '\n'; }
    };
    fdm::SliceParams params;
    auto result = fdm::SliceMesh(vertices, 4, indices, 12, params, {});
    if (result.find(";LAYER:") == std::string::npos) ++failures;
    else std::cout << "PASS valid tetrahedron produces layers\n";
    params.layerHeight = 0; rejects("zero layer height", params);
    params.layerHeight = -1; rejects("negative layer height", params);
    params.layerHeight = std::numeric_limits<float>::quiet_NaN(); rejects("NaN layer height", params);
    params = {}; params.layerHeight = 0.000001f; rejects("excessive layer count", params);
    params = {}; params.filamentDiameter = 0; rejects("zero filament diameter", params);
    params = {}; indices[0] = -1; rejects("negative index", params);
    indices[0] = 4; rejects("out-of-range index", params);
    indices[0] = 0; vertices[0] = std::numeric_limits<float>::infinity(); rejects("non-finite vertex", params);
    return failures == 0 ? 0 : 1;
}
