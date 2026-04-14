#pragma once

// ----------------------------------------------------------------
// AMLabSlicer.Engine  -  C-API Export Header
// 
// Flat C interface for STEP file loading + B-Rep tessellation.
// Called by C# via P/Invoke.
// ----------------------------------------------------------------

#ifdef AMLABSLICERENGINE_EXPORTS
#define ENGINE_API extern "C" __declspec(dllexport)
#else
#define ENGINE_API extern "C" __declspec(dllimport)
#endif

/// Load a STEP file and tessellate it into a triangle mesh.
///
/// Output data layout:
///   outVertices : [x0,y0,z0, x1,y1,z1, ...] -- outVertexCount * 3 floats
///   outNormals  : [nx0,ny0,nz0, ...]         -- outVertexCount * 3 floats
///   outIndices  : [i0,i1,i2, ...]            -- outIndexCount ints (3 per triangle)
///
/// Memory is allocated by C++ using new[].
/// Caller MUST call FreeMeshData() after copying the data.
///
/// @param filePath          UTF-8 encoded file path
/// @param linearDeflection  Linear deflection (mm), smaller = finer, recommended 0.1
/// @param angularDeflection Angular deflection (radians), recommended 0.5
/// @param outVertices       Output vertex array pointer
/// @param outVertexCount    Output vertex count (not float count)
/// @param outNormals        Output normal array pointer
/// @param outIndices        Output index array pointer
/// @param outIndexCount     Output index count
/// @return true on success, false on failure
ENGINE_API bool LoadStepAndTessellate(
    const char* filePath,
    double      linearDeflection,
    double      angularDeflection,
    float**     outVertices,
    int*        outVertexCount,
    float**     outNormals,
    int**       outIndices,
    int*        outIndexCount
);

/// Free memory allocated by LoadStepAndTessellate.
/// Passing nullptr is safe (checked internally).
ENGINE_API void FreeMeshData(
    float* vertices,
    float* normals,
    int*   indices
);

/// Get the error message from the last failed operation (UTF-8 string).
/// Returns a pointer to an internal static buffer - do not free.
ENGINE_API const char* GetLastEngineError();
