// ----------------------------------------------------------------
// AMLabSlicer.Engine  -  OCCT STEP Loader & Tessellator
// ----------------------------------------------------------------
// This file does NOT use precompiled headers (pch.h).
// Set PrecompiledHeader to NotUsing in vcxproj for this file.

#include "SlicerEngine.h"

// -- OCCT Headers ------------------------------------------------
#include <STEPControl_Reader.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <BRep_Tool.hxx>
#include <Poly_Triangulation.hxx>
#include <TopLoc_Location.hxx>
#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Vec.hxx>
#include <gp_Trsf.hxx>
#include <BRepGProp_Face.hxx>
#include <CSLib_DerivativeStatus.hxx>
#include <CSLib_NormalStatus.hxx>
#include <CSLib.hxx>
#include <Geom_Surface.hxx>
#include <BRep_Builder.hxx>

// -- Standard Library --------------------------------------------
#include <vector>
#include <string>
#include <cstring>
#include <cmath>

// -- Thread-safe error buffer ------------------------------------
static thread_local char g_lastError[1024] = {};

static void SetError(const char* msg)
{
    strncpy_s(g_lastError, msg, _TRUNCATE);
}

// ----------------------------------------------------------------
// GetLastEngineError
// ----------------------------------------------------------------
ENGINE_API const char* GetLastEngineError()
{
    return g_lastError;
}

// ----------------------------------------------------------------
// FreeMeshData
// ----------------------------------------------------------------
ENGINE_API void FreeMeshData(float* vertices, float* normals, int* indices)
{
    if (vertices) delete[] vertices;
    if (normals)  delete[] normals;
    if (indices)  delete[] indices;
}

// ----------------------------------------------------------------
// Helper: compute cross product normal from 3 vertices (CCW order)
// Returns normalized (nx, ny, nz). Returns (0,0,1) on degenerate input.
// ----------------------------------------------------------------
static void ComputeFaceNormal(
    float ax, float ay, float az,
    float bx, float by, float bz,
    float cx, float cy, float cz,
    float& nx, float& ny, float& nz)
{
    float ux = bx - ax, uy = by - ay, uz = bz - az;
    float vx = cx - ax, vy = cy - ay, vz = cz - az;
    nx = uy * vz - uz * vy;
    ny = uz * vx - ux * vz;
    nz = ux * vy - uy * vx;
    float len = std::sqrt(nx * nx + ny * ny + nz * nz);
    if (len > 1e-10f) { nx /= len; ny /= len; nz /= len; }
    else              { nx = 0.f;  ny = 0.f;  nz = 1.f; }
}

