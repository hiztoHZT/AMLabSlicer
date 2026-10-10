# 插件目录

插件包布局为 extensions/<id>/<version>/，版本目录根部包含 extension.json、入口 DLL、deps.json 和私有依赖。Agent 首次随构建携带，按独立插件加载。

通过顶部“插件 → 插件管理”安装 ZIP、启用、停用和卸载。安装不执行代码；插件启用后由 AssemblyLoadContext 加载。已加载包的更新、重新启用和卸载需重启。插件只面向可信代码，加载上下文不是安全沙箱。

交互插件的 UserControl 只能显示在主窗口右侧；所有配置编辑只放在“插件”窗口中。详细契约、清单、生命周期和验证规则见项目 docs/PLUGIN_DEVELOPMENT_STANDARD.md。

设置独立存于 DataDirectory/plugins/<id>/settings.json。程序旁有 portable.mode 时 DataDirectory 为程序旁 Data，否则为 LocalAppData/AMLabSlicer。API Key 只保存环境变量名称。
