# AMLabSlicer

一款基于 WPF 和 C# 开发的多轴 3D 打印切片与可视化上位机软件。

## 项目简介


## ✨ 当前功能
* **现代化 UI 布局**：采用响应式侧边栏设计，支持丝滑的动态折叠与展开，完美自适应窗口大小。
* **高性能 3D 渲染**：底层基于 DirectX 11（Helix Toolkit SharpDX），流畅渲染海量三角面片，告别卡顿。
* **极速模型导入**：集成 Assimp 解析库，目前支持极速载入和解析 `.stl` 格式的 3D 模型。
* **完全解耦架构**：严格遵循 MVVM 模式（CommunityToolkit.Mvvm），数据与界面分离，代码结构清晰易维护。

## 🛠️ 技术栈核心
* **UI 框架**：WPF (Windows Presentation Foundation)
* **架构模式**：MVVM (借助 CommunityToolkit.Mvvm)
* **3D 渲染引擎**：HelixToolkit.Wpf.SharpDX
* **模型解析库**：HelixToolkit.SharpDX.Assimp

## 💻 快速启动
1. 克隆本项目到本地环境：
  `bash`
   git clone [https://github.com/hiztoHZT/AMLabSlicer.git](https://github.com/hiztoHZT/AMLabSlicer.git)
2. 使用 Visual Studio 打开 AMLabSlicer.sln 解决方案文件。

3.在解决方案资源管理器中右键 -> 还原 NuGet 程序包（确保 Helix 等依赖项正确下载）。

4.按下 F5 或点击“启动”按钮编译并运行项目。

📅 近期开发计划 (TODO)
[ ] 绘制 3D 打印平台网格（Grid）与构建体积边界。

[ ] 接入视角的快速控制功能（正视、俯视、侧视等）。

[ ] 开发切片（Slicing）参数配置模块。

[ ] 探索并集成核心切片算法，实现 G-Code 生成与路径可视化。

本项目由 AMLab 持续开发与维护。


---

### 💡 如何维护它？
* **勾选框**：我在“近期开发计划”里放了 `-[ ]` 这样的语法。在 GitHub 网页上，这会显示为一个小方框。以后你完成了一项功能，就可以把它改成 `-[x]`，代表已打勾完成，非常有成就感。
* **持续更新**：每次你完成了重大功能（比如切片算法写好了，或者加了炫酷的特效），记得顺手更新一下这个文件。