// ----------------------------------------------------------------
// LoadStepAndTessellate
// ----------------------------------------------------------------
ENGINE_API bool LoadStepAndTessellate(
    const char* filePath,
    double      linearDeflection,
    double      angularDeflection,
    float**     outVertices,
    int*        outVertexCount,
    float**     outNormals,
    int**       outIndices,
    int*        outIndexCount)
{
    // -- Parameter validation ------------------------------------
    if (!filePath || !outVertices || !outVertexCount ||
        !outNormals || !outIndices || !outIndexCount)
    {
        SetError("Invalid null parameter(s).");
        return false;
    }

    // Initialize outputs
    *outVertices    = nullptr;
    *outVertexCount = 0;
    *outNormals     = nullptr;
    *outIndices     = nullptr;
    *outIndexCount  = 0;
    g_lastError[0]  = '\0';

    // -- 1. Read STEP file ---------------------------------------
    STEPControl_Reader reader;
    IFSelect_ReturnStatus status = reader.ReadFile(filePath);
    if (status != IFSelect_RetDone)
    {
        SetError("STEPControl_Reader::ReadFile failed. Check file path and format.");
        return false;
    }

    // Transfer all root entities
    Standard_Integer nbRoots = reader.TransferRoots();
    if (nbRoots <= 0)
    {
        SetError("No transferable roots found in STEP file.");
        return false;
    }

    TopoDS_Shape shape = reader.OneShape();
    if (shape.IsNull())
    {
        SetError("Resulting shape is null after transfer.");
        return false;
    }

    // -- 2. Tessellate -------------------------------------------
    // Deflection values: larger = faster/coarser, smaller = slower/finer
    // For slicer preview, 0.1 mm linear / 0.5 rad angular is a good balance.
    if (linearDeflection  <= 0.0) linearDeflection  = 0.1;
    if (angularDeflection <= 0.0) angularDeflection = 0.5;

    BRepMesh_IncrementalMesh mesher(shape, linearDeflection,
                                    Standard_False,   // isRelative
                                    angularDeflection,
                                    Standard_True);   // isParallel (multi-thread tessellation)
    mesher.Perform();
    // Note: IsDone() may return false but still produce partial geometry; continue anyway.

    // -- 3. Iterate faces, collect triangle data -----------------
    // Strategy:
    //   a) Extract per-vertex positions and indices from Poly_Triangulation.
    //   b) For normals: try tri->HasNormals() first (detailed mesh from some STEP exporters).
    //      If not available, compute face normal analytically via BRepGProp_Face (UV surface
    //      normal at centroid), or fall back to cross-product of triangle edges.
    //   c) Apply only the ROTATION part of Location to normals (not scale/translation).

    std::vector<float> allVertices;
    std::vector<float> allNormals;
    std::vector<int>   allIndices;

    allVertices.reserve(1 << 17);   // 128K floats initial capacity
    allNormals.reserve(1 << 17);
    allIndices.reserve(1 << 17);

    int globalVertexOffset = 0;

    for (TopExp_Explorer faceExp(shape, TopAbs_FACE); faceExp.More(); faceExp.Next())
    {
        const TopoDS_Face& face = TopoDS::Face(faceExp.Current());
        TopLoc_Location location;

        Handle(Poly_Triangulation) tri = BRep_Tool::Triangulation(face, location);
        if (tri.IsNull()) continue;

        const int nbNodes     = tri->NbNodes();
        const int nbTriangles = tri->NbTriangles();
        if (nbNodes == 0 || nbTriangles == 0) continue;

        // Face orientation: REVERSED faces need flipped normals and winding
        bool faceReversed = (face.Orientation() == TopAbs_REVERSED);

        // Rotation-only transform for normals.
        // gp_Trsf::GetRotation gives the pure rotation component,
        // so normals stay unit-length after transform.
        gp_Trsf trsf = location.IsIdentity() ? gp_Trsf() : location.IsIdentity() ? gp_Trsf() : location.Transformation();
        bool hasLocTrsf = !location.IsIdentity();

        // -- Extract vertex positions -----------------------------
        const int vertexBase = static_cast<int>(allVertices.size()) / 3;
        for (int i = 1; i <= nbNodes; ++i)
        {
            gp_Pnt pt = tri->Node(i);
            if (hasLocTrsf) pt.Transform(trsf);

            allVertices.push_back(static_cast<float>(pt.X()));
            allVertices.push_back(static_cast<float>(pt.Y()));
            allVertices.push_back(static_cast<float>(pt.Z()));
        }

        // -- Extract normals -------------------------------------
        bool normalsWritten = false;

        if (tri->HasNormals())
        {
            // Path A: use per-node normals from triangulation
            normalsWritten = true;
            for (int i = 1; i <= nbNodes; ++i)
            {
                gp_Dir n = tri->Normal(i);
                if (faceReversed) n.Reverse();

                // Apply only rotation (no scale/translation) to normal
                if (hasLocTrsf)
                {
                    // Use transform's rotation part only
                    gp_Vec nv(n.X(), n.Y(), n.Z());
                    nv.Transform(trsf);  // gp_Vec::Transform applies rotation+scale but not translation
                    double len = nv.Magnitude();
                    if (len > 1e-10) nv /= len;
                    allNormals.push_back(static_cast<float>(nv.X()));
                    allNormals.push_back(static_cast<float>(nv.Y()));
                    allNormals.push_back(static_cast<float>(nv.Z()));
                }
                else
                {
                    allNormals.push_back(static_cast<float>(n.X()));
                    allNormals.push_back(static_cast<float>(n.Y()));
                    allNormals.push_back(static_cast<float>(n.Z()));
                }
            }
        }

        if (!normalsWritten)
        {
            // Path B: compute per-triangle face normal via cross product,
            // then assign the same flat normal to all 3 vertices of each triangle.
            // We need to compute normals per-triangle and store per-vertex.
            // Allocate placeholder normals first (same count as vertices).
            size_t normalStart = allNormals.size();
            allNormals.resize(normalStart + static_cast<size_t>(nbNodes) * 3, 0.0f);

            for (int i = 1; i <= nbTriangles; ++i)
            {
                Standard_Integer n1, n2, n3;
                tri->Triangle(i).Get(n1, n2, n3);

                // Vertex positions (already transformed, stored in allVertices)
                int base = (globalVertexOffset + (n1 - 1)) * 3;
                float ax = allVertices[base],     ay = allVertices[base + 1],     az = allVertices[base + 2];
                base = (globalVertexOffset + (n2 - 1)) * 3;
                float bx = allVertices[base],     by = allVertices[base + 1],     bz = allVertices[base + 2];
                base = (globalVertexOffset + (n3 - 1)) * 3;
                float cx = allVertices[base],     cy = allVertices[base + 1],     cz = allVertices[base + 2];

                float nx, ny, nz;
                if (faceReversed)
                    ComputeFaceNormal(ax, ay, az, cx, cy, cz, bx, by, bz, nx, ny, nz);
                else
                    ComputeFaceNormal(ax, ay, az, bx, by, bz, cx, cy, cz, nx, ny, nz);

                // Accumulate: add this triangle's normal to each of its vertices
                // (simple average: sum then normalize later)
                for (int vi : {n1 - 1, n2 - 1, n3 - 1})
                {
                    size_t nIdx = normalStart + static_cast<size_t>(vi) * 3;
                    allNormals[nIdx]     += nx;
                    allNormals[nIdx + 1] += ny;
                    allNormals[nIdx + 2] += nz;
                }
            }

            // Normalize accumulated normals
            for (int i = 0; i < nbNodes; ++i)
            {
                size_t nIdx = normalStart + static_cast<size_t>(i) * 3;
                float nx = allNormals[nIdx], ny = allNormals[nIdx + 1], nz = allNormals[nIdx + 2];
                float len = std::sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-10f) { allNormals[nIdx] /= len; allNormals[nIdx + 1] /= len; allNormals[nIdx + 2] /= len; }
                else              { allNormals[nIdx] = 0.f; allNormals[nIdx + 1] = 0.f; allNormals[nIdx + 2] = 1.f; }
            }

            normalsWritten = true;
        }

        // -- Extract triangle indices ----------------------------
        for (int i = 1; i <= nbTriangles; ++i)
        {
            Standard_Integer n1, n2, n3;
            tri->Triangle(i).Get(n1, n2, n3);

            // OCCT uses 1-based indices, convert to 0-based + global offset
            if (faceReversed)
            {
                allIndices.push_back(globalVertexOffset + (n1 - 1));
                allIndices.push_back(globalVertexOffset + (n3 - 1));
                allIndices.push_back(globalVertexOffset + (n2 - 1));
            }
            else
            {
                allIndices.push_back(globalVertexOffset + (n1 - 1));
                allIndices.push_back(globalVertexOffset + (n2 - 1));
                allIndices.push_back(globalVertexOffset + (n3 - 1));
            }
        }

        globalVertexOffset += nbNodes;
    }

    // -- 4. Validate output --------------------------------------
    if (allVertices.empty() || allIndices.empty())
    {
        SetError("Tessellation produced no geometry data.");
        return false;
    }

    // -- 5. Allocate output memory -------------------------------
    const int vertexCount = globalVertexOffset;
    const int indexCount  = static_cast<int>(allIndices.size());

    *outVertexCount = vertexCount;
    *outIndexCount  = indexCount;

    // Vertex array (vertexCount * 3 floats)
    *outVertices = new (std::nothrow) float[allVertices.size()];
    if (!*outVertices)
    {
        SetError("Failed to allocate memory for vertices.");
        return false;
    }
    std::memcpy(*outVertices, allVertices.data(), allVertices.size() * sizeof(float));

    // Normal array (vertexCount * 3 floats)
    *outNormals = new (std::nothrow) float[allNormals.size()];
    if (!*outNormals)
    {
        delete[] *outVertices;
        *outVertices = nullptr;
        SetError("Failed to allocate memory for normals.");
        return false;
    }
    std::memcpy(*outNormals, allNormals.data(), allNormals.size() * sizeof(float));

    // Index array
    *outIndices = new (std::nothrow) int[indexCount];
    if (!*outIndices)
    {
        delete[] *outVertices;
        delete[] *outNormals;
        *outVertices = nullptr;
        *outNormals  = nullptr;
        SetError("Failed to allocate memory for indices.");
        return false;
    }
    std::memcpy(*outIndices, allIndices.data(), indexCount * sizeof(int));

    return true;
}
