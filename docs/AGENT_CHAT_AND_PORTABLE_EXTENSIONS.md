# Agent 插件与便携安装

Agent 已迁移至独立 AMLabSlicer.Plugins.Agent 项目，主程序通过清单和 AssemblyLoadContext 加载。插件开发与交互位置规范见 [PLUGIN_DEVELOPMENT_STANDARD.md](PLUGIN_DEVELOPMENT_STANDARD.md)。

## 使用

- 右上图标整体展开/收起右侧插件区；选择 A 打开 Agent。输入、发送、停止、新对话保留在面板中。
- 顶部“插件 → Agent 设置”或 Agent 的“设置”打开插件设置页。服务地址、模型、模型列表读取、密钥环境变量、超时及提示词只在此处修改。
- “插件 → 插件管理”及右侧“＋”管理插件包，并设置整个右侧面板的动画、启动展开和宽度。
- “安装 ZIP”只安装；点击启用后加载。停用移除面板，已加载插件的重新启用和更新需重启；已加载包的卸载下一次启动完成。

## 存储

插件包位于程序旁 extensions/<id>/<version>。配置位于 DataDirectory/plugins/<id>/settings.json，注册表为 plugins/registry.json；普通模式使用 LocalAppData/AMLabSlicer，portable.mode 使用程序旁 Data。

Agent 首次加载迁移旧首选项字段，之后独立保存；首选项重置不会改变插件设置。不保存明文 API Key。首个插件包由构建自动部署，也生成可安装 ZIP。

## 验证边界

实际验证 DLL 加载、插件面板和设置页、深浅主题、迁移及独立保存、折叠及缩放、安装/启用/停用、跨进程配置恢复和延迟卸载。对话请求仍使用模拟服务验证多轮、错误与取消；未调用真实模型、切片引擎或设备。

插件 SDK 当前公开配置、导航和日志；未接入模型工具调用。AssemblyLoadContext 不提供安全沙箱，只加载可信插件。
