# 开发环境迁移检查（2026-09-22）

## 结论与验证边界

已修复依赖路径失效，完整 Debug/x64 解决方案与 Release FDM 引擎均构建成功；OCCT 原生 DLL 实际读取 STEP、细分并释放内存成功。WPF 窗口启动和 EngineHost/FDM 的 gRPC 联调因自动审批策略拒绝启动/联调/清理进程的操作而未完成，不能将构建成功等同于完整运行验证。

## 本机工具链

| 项目 | 已核实状态 |
| --- | --- |
| .NET SDK | 已安装 9.0.203、10.0.102；仓库 global.json 选择 9.0.203，兼容现有 VS 2022 |
| 应用目标 | net8.0 / net8.0-windows，未变更 |
| .NET / WPF / ASP.NET Core 8 | 8.0.15 运行时均存在 |
| Visual Studio Community | D:\visual studio，17.13.6，完整解决方案使用其 64 位 MSBuild |
| C++ Build Tools | 17.14.22，MSVC 14.44.35207，可构建 FDM；该实例缺少 .NET SDK resolver，不用于混合解决方案 |
| Windows SDK | 10.0.22621.0 |
| CMake | VS 内置 3.30.5，可通过构建脚本使用，无需全局 PATH |
| vcpkg | I:\file\vcpkg，已恢复用户级 MSBuild 集成并设置用户 VCPKG_ROOT |
| OCCT | 7.9.3，x64-windows |
| FDM 依赖 | protobuf、gRPC、OpenSSL 等已由 CMake 找到并成功链接 |

## 修复内容

1. vcpkg 用户集成原来指向不存在的 F:\vcpkg，导致 OCCT 链接报 TKernel.lib 缺失。重新执行迁移后 vcpkg 的 integrate install。
2. 原生项目的 OCCT 头文件不再固定到 D:\VS TOOL\vcpkg，改用 VcpkgRoot 和 VcpkgTriplet；禁用 vcpkg 自动链接全部库，保留项目显式链接列表，并使用 64 位编译工具。
3. FDM 的旧 CMake 缓存包含 F: 盘源码和依赖路径，已备份缓存并用 --fresh 在原 build 目录重新配置、构建。
4. 增加 global.json 及 scripts/Build-Development.ps1，避免误选不适用的 MSBuild/SDK。

## 实际验证

- 完整解决方案：Visual Studio Community 的 amd64 MSBuild，Debug/x64，还原和构建成功。
- FDM：新路径配置成功，Release 编译链接成功，输出 build/Release/fdm_engine.exe。
- 构建脚本：实际执行成功，退出码 0。
- OCCT：从 WPF 输出目录加载 AMLabSlicer.Occt.Native.dll，以 Supportless_sample.stp 的临时副本调用 LoadStepAndTessellate，返回 true，顶点 1264、索引 6546，随后调用 FreeMeshData。
- 窗口启动、显卡渲染、gRPC 联调、完整切片流程：未验证。

## 日常使用

在解决方案根目录的 PowerShell 中运行：

```powershell
.\scripts\Build-Development.ps1
```

再次迁移目录后，更新 VCPKG_ROOT、执行对应 vcpkg 的 integrate install，再运行脚本的 -Fresh 选项。已有终端/VS 应重开以读取新的用户环境变量。

在 Visual Studio 中选择 Debug/x64，使用 AMLabSlicer + EngineHost 启动配置；手工确认界面打开、导入 STEP、取得参数模板和执行切片。

## 保留事项

- 当前存在项目自身的可空性、MVVM 及 .NET 11 预览依赖与 net8.0 兼容性警告，未通过升级包或修改业务代码消除。
- 原生项目遗留中间目录产生 MSB8028 警告，本次构建仍成功。
- 仓库跟踪了 FDM 的部分 build 生成文件；重新配置/生成 protobuf 会产生这些文件的 Git 差异。没有手工修改生成代码。
- 用户原有及并行发生的业务文件修改未回退。
- 原 vcpkg 集成备份：C:\Users\Administrator\AppData\Local\Temp\amlab-env-backup-20260922-162124。
- 原 CMake 缓存备份：%TEMP%\amlab-CMakeCache-before-migration.txt。
- 构建日志：%TEMP%\amlab-development-build.log、amlab-solution-build.log、amlab-cmake-standard.log。
