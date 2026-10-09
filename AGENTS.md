# AMLabSlicer Agent Instructions

本文件是 AMLabSlicer 仓库的 Codex 个性化入口。进入本仓库工作时，优先遵守这些项目规则。

## 语言与输出

- 用户使用中文时，默认用中文回答。
- 回答尽量贴近真实代码、真实配置、真实端口和当前实现边界。
- 说明架构或排查问题时，优先按 `proto/service contract -> UI client -> EngineHost/router -> C++ engine` 的链路组织。
- 区分“UI/模板里有这个参数”和“核心算法实际消费这个参数”，不要把占位或未接线能力描述成已完成能力。

## 项目边界

- `Protos/slicer.proto` 是前后端和引擎通信契约。保留字段号，兼容演进时新增字段，不随意重命名或删除。
- WPF UI 不直接调用 C++ FDM engine，默认链路是 `WPF UI -> EngineHost localhost:50051 -> C++ FDM engine localhost:50100`。
- EngineHost 负责路由、注册、启动和转发，不承载切片算法逻辑。
- OCCT native interop 保持明确所有权：native 分配，C# 复制，C# 调用释放 API。
- 生成的 protobuf/gRPC 文件和 build 输出不是手工编辑目标。

## UI 设计标准

- 新增或修改 WPF 界面前，阅读并遵守 `docs/UI_DESIGN_STANDARD.md`。
- 采用紧凑深色卡片布局，卡片和常规控件圆角统一为 5，默认单行输入高度为 26、参数行高度为 32。
- 颜色与共用尺寸集中在主题资源中维护；保持 MVVM、既有绑定与业务行为。
- 如需调整设计基准，同步更新设计标准和相关共用样式，避免各视图使用不同规范。

## 常用入口

- `Protos/slicer.proto`：服务契约、参数模板、流式切片消息。
- `AMLabSlicer/ViewModel/PrepareWorkspaceViewModel.cs`：UI 侧切片流程、参数和 mesh 打包。
- `AMLabSlicer/Views/ParameterPanelView.xaml`：参数面板布局。
- `AMLabSlicer.EngineHost/Program.cs`：Host 监听、引擎注册、子进程启动与转发。
- `AMLabSlicer.EngineHost/Properties/launchSettings.json` 与 `AMLabSlicer.slnLaunch`：启动配置和端口链路。
- `AMLabSlicer.Engine.FDM/main.cpp`：C++ gRPC server wrapper、参数解析、mesh 解包。
- `AMLabSlicer.Engine.FDM/slicer/`：FDM 切片核心模块。
- `AMLabSlicer.Occt/OcctInteropService.cs` 与 `AMLabSlicer.Occt.Native/SlicerEngine.*`：STEP 导入和 native 内存边界。

## 工作流程

1. 改文件前先看 `git status --short`，保护用户已有未提交改动。
2. 搜索优先用 `rg` / `rg --files`，默认排除 `build/`、`bin/`、`obj/`。
3. 先判断任务属于 UI、参数、proto、EngineHost、OCCT interop、还是 C++ FDM 算法边界。
4. 变更保持小范围；只有契约变化确实需要时才同步更新相邻层。
5. 对 WPF 代码沿用 CommunityToolkit.Mvvm、Dispatcher、HelixToolkit 等既有模式。
6. 对几何算法，简单基线可以保留；需要鲁棒偏置/布尔时优先考虑 Clipper/Clipper2，需要 3D 拓扑/曲面能力时考虑 CGAL 或 OCCT。

## 验证

按改动范围选择验证命令：

```powershell
dotnet build AMLabSlicer.EngineHost\AMLabSlicer.EngineHost.csproj
dotnet build AMLabSlicer.UI.csproj
dotnet build AMLabSlicer.sln -p:Platform=x64
cmake -S AMLabSlicer.Engine.FDM -B AMLabSlicer.Engine.FDM\build
cmake --build AMLabSlicer.Engine.FDM\build --config Release
```

完整 solution build 可能受 native VC++ MSBuild 环境影响；如果失败，要说明失败边界，不要把环境问题误判为业务代码问题。

## Agent 配套资料

- MCP 建议与示例：`docs/agent/MCP_AND_SKILLS.md`
- MCP JSON：`docs/agent/mcp-servers.example.json`
- 项目 skill 草案：`docs/agent/skills/amlab-slicer-dev/SKILL.md`
- 用户级已安装 skill：`C:\Users\Administrator\.codex\skills\amlab-slicer-dev`
