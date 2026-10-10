# AMLabSlicer 插件开发标准

界面语言由宿主首选项统一控制。内置 Agent 使用共享 WPF SDK 的 `AMLabSlicer.Plugin.Wpf.Localization`：静态文案用 `{loc:Text Key='文案'}`，显示用绑定文案用 `{loc:LocalizedBinding Status}`。不得翻译用户输入、模型名称或请求内容，不得用已翻译的标题作为插件或业务标识。新增文案同步维护英文词条；未知外部文本保留原文。

版本：1.0；日期：2026-10-10。以下规则用于后续插件开发，Agent 是首个实现样例。

## 1. 工程边界

- `AMLabSlicer.Plugin.Abstractions`：生命周期、宿主上下文及配置存储契约，目标框架 net8.0。
- `AMLabSlicer.Plugin.Wpf`：面板与设置页契约，目标框架 net8.0-windows。
- `AMLabSlicer.Plugins.Agent`：独立插件 DLL，包含对话服务、ViewModel、面板、设置页。不得引用 AMLabSlicer.UI。
- 宿主 `PluginManager`：发现、校验、加载、启用、停用、安装和卸载。`PluginLoadContext`：私有依赖解析及共享 SDK。
- 普通插件实现 `IPlugin`；交互插件实现 `IPanelPlugin`；设置页通过 `IPluginSettingsPage` 提供。

## 2. 界面位置规则

1. 交互插件只能返回嵌入式 `UserControl`，由宿主放进主窗口右侧插件面板。
2. 不得自行创建 Window、调用 Show/ShowDialog、创建独立 Dispatcher 或修改主窗口布局。不得向左侧模型/参数区或中央预览区注册插件面板。
3. 所有插件配置只能在顶部“插件”窗口所属的设置页修改。右侧面板可以显示只读连接摘要，设置按钮只能导航；不得内嵌服务地址、模型选择、密钥、提示词等配置编辑。
4. 面板交互状态（草稿、发送、取消、新对话）不属于配置，可以在面板操作。
5. 插件提供设置页内容，不创建设置窗口。面板、设置页分别返回不同的控件实例，不共享控件父级。
6. WPF 控件创建和更新在宿主 UI 线程执行；耗时操作异步执行。界面遵守 UI_DESIGN_STANDARD.md，复用宿主主题资源及圆角 5。

这些规则由 SDK 类型和宿主注册路径支持，也是插件作者必须遵守的开发约定。进程内 DLL 能自行执行代码，接口不是针对恶意代码的沙箱。

## 3. 清单与包结构

```text
extensions/<id>/<version>/
  extension.json
  插件.dll
  插件.deps.json
  私有依赖.dll
  assets/（可选）
```

ZIP 根目录必须直接包含清单及入口文件，不能多套一层目录。Agent 的 extension.json 是参考：id、name、version、apiVersion、targetFramework、architecture、entryAssembly、entryType、icon。

- API 版本目前为整数 1，插件目标框架为 net8.0-windows；architecture 为 x64 或 any。含原生库的插件必须与 x64 宿主兼容。
- id 使用小写字母、数字、点和连字符，最长 80 字符，禁止 `..`；version 使用可由 System.Version 解析的数字版本，当前不支持预发布 SemVer 标签。
- 入口 DLL 必须位于版本目录根部，入口类型实现 IPlugin 并提供公开无参数构造函数。
- 同一 id 只启用一个版本，启动时选择最高数字版本。已加载过的 id 切换版本必须重启。
- 每个插件使用独立且唯一的程序集名称和命名空间。SDK 不随插件包重复分发；宿主与插件共享宿主加载的 SDK 程序集。
- 插件项目使用 EnableDynamicLoading=true。通过 Private=false/ExcludeAssets=runtime 排除共享 SDK 的运行时复制，其他私有依赖随包分发。

## 4. 生命周期

`读取清单 → 兼容检查 → 创建 AssemblyLoadContext → 加载入口 → InitializeAsync → 注册右侧面板/设置页`。

- 构造和初始化不得自动向模型服务发送请求或控制设备。Agent 由用户发送或读取模型列表操作触发请求。
- StopAsync 取消任务、解除事件订阅、停止计时器并释放网络资源。宿主限制停止等待时间。
- 首版采用非 collectible 上下文，不承诺 WPF 热卸载。停用移除面板；已停用的已加载插件再次启用显示“待重启启用”。
- 卸载未加载的包可立即删除；已加载的包记录待删除，下一次进程启动、加载 DLL 前删除。默认保留插件配置。
- 单个插件初始化失败显示到管理列表，不将其加入面板。SDK API 或平台不兼容的包不执行。

## 5. 安装与更新

“插件 → 插件管理 → 安装 ZIP”先解压到临时目录并检查，再移入独立版本目录。安装不执行代码；用户单独启用。限制 2000 项、解压总大小 100 MB，拒绝越界路径和符号链接，失败清理临时目录。

更新安装更高版本的包，停用旧版后启用新版；如果本进程加载过这个 id，重启后生效。管理列表刷新保留已有运行实例，不重新加载 DLL。

## 6. 配置与能力边界

- 插件通过 `IPluginContext.Settings` 保存配置；每个插件存储在 `AppStoragePaths.DataDirectory/plugins/<id>/settings.json`。宿主注册表位于 plugins/registry.json。
- 普通模式 DataDirectory 为 LocalAppData/AMLabSlicer；程序旁存在 portable.mode 时改为程序旁 Data。
- Agent 首次加载时从旧 preferences.json 对应字段迁移；已有插件设置不覆盖。旧宿主字段只为兼容迁移保留，实际 Agent 使用自己的设置对象。
- 不保存 API Key 明文，只保存环境变量名称。首选项恢复默认不影响插件配置。
- 不向插件提供整个 DI 容器、MainWindow、PreferencesViewModel、可变模型集合或原生指针。
- 当前 SDK 只公开配置、设置导航和日志；尚未提供模型修改、切片执行、设备控制和 Agent 工具调用。未来按明确契约添加能力，并保留宿主校验和撤销流程。
- AssemblyLoadContext 隔离程序集依赖，不隔离权限或进程崩溃。本机制面向可信插件，复杂原生算法可另用工作进程。

## 7. 构建与验证

构建 AMLabSlicer.UI.csproj 会先构建 Agent，再复制包至输出目录 extensions/amlab.agent/1.0.0；Publish 同样携带包。解决方案只设置项目构建依赖，宿主没有 Agent 运行时引用。

Agent 工程自动生成 `bin/<Configuration>/amlab.agent-1.0.0.zip`，可作为便携包。

回归测试覆盖实际 AssemblyLoadContext 加载及共享契约、插件无宿主 UI 引用、配置迁移与独立保存、WPF 两套主题、右侧整体折叠、反复缩放、停用及新进程恢复、ZIP 安装与越界/不兼容拒绝、立即和跨进程延迟卸载；对话协议使用模拟服务，未调用真实模型或设备。

官方机制参考：[.NET 插件加载](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support)、[AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext)、[协作式卸载](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability)。
