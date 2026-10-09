# AMLabSlicer Agent MCP 与 Skill 配置

本文面向参与 AMLabSlicer 开发的 AI agent。项目当前是 WPF/C# 上位机、HelixToolkit 渲染、OCCT native STEP 支持、gRPC 前后端通信、C++ FDM 切片引擎的混合仓库，后续可能接入 Clipper/CGAL 等几何算法库。

## 当前状态

- 项目内已有 MCP 示例配置：`docs/agent/mcp-servers.example.json`
- 项目内已有 skill 草案：`docs/agent/skills/amlab-slicer-dev/SKILL.md`
- 用户级 Codex skill 已安装：`C:\Users\Administrator\.codex\skills\amlab-slicer-dev`
- 当前仓库根目录已补充 Codex 个性化入口：`AGENTS.md`

注意：`mcp-servers.example.json` 是可复制的 MCP 配置样例，不等于所有 MCP 已经在当前 Codex 运行时启用。启用时按所用客户端要求导入该 JSON，GitHub token 需要替换为自己的凭据。

## 推荐 MCP

### P0：优先启用

1. `filesystem`
   - 用途：读写 AMLabSlicer 仓库文件。
   - 建议权限：只授权 `F:\2026.3\AMLabSlicer\AMLabSlicer`，不要授权整盘。

2. `git`
   - 用途：查看 diff、历史、分支、未提交变更。
   - 原因：本仓库有 C#、C++、proto、生成文件和文档，改动前必须区分用户已有改动和 agent 新改动。

3. `github`
   - 用途：读取 issue、PR、review、CI 结果，必要时创建 PR。
   - 适用场景：多人协作、代码评审、CI 失败排查。

4. `microsoft-learn`
   - 用途：查询 .NET、WPF、C#、MSBuild、gRPC for .NET 官方资料。
   - 原因：项目依赖 `net8.0-windows`、WPF、`Grpc.Net.Client`、`Grpc.AspNetCore`、`CommunityToolkit.Mvvm`。

### P1：推荐启用

5. `context7`
   - 用途：查询第三方库版本化文档，例如 HelixToolkit、CommunityToolkit.Mvvm、gRPC、Clipper2、CGAL、OCCT。
   - 使用时仍要优先核对本仓库实际包版本、CMake 配置和 vcpkg 配置。

6. `memory`
   - 用途：保存项目长期约定，例如端口、参数 key、单位、坐标系、proto 演进原则。
   - 不要存 token、私有路径、客户模型文件或敏感数据。

7. `sequential-thinking`
   - 用途：处理多轴切片、曲面分层、路径规划、轮廓偏置、拓扑修复等需要拆解推理的任务。
   - 普通 UI bug 或简单代码修改不需要优先调用。

### P2：按需启用

8. `figma-dev-mode`
   - 只有存在 Figma 设计稿、且需要读取设计系统或页面标注时再启用。
   - 需要 Figma Desktop 打开文件、切到 Dev Mode，并启用本地 MCP server。

9. Windows UI Automation / WinAppDriver 类 MCP
   - 只有需要自动化验证 WPF 桌面窗口、菜单、参数面板、模型导入流程时再启用。

10. CMake/vcpkg 类 MCP
   - 只有团队已有成熟 MCP 用于管理 C++ 依赖时再启用；否则终端命令和项目文件足够。

## 不建议优先启用

- 数据库 MCP：当前项目没有数据库边界。
- Playwright MCP：主要面向 Web，不能直接验证 WPF 桌面 UI。
- Slack/Teams/Notion/Jira：除非团队协作流程已经在那里，否则先不接入。
- 过宽权限的 shell/command MCP：项目包含 native DLL、生成代码、build 目录，宽泛执行权限容易污染构建产物或误改文件。

## MCP JSON 约定

本项目示例文件位于：

```text
docs/agent/mcp-servers.example.json
```

该客户端的远程 MCP 条目使用 `serverURL`，不要写成 `url`；本地 MCP 条目使用 `command` 和 `args`。

启用前至少检查：

- `github.env.GITHUB_PERSONAL_ACCESS_TOKEN` 已替换为自己的 GitHub PAT，或改成客户端支持的环境变量引用方式。
- `filesystem` 和 `git` 路径仍指向当前仓库根目录。
- 本机已具备示例所需运行时：Node/npm、uvx、Docker。
- 不需要的 MCP 可以先删掉，优先保持最小可用配置。

## Skill 规划

当前使用一个主 skill：

```text
amlab-slicer-dev
```

覆盖范围：

- WPF + MVVM + HelixToolkit UI 开发
- OCCT native DLL 与 C# P/Invoke 边界
- `Protos/slicer.proto` gRPC 契约
- C# EngineHost 路由与进程管理
- C++ FDM engine、CMake、protobuf/gRPC 生成代码
- 后续 Clipper/CGAL 几何算法接入注意事项

后续当某类任务反复出现，再拆成更细的独立 skill：

- `slicer-geometry-algorithms`：轮廓拼接、偏置、布尔、填充、曲面切片、CGAL/Clipper 选型。
- `wpf-helix-viewport`：相机、选择、变换、Gizmo、模型树、参数面板、渲染性能。
- `grpc-engine-contract`：proto 演进、前后端兼容、流式消息、引擎注册、取消任务。
- `occt-step-interop`：STEP 读取、B-Rep 细分、native 内存所有权、x64 DLL 部署。

## 个性化规则

仓库根目录的 `AGENTS.md` 是当前项目的 Codex 个性化入口。Agent 进入本仓库后应优先遵守其中的语言、搜索、架构边界和验证规则。
