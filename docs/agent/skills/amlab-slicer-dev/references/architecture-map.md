# AMLabSlicer Architecture Map

Use this reference when a task crosses UI, engine, proto, native interop, or geometry algorithm boundaries.

## Runtime Shape

The intended runtime chain is:

```text
WPF UI
  -> gRPC client generated from Protos/slicer.proto
  -> AMLabSlicer.EngineHost on http://localhost:50051
  -> registered slicing engine, currently C++ FDM on http://localhost:50100
  -> SliceServerMessage stream back to UI
```

STEP import uses a separate native path:

```text
WPF/UI or service code
  -> AMLabSlicer.Occt.OcctInteropService
  -> AMLabSlicer.Occt.Native.dll flat C exports
  -> OCCT STEP read and tessellation
  -> HelixToolkit MeshGeometry3D
```

## Contracts

`Protos/slicer.proto` is the source of truth for:

- algorithm discovery
- dynamic slicing parameter templates
- mesh object transfer
- five-axis configuration
- bidirectional slicing stream
- progress, log, layer preview, and final G-code result messages

Preserve protobuf field numbers. Add fields for compatible evolution.

## Native Interop

`AMLabSlicer.Occt.Native/SlicerEngine.h` exposes:

- `LoadStepAndTessellate`
- `FreeMeshData`
- `GetLastEngineError`

C# copies unmanaged arrays into managed arrays and must always free native memory in `finally`.

## C++ Engine

`AMLabSlicer.Engine.FDM/CMakeLists.txt` generates protobuf C++ files into `build/generated`. The executable links `gRPC::grpc++` and `protobuf::libprotobuf`.

The current C++ FDM slicing flow is:

```text
vertices/indices
  -> Triangle list
  -> z-height layer list
  -> CutMeshAtZ
  -> BuildContours
  -> OffsetContour
  -> GenerateLineInfill
  -> GenerateGCode
```

When replacing hand-rolled geometry:

- Use Clipper/Clipper2 for 2D polygon offset, clipping, booleans, and cleanup.
- Use CGAL for robust 3D mesh/surface operations when OCCT is not the better fit.
- Keep library choices isolated behind small algorithm modules so the gRPC contract stays stable.

## Build Notes

Use x64 when native DLLs are involved.

```powershell
dotnet build AMLabSlicer.sln -p:Platform=x64
cmake -S AMLabSlicer.Engine.FDM -B AMLabSlicer.Engine.FDM\build
cmake --build AMLabSlicer.Engine.FDM\build --config Release
```

If adding proto fields, rebuild both C# and C++ sides.

## File Ownership Hints

- UI workflow changes usually involve `AMLabSlicer/Views`, `AMLabSlicer/ViewModel`, and `AMLabSlicer/Core`.
- Parameter changes usually involve `Protos/slicer.proto`, engine parameter generation, and UI parameter binding.
- STEP changes usually involve `AMLabSlicer.Occt`, `AMLabSlicer.Occt.Native`, and native DLL copy/build behavior.
- FDM algorithm changes usually involve `AMLabSlicer.Engine.FDM/slicer`.
- Engine routing changes usually involve `AMLabSlicer.EngineHost/Program.cs` and proto compatibility.
