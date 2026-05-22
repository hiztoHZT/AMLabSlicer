---
name: amlab-slicer-dev
description: AMLabSlicer project development guidance for WPF/C# UI, CommunityToolkit.Mvvm view models, HelixToolkit rendering, OCCT STEP interop through native P/Invoke, gRPC/protobuf contracts, C++ FDM slicing engine work, CMake builds, and future Clipper/CGAL geometry algorithm integration. Use when modifying or reviewing AMLabSlicer source files, slicer.proto, native interop boundaries, slicing algorithms, viewport/model workflows, engine host routing, or build/debug instructions for this repository.
---

# AMLabSlicer Dev

## Overview

Use this skill to work inside the AMLabSlicer repository without rediscovering the project architecture. Preserve the C#/WPF UI, OCCT native interop, gRPC contract, and C++ slicing engine boundaries unless the task explicitly changes them.

## Project Map

- `AMLabSlicer/`: WPF UI, views, view models, commands, HelixToolkit viewport code.
- `AMLabSlicer.Core/`: shared parameters, command infrastructure, topology/data models.
- `AMLabSlicer.Occt/`: C# OCCT interop facade and Helix mesh conversion.
- `AMLabSlicer.Occt.Native/`: C++ native DLL exporting flat C APIs for STEP loading/tessellation.
- `AMLabSlicer.EngineHost/`: C# gRPC routing host, engine registry, child process management.
- `AMLabSlicer.Engine.FDM/`: C++ FDM slicing engine, CMake project, gRPC server, slicer algorithms.
- `Protos/slicer.proto`: canonical front/back/engine contract. Generated code must follow this file.
- `AMLabSlicer.Engine.FDM/build/generated/`: generated protobuf/gRPC code. Do not hand-edit.

For more detail, read `references/architecture-map.md` when touching cross-boundary behavior.

## Default Workflow

1. Check `git status --short` before editing.
2. Search with `rg` or `rg --files`; ignore generated/build outputs unless diagnosing generation.
3. Identify the boundary first: UI, core model, proto contract, EngineHost, native OCCT DLL, or C++ slicer.
4. Keep edits scoped to that boundary and update adjacent contracts only when required.
5. Prefer existing project patterns: CommunityToolkit attributes in view models, HelixToolkit types in viewport code, flat C exports for native interop, protobuf for engine communication.
6. Build the affected side after changes when feasible.

## Build And Verification

Use x64 for WPF/native interop work. The UI copies native DLLs from `x64/<Configuration>/`.

Common commands:

```powershell
dotnet build AMLabSlicer.sln -p:Platform=x64
dotnet build AMLabSlicer.EngineHost\AMLabSlicer.EngineHost.csproj
cmake -S AMLabSlicer.Engine.FDM -B AMLabSlicer.Engine.FDM\build
cmake --build AMLabSlicer.Engine.FDM\build --config Release
```

If a build fails because generated gRPC files are stale, regenerate through the CMake or MSBuild protobuf configuration rather than editing generated files.

## Boundary Rules

`slicer.proto` is the contract. Any message or enum change can affect WPF client generation, EngineHost routing, and the C++ engine server. Preserve field numbers; add new fields instead of renaming/removing existing ones unless a migration is intentional.

The C++ FDM engine receives packed `float[]` vertices and `int[]` indices through protobuf `bytes`. Keep layouts explicit and little-endian compatible with current `Buffer.BlockCopy` usage.

The OCCT native DLL exports a flat C API. Maintain clear memory ownership: native allocates mesh arrays, C# copies them, C# always calls `FreeMeshData`.

Do not block the WPF UI thread with STEP loading, slicing, or large mesh conversion. Use background work and marshal UI updates through the WPF dispatcher.

EngineHost listens on `http://localhost:50051`; the current FDM engine is registered at `http://localhost:50100`. Keep ports discoverable and avoid hard-coded absolute paths when adding new engines.

## WPF And HelixToolkit

Use CommunityToolkit.Mvvm patterns already present in the repository: `[ObservableProperty]`, `[RelayCommand]`, and explicit dispatcher use for UI collection updates.

Viewport features should preserve scene graph transform correctness. When converting mesh data for slicing, use world-space vertices and include parent node transforms.

Avoid UI text that explains implementation details to users. Put operational guidance in docs, comments, or tooltips only when it helps actual use.

## Slicing And Geometry

Treat current C++ FDM code as an initial pipeline: mesh preprocessing, z layers, triangle-plane cutting, contour building, wall offsets, infill, G-code. Do not expand fragile hand-rolled geometry unless the task is exploratory.

For robust polygon offset/boolean operations, prefer Clipper/Clipper2. For 3D topology, mesh repair, surface intersections, and robust computational geometry, prefer CGAL or OCCT capabilities where appropriate.

When adding algorithms, define parameters through `GetAlgorithmParameters` so the WPF parameter UI remains dynamic. Keep parameter keys stable and use string values at the gRPC boundary.

## Common Pitfalls

- Do not edit `.pb.cc`, `.pb.h`, or `.grpc.pb.h` generated files directly.
- Do not mix AnyCPU UI assumptions with native x64 DLL loading.
- Do not add new geometry binary layouts without documenting packing, element type, and coordinate space.
- Do not silently change model units; assume millimeters unless a task states otherwise.
- Do not let EngineHost own algorithm logic; it should route, register, launch, and forward engine messages.
- Do not use broad string parsing for proto or project files when structured build/proto tooling is available.
