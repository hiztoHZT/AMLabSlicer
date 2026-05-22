# AMLabSlicer Agent MCP 与 Skill 建议

本文面向参与 AMLabSlicer 开发的 AI agent。项目当前是 WPF/C# 上位机、HelixToolkit 渲染、OCCT native STEP 支持、gRPC 前后端通信、C++ FDM 切片引擎的混合仓库，后续会引入 Clipper/CGAL 等几何算法库。

## 结论

建议先安装少量高价值 MCP，不要把所有看起来有用的 MCP 都接进来。这个项目真正需要的是：可靠读写仓库、理解 Git 变更、查官方/库文档、保留项目约定、必要时访问 GitHub 协作信息。

已在仓库内新增项目 skill 草案：

`docs/agent/skills/amlab-slicer-dev/SKILL.md`

该 skill 适合在处理 UI、OCCT、gRPC、C++ 切片、算法演进任务时显式调用。若后续项目规模继续变大，再拆成更细的领域 skill。

可直接复制的 MCP JSON 示例：

`docs/agent/mcp-servers.example.json`

使用前需要把 `github.env.GITHUB_PERSONAL_ACCESS_TOKEN` 替换成自己的 GitHub PAT，或按客户端规则改成环境变量引用。`figma-dev-mode` 只有在 Figma Desktop 已打开文件、切到 Dev Mode、并启用 MCP server 后才会连接成功。`git` 依赖 `uvx`，`filesystem`、`memory`、`sequential-thinking` 依赖 Node.js/npm，`github` 示例使用 Docker 运行官方 GitHub MCP Server。

## MCP 安装优先级

### P0 必装

1. `filesystem` MCP

用途：让 agent 稳定读取、搜索、编辑仓库文件。建议只授权到本仓库根目录：

`F:\2026.3\AMLabSlicer\AMLabSlicer`

当前 Codex 已有本地文件访问能力时，这个 MCP 可以不重复安装；但如果换到 Claude Desktop、Cursor、Cline、Roo 等 MCP 客户端，filesystem 是基础能力。

2. `git` MCP

用途：查看 diff、历史、分支、提交边界，避免 agent 覆盖用户未提交改动。这个项目多语言、多生成物，必须让 agent 在改动前能识别哪些文件是用户改的，哪些是生成的。

3. `github` MCP

用途：如果项目托管在 GitHub，用于读取 issue、PR、review、CI 结果、发布计划。它对后续管理算法模块、UI 任务、bug 复现很有价值。

4. `microsoft-learn` MCP

用途：查 .NET、WPF、C#、MSBuild、gRPC for .NET 的官方文档。该项目依赖 `net8.0-windows`、WPF、Grpc.Net.Client、Grpc.AspNetCore、CommunityToolkit.Mvvm，官方文档比泛化搜索更稳。

### P1 推荐

5. `context7` MCP

用途：查第三方库的版本化文档和示例，尤其是 HelixToolkit、CommunityToolkit.Mvvm、gRPC、Clipper2、CGAL、OCCT 相关用法。使用时仍要优先核对本仓库里的实际包版本和 CMake/vcpkg 配置。

6. `memory` 或知识库 MCP

用途：保存项目约定，例如坐标系、单位、gRPC 端口、算法命名、参数 key、STEP 细分默认值、五轴运动学术语。适合长期多人协作，避免每次从零解释。

7. `sequential-thinking` MCP

用途：用于五轴切片、曲面分层、路径规划、轮廓偏置、拓扑修复这类需要拆解推理的任务。普通 UI 修 bug 不需要调用。

### P2 按需

8. Windows UI Automation / WinAppDriver 类 MCP

用途：只有在要自动化验证 WPF 窗口、菜单、参数面板、模型导入流程时再装。若只是代码修改，终端构建和人工运行更直接。

9. Figma MCP

用途：只有当 UI 有 Figma 设计稿，且需要 agent 读取设计系统或页面标注时再装。

10. CMake/vcpkg 包管理类 MCP

用途：如果团队已有私有 MCP 来管理 C++ 依赖、vcpkg manifest、预编译包，可安装。否则终端命令足够，单独引入 MCP 收益不高。

## 不建议优先安装

- 数据库 MCP：当前项目没有数据库边界。
- Playwright MCP：主要面向 Web，不能直接验证 WPF 桌面 UI。
- Slack/Teams/Notion/Jira：除非团队协作流程已经在那里，否则先不用接入。
- 过宽权限的 shell/command MCP：该项目有 native DLL、生成代码、build 目录，过宽执行权限容易误删或污染构建产物。

## 推荐权限边界

filesystem MCP 只给仓库根目录，不给整个磁盘。

git/GitHub MCP 允许读取和创建 PR，是否允许 push/merge 应由人工控制。

memory MCP 只存项目工程约定，不存密钥、私有路径、个人 token、客户模型文件。

docs/search MCP 查询外部资料时优先官方来源：Microsoft Learn、GitHub 官方仓库、库官方文档。

## Skill 规划

当前先使用一个主 skill：

`amlab-slicer-dev`

它覆盖：

- WPF + MVVM + HelixToolkit UI 开发
- OCCT native DLL 与 C# P/Invoke 边界
- `Protos/slicer.proto` gRPC 契约
- C# EngineHost 路由与进程管理
- C++ FDM engine、CMake、protobuf/gRPC 生成代码
- 后续 Clipper/CGAL 几何算法接入注意事项

后续当某类任务反复出现，再拆成独立 skill：

- `slicer-geometry-algorithms`：轮廓拼接、偏置、布尔、填充、曲面切片、CGAL/Clipper 选型。
- `wpf-helix-viewport`：相机、选择、变换、Gizmo、模型树、参数面板、渲染性能。
- `grpc-engine-contract`：proto 演进、前后端兼容、流式消息、引擎注册、取消任务。
- `occt-step-interop`：STEP 读取、B-Rep 细分、native 内存所有权、x64 DLL 部署。

## 参考来源

- Model Context Protocol servers: https://github.com/modelcontextprotocol/servers
- GitHub MCP Server: https://github.com/github/github-mcp-server
- Microsoft Learn MCP Server: https://learn.microsoft.com/en-us/training/support/mcp
- Context7 MCP: https://github.com/upstash/context7
