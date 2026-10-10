using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMLabSlicer.Models;
using AMLabSlicer.Services;
using AMLabSlicer.ViewModel;
using AMLabSlicer.Views;
using AMLabSlicer.Core.Parameters;
using AMLabSlicer.Grpc;
using AMLabSlicer.Plugins.Agent.Models;
using AMLabSlicer.Plugins.Agent.Services;
using AMLabSlicer.Plugins.Agent.ViewModel;
using AMLabSlicer.Plugins.Agent.Views;

internal static class PreferencesAndAgentChecks
{
    private static bool HasRoundedNativeRegion(Window window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            return GetWindowRect(hwnd, out var rect) && GetWindowRgn(hwnd, region) == 3
                && !PtInRegion(region, 0, 0) && PtInRegion(region, 20, 20)
                && !PtInRegion(region, rect.Right - rect.Left, 20)
                && !PtInRegion(region, 20, rect.Bottom - rect.Top);
        }
        finally { DeleteObject(region); }
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr region);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    private static bool LightCornersHaveOpaqueBacking(Window window)
    {
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        foreach (var (x, y) in new[] { (1, 1), (width - 2, 1), (1, height - 2), (width - 2, height - 2) })
        {
            var index = (y * width + x) * 4;
            if (pixels[index + 3] != 255 || pixels[index] < 140 || pixels[index + 1] < 140 || pixels[index + 2] < 140) return false;
        }
        return true;
    }
    public static async Task RunAsync(Action<bool, string> check, IUserDialogService dialogs)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AMLabSlicer-feature-check-" + Guid.NewGuid().ToString("N"));
        var store = new JsonPreferencesStore(Path.Combine(directory, "preferences.json"));
        var prefs = new PreferencesViewModel(store);
        prefs.Theme = "浅色"; prefs.FontSize = 18; prefs.FontFamilyName = "Segoe UI";
        prefs.AgentEndpoint = "http://localhost:11434/v1/"; prefs.AgentModel = "test-model";
        prefs.DeveloperPanelWidth = 420; prefs.EnablePanelAnimations = false;
        prefs.ShowEngineHostConsole = true;
        prefs.Language = "en";
        var loaded = new PreferencesViewModel(new JsonPreferencesStore(store.Location));
        check(loaded.ShowEngineHostConsole, "Host console preference persists");
        check(loaded.Language == "en", "English language preference persists across instances");
        check(loaded.Theme == "浅色" && loaded.FontSize == 18 && loaded.FontFamilyName == "Segoe UI" && loaded.AgentModel == "test-model" && loaded.DeveloperPanelWidth == 420,
            "preferences persist theme, font and Agent configuration across new instances");
        prefs.FontSize = 99; prefs.UndoStackDepth = -5; prefs.DeveloperPanelWidth = 999;
        check(prefs.FontSize == 20 && prefs.UndoStackDepth == 0 && prefs.DeveloperPanelWidth == 600, "preference ranges are enforced");
        File.WriteAllText(store.Location, "invalid json");
        var corrupt = new JsonPreferencesStore(store.Location);
        check(corrupt.Load().FontSize == 12 && corrupt.LoadWarning.Length > 0 && File.ReadAllText(store.Location) == "invalid json",
            "corrupt preferences fall back without silently overwriting source");
        corrupt.Save(new UserPreferences());
        check(File.ReadAllText(store.Location + ".bak") == "invalid json", "replacing settings preserves previous file backup");
        var portableProbe = new JsonPreferencesStore();
        check(portableProbe.Location.EndsWith("preferences.json") && AppStoragePaths.ExtensionsDirectory.EndsWith("extensions"), "storage and extension paths are available without installing plugins");

        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var chat = new OpenAiCompatibleChatService(client);
        var settings = new AgentSettings { AgentModel = "test-model", AgentSystemPrompt = "test system" };
        var reply = await chat.SendAsync(settings, [new("user", "hello")], CancellationToken.None);
        using var request = JsonDocument.Parse(handler.Body);
        check(reply == "test reply" && handler.LastUri!.AbsolutePath == "/v1/chat/completions" && request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "hello",
            "chat transport sends model and messages to compatible endpoint");
        check((await chat.ListModelsAsync(settings, CancellationToken.None)).Single() == "test-model", "model listing parses compatible response");
        check(OpenAiCompatibleChatService.ResolveEndpoint("http://localhost:11434/v1/chat/completions", "models").AbsolutePath == "/v1/models",
            "full completion URL normalizes to model-list endpoint");
        var agentSettingsStore = new PluginSettingsStore(Path.Combine(directory, "agent-settings.json"));
        agentSettingsStore.Save(new AgentSettings { AgentModel = loaded.AgentModel });
        var agentPrefs = new AgentSettingsViewModel(agentSettingsStore);
        var agent = new AgentChatViewModel(agentPrefs, chat, () => dialogs.OpenExtensions("amlab.agent"));
        agent.Input = "first"; await agent.SendCommand.ExecuteAsync(null);
        agent.Input = "second"; await agent.SendCommand.ExecuteAsync(null);
        using var conversation = JsonDocument.Parse(handler.Body);
        check(conversation.RootElement.GetProperty("messages").GetArrayLength() == 4 && agent.Messages.Count(m => m.Role == "assistant") == 2,
            "Agent continues successful conversation context");
        agentPrefs.AgentModel = "another-model"; agent.Input = "new model"; await agent.SendCommand.ExecuteAsync(null);
        using var switched = JsonDocument.Parse(handler.Body);
        check(switched.RootElement.GetProperty("messages").GetArrayLength() == 2, "switching model starts a fresh transmitted context");
        handler.Status = HttpStatusCode.Unauthorized; agent.Input = "retry me"; await agent.SendCommand.ExecuteAsync(null);
        check(agent.Input == "retry me" && !agent.IsSending && agent.Messages.Last().DeliveryStatus == "未完成", "authentication failure restores input and releases busy state");
        handler.Status = HttpStatusCode.OK; handler.Delay = true;
        agent.Input = "cancel me"; var pending = agent.SendCommand.ExecuteAsync(null);
        await Task.Delay(15); agent.SendCommand.Cancel(); await pending;
        check(!agent.IsSending && agent.Status == "已取消" && agent.Input == "cancel me", "cancellation retains draft and releases busy state");
        handler.Delay = false;
        agent.NewConversationCommand.Execute(null);
        check(agent.Messages.Count == 0, "new conversation clears local history");
        await agent.LoadModelsCommand.ExecuteAsync(null);
        check(agent.Models.Single() == "test-model" && agentPrefs.AgentModel == "test-model", "model discovery selects an available model");
        var registry = new EmptyAgentToolRegistry();
        check(registry.Tools.Count == 0 && !registry.TryGetTool("missing", out _), "extension registry is empty and invokes no plugins");
        var inventoryPath = Path.Combine(directory, "extensions");
        var validPackagePath = Path.Combine(inventoryPath, "test", "1.0");
        Directory.CreateDirectory(validPackagePath);
        File.WriteAllText(Path.Combine(validPackagePath, "extension.json"), "{\"name\":\"测试拓展\",\"version\":\"1.0\"}");
        var invalidPackagePath = Path.Combine(inventoryPath, "invalid", "1.0");
        Directory.CreateDirectory(invalidPackagePath); File.WriteAllText(Path.Combine(invalidPackagePath, "extension.json"), "invalid");
        var inventory = new ExtensionInventory(inventoryPath);
        var packages = inventory.ReadPackages();
        check(packages.Count == 2 && packages.Any(p => p.Name == "测试拓展" && p.State == "未加载") && packages.Any(p => p.State == "清单无法读取"),
            "extension manager lists package metadata and invalid manifests without loading plugins");
        agent.OpenSettingsCommand.Execute(null);
        check(dialogs is FakeDialogs { ExtensionsSection: "amlab.agent" }, "Agent settings open the extensions window");

        var app = Application.Current ?? new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AMLabSlicer.UI;component/Themes/DarkTheme.xaml") });
        var mainWindow = new MainWindow();
        var toolbarRow = ((Grid)mainWindow.Content).RowDefinitions[1];
        check(toolbarRow.Height.IsAbsolute && toolbarRow.Height.Value == 36, "main window XAML loads with typed default toolbar height");
        mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        mainWindow.Left = -10000; mainWindow.Top = -10000;
        mainWindow.ShowActivated = false; mainWindow.ShowInTaskbar = false;
        mainWindow.Show();
        var appearance = new AppearanceService();
        var uiPrefs = new PreferencesViewModel(null, appearance);
        var uiAgent = new AgentChatViewModel(agentPrefs, chat, () => dialogs.OpenExtensions("amlab.agent"));
        uiAgent.Messages.Add(new("user", "如何调整填充密度？"));
        uiAgent.Messages.Add(new("assistant", "这是布局测试消息，未调用真实模型。"));
        uiAgent.Input = string.Join("\n", Enumerable.Repeat("输入内容与上方消息保持相同的左右留白。较长文字会自动换行。", 12));
        var preferences = new PreferencesWindow(uiPrefs);
        check(preferences.WindowStyle == WindowStyle.None && preferences is ThemedWindow, "preferences use shared themed window chrome");
        var preferencesContent = (FrameworkElement)preferences.Content;
        preferences.Content = null;
        preferencesContent.DataContext = uiPrefs;
        var preferencesHost = new Border { Child = preferencesContent };
        preferencesHost.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        preferencesHost.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "PrimaryTextBrush");
        preferencesHost.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "UiFontFamily");
        preferencesHost.SetResourceReference(System.Windows.Documents.TextElement.FontSizeProperty, "UiFontSize");
        preferences.Content = preferencesHost;
        preferences.WindowStartupLocation = WindowStartupLocation.Manual;
        preferences.Left = -10000; preferences.Top = -10000;
        preferences.ShowActivated = false; preferences.ShowInTaskbar = false;
        preferences.Show();
        foreach (var theme in new[] { "深色", "浅色" })
        {
            uiPrefs.Theme = theme;
            foreach (var section in uiPrefs.Sections)
            {
                uiPrefs.SelectedSection = section;
                Layout(preferencesHost, 748, 550);
                check(preferencesContent.ActualWidth == 716, $"preferences section renders: {theme}/{section}");
            }
        }
        uiPrefs.SelectedSection = "外观与排版";
        uiPrefs.Language = "en";
        preferences.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Layout(preferencesHost, 748, 550);
        var languageSelector = (ComboBox)preferences.FindName("LanguageSelector");
        check(preferences.Title == "Preferences" && (string?)languageSelector.SelectedValue == "en", "language selector switches the open preferences window to English");
        check(VisualDescendants(languageSelector).OfType<TextBlock>().Any(t => t.Text == "English"), "language selector displays the readable language name");
        check(AMLabSlicer.Plugin.Wpf.Localization.UiText.Current.Translate("选择：层高") == "Selected: 层高", "localization preserves user model names inside selection messages");
        check(VisualDescendants(mainWindow).OfType<MenuItem>().Any(m => Equals(m.Header, "File")), "main menu updates to English without recreation");
        foreach (var section in uiPrefs.Sections)
        {
            uiPrefs.SelectedSection = section; Layout(preferencesHost, 748, 550);
            check(VisualDescendants(preferencesHost).OfType<TextBlock>().Any(t => t.Text == AMLabSlicer.Plugin.Wpf.Localization.UiText.Current.Translate(section)), "English section navigation preserves stable selection: " + section);
        }
        uiPrefs.SelectedSection = "外观与排版";
        SaveWindow(preferences, Path.Combine(directory, "preferences-english.png"));
        var englishWarning = new MessageDialogWindow("参数“填充密度”不能大于 100，已调整为 100。", "参数超出范围", true)
        { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        englishWarning.Show();
        englishWarning.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        check(englishWarning.Title == "Parameter Out of Range" && VisualDescendants(englishWarning).OfType<TextBlock>().Any(t => t.Text.Contains("Infill Density") && t.Text.Contains("100")), "parameter warning localizes its template and label without changing numeric values");
        SaveWindow(englishWarning, Path.Combine(directory, "warning-english.png"));
        uiPrefs.Language = "zh-CN";
        preferences.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        check(preferences.Title == "首选项" && englishWarning.Title == "参数超出范围", "switching back restores existing windows to Chinese");
        englishWarning.Close();
        Save(preferencesHost, Path.Combine(directory, "preferences-light.png"), 748, 550);
        check(((SolidColorBrush)preferencesHost.Background).Color == Color.FromRgb(243,244,246), "light theme updates existing rendered controls");
        uiPrefs.Theme = "深色";
        Save(preferencesHost, Path.Combine(directory, "preferences-dark.png"), 748, 550);
        uiPrefs.FontSize = 20;
        Layout(preferencesHost, 608, 420);
        check(((double)app.Resources["UiInputHeight"]) == 34 && ((double)app.Resources["UiFontSize"]) == 20, "font changes update live sizing resources");
        check(toolbarRow.Height.IsAbsolute && toolbarRow.Height.Value == 44, "main window toolbar GridLength updates when font grows");
        uiPrefs.FontSize = 12;
        Layout(preferencesHost, 748, 550);
        check(toolbarRow.Height.Value == 36, "main window toolbar height restores with default font");
        mainWindow.Close();
        preferences.Close();
        var preferencesPreview = new PreferencesWindow(uiPrefs)
        { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        preferencesPreview.Show();
        foreach (var theme in new[] { "浅色", "深色" })
        {
            uiPrefs.Theme = theme;
            preferencesPreview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            check(HasRoundedNativeRegion(preferencesPreview), $"preferences native corners are clipped: {theme}");
            if (theme == "浅色") check(LightCornersHaveOpaqueBacking(preferencesPreview), "light preferences anti-aliased corners have opaque theme backing");
            SaveWindow(preferencesPreview, Path.Combine(directory, theme == "浅色" ? "preferences-chrome-light.png" : "preferences-chrome-dark.png"));
        }
        preferencesPreview.Width = 700; preferencesPreview.Height = 510;
        preferencesPreview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        check(HasRoundedNativeRegion(preferencesPreview), "native rounded region follows preferences resize");
        preferencesPreview.Close();
        var pluginDirectory = Path.Combine(directory, "runtime-plugins");
        var pluginSource = Path.Combine(AppContext.BaseDirectory, "extensions", "amlab.agent", "1.0.0");
        var pluginTarget = Path.Combine(pluginDirectory, "amlab.agent", "1.0.0");
        Directory.CreateDirectory(pluginTarget);
        foreach (var file in Directory.GetFiles(pluginSource)) File.Copy(file, Path.Combine(pluginTarget, Path.GetFileName(file)));
        var manager = new PluginManager(uiPrefs, dialogs, pluginDirectory, Path.Combine(directory, "plugin-data"));
        uiPrefs.AgentModel = "legacy-agent-model";
        await manager.LoadEnabledAsync();
        check(manager.Panels.Count == 1 && manager.Packages.Single().State == "已启用", "Agent is loaded from a separate plugin package");
        var loadedPanel = (UserControl)manager.Panels.Single().Content!;
        uiPrefs.Language = "en";
        Layout(loadedPanel, 450, 640);
        check(VisualDescendants(loadedPanel).OfType<Button>().Any(b => Equals(b.Content, "New Chat")), "separate Agent plugin shares the host language");
        uiPrefs.Language = "zh-CN";
        check(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loadedPanel.GetType().Assembly) is PluginLoadContext,
            "Agent panel comes from the plugin AssemblyLoadContext");
        check(loadedPanel.GetType().Assembly.GetReferencedAssemblies().All(a => a.Name != "AMLabSlicer.UI"), "Agent plugin has no reference to the host UI assembly");
        var settingsPage = manager.CreateSettingsPage("amlab.agent")!;
        var settingsVm = settingsPage.DataContext;
        var modelProperty = settingsVm.GetType().GetProperty("AgentModel")!;
        check((string?)modelProperty.GetValue(settingsVm) == "legacy-agent-model", "Agent migrates previous settings before taking ownership");
        modelProperty.SetValue(settingsVm, "plugin-owned-model");
        using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "plugin-data", "amlab.agent", "settings.json"))))
            check(saved.RootElement.GetProperty("AgentModel").GetString() == "plugin-owned-model" && uiPrefs.AgentModel == "legacy-agent-model", "Agent stores changes independently of general preferences");
        check(!VisualDescendants(loadedPanel).OfType<ComboBox>().Any(), "chat panel does not edit plugin settings");
        var extensionsVm = new ExtensionsViewModel(uiPrefs, manager, dialogs);
        extensionsVm.RefreshCommand.Execute(null);
        var extensionsWindow = new ExtensionsWindow(extensionsVm)
        { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        extensionsWindow.Show();
        foreach (var theme in new[] { "深色", "浅色" })
        foreach (var section in extensionsVm.Sections.ToArray())
        {
            uiPrefs.Theme = theme; extensionsVm.SelectedSection = section;
            extensionsWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            extensionsWindow.UpdateLayout();
            check(extensionsWindow.IsVisible && ((FrameworkElement)extensionsWindow.Content).ActualWidth > 0, $"extensions window renders: {theme}/{section}");
            if (theme == "深色") SaveWindow(extensionsWindow, Path.Combine(directory, section == "Agent" ? "extensions-agent.png" : "extensions-manager.png"));
        }
        extensionsWindow.Close(); uiPrefs.Theme = "深色";
        uiPrefs.AgentModel = "keep-model"; uiPrefs.ResetDefaultsCommand.Execute(null);
        check(uiPrefs.AgentModel == "keep-model" && uiPrefs.Sections.Length == 4 && !uiPrefs.ShowEngineHostConsole, "general preference reset preserves migrated Agent settings and restores hidden Host");
        check(((SolidColorBrush)app.Resources["SurfaceBrush"]).Color == ((SolidColorBrush)app.Resources["WindowBackgroundBrush"]).Color, "dark side panels share the workspace background");
        foreach (var theme in new[] { "浅色", "深色" })
        {
            uiPrefs.Theme = theme;
            var message = new MessageDialogWindow("参数“层高”不能大于 0.6，已调整为 0.6。", "参数超出范围", true)
            { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
            message.Show();
            message.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            check(HasRoundedNativeRegion(message), $"warning native corners are clipped: {theme}");
            if (theme == "浅色") check(LightCornersHaveOpaqueBacking(message), "light warning anti-aliased corners have opaque theme backing");
            check(message.WindowStyle == WindowStyle.None && ((SolidColorBrush)message.Background).Color == (theme == "浅色" ? Color.FromRgb(243,244,246) : Color.FromRgb(30,30,30)),
                $"warning dialog uses shared chrome and palette: {theme}");
            SaveWindow(message, Path.Combine(directory, theme == "浅色" ? "warning-light.png" : "warning-dark.png"));
            System.Windows.SystemCommands.CloseWindowCommand.Execute(null, message);
            check(!message.IsVisible, "themed dialog title-bar close command closes the window");
        }
        var pane = loadedPanel;
        pane.DataContext.GetType().GetProperty("Input")!.SetValue(pane.DataContext, uiAgent.Input);
        Layout(pane, 360, 640);
        var chatInput = (TextBox)pane.FindName("ChatInput");
        var inputScroll = (ScrollViewer)chatInput.Template.FindName("PART_ContentHost", chatInput);
        var verticalBar = (System.Windows.Controls.Primitives.ScrollBar)inputScroll.Template.FindName("PART_VerticalScrollBar", inputScroll);
        ExerciseScrollBar(verticalBar, 0, 20);
        Layout(pane, 360, 640);
        check(chatInput.VerticalOffset > 0, "custom input scrollbar thumb drag scrolls text");
        var beforePage = chatInput.VerticalOffset;
        System.Windows.Controls.Primitives.ScrollBar.PageDownCommand.Execute(null, verticalBar);
        Layout(pane, 360, 640);
        check(chatInput.VerticalOffset > beforePage, "custom scrollbar track paging remains available");
        chatInput.ScrollToHome(); Layout(pane, 360, 640);
        var horizontalScroll = new ScrollViewer { Content = new Border { Width = 1000, Height = 60 }, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Layout(horizontalScroll, 360, 120);
        var horizontalBar = (System.Windows.Controls.Primitives.ScrollBar)horizontalScroll.Template.FindName("PART_HorizontalScrollBar", horizontalScroll);
        ExerciseScrollBar(horizontalBar, 20, 0);
        Layout(horizontalScroll, 360, 120);
        check(horizontalScroll.HorizontalOffset > 0, "custom horizontal scrollbar thumb drag scrolls content");
        Save(pane, Path.Combine(directory, "agent-panel.png"), 360, 640);
        uiPrefs.OpenDeveloperPanelOnStartup = true; uiPrefs.EnablePanelAnimations = false;
        var workspace = new PrepareWorkspaceViewModel(new ParameterStore(), uiPrefs, new NoSlicing(), new SliceRequestFactory(), dialogs, manager);
        var view = new PrepareWorkspaceView { DataContext = workspace };
        Layout(view, 960, 640);
        var outlinerRow = (RowDefinition)view.FindName("OutlinerRow");
        var parameterHost = (Border)view.FindName("ParameterHost");
        var outlinerSplitter = (GridSplitter)view.FindName("OutlinerSplitter");
        outlinerSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        outlinerSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 10000) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        outlinerSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 10000, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
        Layout(view, 960, 640);
        check(parameterHost.ActualHeight >= 149.9 && outlinerRow.ActualHeight + parameterHost.ActualHeight + 4 <= 633,
            $"dragging the horizontal splitter cannot hide the 150 DIP parameter area (top={outlinerRow.ActualHeight}, bottom={parameterHost.ActualHeight}, view={view.ActualHeight}, max={outlinerRow.MaxHeight})");
        Layout(view, 1280, 1200); outlinerRow.Height = new GridLength(1000); Layout(view, 1280, 1200);
        foreach (var height in new[] { 640, 480, 550, 480 })
        {
            Layout(view, 960, height);
            check(parameterHost.ActualHeight >= 149.9 && outlinerRow.ActualHeight + parameterHost.ActualHeight + 4 <= height - 8 + 1,
                "resizing after a tall outliner preserves the parameter area: " + height);
        }
        outlinerRow.Height = new GridLength(220); Layout(view, 960, 640);
        var modelPreview = (ModelPreviewView)view.FindName("ModelPreview");
        check(modelPreview.FindName("BtnMode") is null && modelPreview.FindName("FaceOverlayCanvas") is null
            && typeof(PrepareWorkspaceViewModel).GetProperty("ViewportMode") is null
            && typeof(OutlinerNodeViewModel).GetProperty("FaceIndices") is null,
            "selection mode entry, overlay, state and saved face groups have been removed");
        modelPreview.SelectedNode = new HelixToolkit.SharpDX.Model.Scene.GroupNode { Name = "Selection test object" };
        using (var inputSource = new HwndSource(new HwndSourceParameters("selection-removal-keyboard-check") { Width = 1, Height = 1, WindowStyle = 0 }))
        {
            foreach (var key in new[] { System.Windows.Input.Key.Tab, System.Windows.Input.Key.Q, System.Windows.Input.Key.W, System.Windows.Input.Key.E, System.Windows.Input.Key.T, System.Windows.Input.Key.L, System.Windows.Input.Key.D })
            {
                var keyEvent = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, inputSource, Environment.TickCount, key)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                modelPreview.RaiseEvent(keyEvent);
                check(!keyEvent.Handled && modelPreview.SelectedNode?.Name == "Selection test object", "removed selection shortcut leaves normal object selection intact: " + key);
            }
        }
        modelPreview.SelectedNode = null;
        check(modelPreview.FindName("BtnMove") is Button && modelPreview.FindName("BtnFace") is Button && modelPreview.FindName("BtnSplit") is Button,
            "object transform, bottom-face placement and model split tools remain available");
        var right = (System.Windows.Controls.Border)view.FindName("AgentPanelHost");
        var rail = (Border)view.FindName("ExtensionRailHost");
        check(rail.Parent is Grid { Parent: Border parent } && parent == right,
            "extension rail belongs to the collapsible right panel");
        check(right.Visibility == Visibility.Visible && right.ActualWidth >= 300, "right Agent panel opens with startup preference at minimum window width");
        var openViewportWidth = ((Border)view.FindName("ViewportHost")).ActualWidth;
        workspace.ToggleAgentPanelCommand.Execute(null); Layout(view, 960, 640);
        check(right.Visibility == Visibility.Collapsed, "right panel collapses independently");
        check(((Border)view.FindName("ViewportHost")).ActualWidth >= openViewportWidth + 303.9,
            "collapsing the right panel returns its complete width and splitter to the viewport");
        workspace.ToggleAgentPanelCommand.Execute(null); Layout(view, 1280, 640);
        check(right.Visibility == Visibility.Visible && right.ActualWidth >= 300, "right panel restores its width");
        var otherPanel = new WorkspacePanelEntry("fixture", "测试面板", "T", new object());
        workspace.ExtensionPanels.Add(otherPanel); workspace.SelectExtensionPanelCommand.Execute(otherPanel);
        check(workspace.SelectedExtensionPanel == otherPanel && otherPanel.IsSelected && !workspace.ExtensionPanels[0].IsSelected, "extension rail switches the selected panel content");
        workspace.SelectExtensionPanelCommand.Execute(workspace.ExtensionPanels[0]); workspace.ExtensionPanels.Remove(otherPanel);
        workspace.OpenExtensionManagementCommand.Execute(null);
        check(dialogs is FakeDialogs { ExtensionsSection: "插件管理" }, "extension rail plus opens extension management");
        workspace.TogglePanelCommand.Execute(null); Layout(view, 1280, 640);
        check(((System.Windows.Controls.Border)view.FindName("LeftPanelHost")).Visibility == Visibility.Collapsed && right.Visibility == Visibility.Visible,
            "left and right panels have independent controls");
        workspace.TogglePanelCommand.Execute(null); Layout(view, 1280, 640);
        Save(view, Path.Combine(directory, "workspace-panels.png"), 1280, 640);
        var leftColumn = (ColumnDefinition)view.FindName("LeftPanelColumn");
        var rightColumn = (ColumnDefinition)view.FindName("AgentPanelColumn");
        var leftHost = (Border)view.FindName("LeftPanelHost");
        var viewportHost = (Border)view.FindName("ViewportHost");
        check(viewportHost.CornerRadius == new CornerRadius(5)
            && ((Border)view.FindName("OutlinerHost")).CornerRadius == new CornerRadius(0)
            && ((Border)view.FindName("ParameterHost")).CornerRadius == new CornerRadius(0)
            && ((Border)view.FindName("AgentContentHost")).CornerRadius == new CornerRadius(0),
            "workspace uses rounded boundaries only around the model viewport");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Layout(view, 1800, 640);
            leftColumn.Width = new GridLength(620); rightColumn.Width = new GridLength(590);
            Layout(view, 1800, 640);
            foreach (var width in new[] { 1280, 960, 1100, 960 })
            {
                Layout(view, width, 640); Layout(view, width, 640);
                check(leftHost.ActualWidth >= 280 && right.ActualWidth >= 300 && viewportHost.ActualWidth >= 299.9 &&
                    leftHost.ActualWidth + right.ActualWidth + viewportHost.ActualWidth + 16 <= width + .1 &&
                    Math.Abs(leftColumn.Width.Value - leftHost.ActualWidth) < 1 && Math.Abs(rightColumn.Width.Value - right.ActualWidth) < 1,
                    $"resized panels fit together after widening: cycle {cycle}, width {width}");
            }
        }
        workspace.ToggleAgentPanelCommand.Execute(null); Layout(view, 960, 640);
        check(right.ActualWidth == 0 && rightColumn.MinWidth == 0 && viewportHost.ActualWidth >= 300, "collapsed panel stays at zero during resize");
        workspace.ToggleAgentPanelCommand.Execute(null); Layout(view, 960, 640);
        var preview = (ModelPreviewView)view.FindName("ModelPreview");
        var cube = (Canvas)preview.FindName("ViewCubeCanvas");
        uiPrefs.Theme = "浅色";
        check(cube.Children.OfType<System.Windows.Shapes.Polygon>().Take(6).All(p => ((SolidColorBrush)p.Fill).Color.A == 0),
            "view cube faces remain transparent");
        check(((SolidColorBrush)app.Resources["ViewCubeTextBrush"]).Color == Color.FromRgb(80,86,96), "light theme cube labels use grey text");
        var cubeViewport = (HelixToolkit.Wpf.SharpDX.Viewport3DX)preview.FindName("MainViewport");
        cubeViewport.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        { LookDirection = new System.Windows.Media.Media3D.Vector3D(-1,-1,-1), UpDirection = new System.Windows.Media.Media3D.Vector3D(0,0,1) };
        typeof(ModelPreviewView).GetMethod("UpdateViewCubeCanvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(preview, [null, EventArgs.Empty]);
        var cubeHost = new Border { Background = new SolidColorBrush(Color.FromRgb(228,231,236)) };
        ((Panel)cube.Parent).Children.Remove(cube); cubeHost.Child = cube;
        Save(cubeHost, Path.Combine(directory, "view-cube-light.png"), 120, 120);
        // Offscreen renders test WPF structure and bindings, not GPU rendering or real OS DPI changes.
        var package = manager.Packages.Single();
        await manager.DisableAsync(package);
        check(manager.Panels.Count == 0 && workspace.SelectedExtensionPanel is null, "disabling Agent removes its right-side panel and selection");
        await CheckFreshProcess(pluginDirectory, Path.Combine(directory, "plugin-data"), 0);
        check(true, "disabled plugin stays disabled in a fresh process");
        await manager.EnableAsync(package);
        check(package.State == "待重启启用" && manager.Panels.Count == 0, "stopped WPF plugins require restart before reactivation");
        var restarted = new PluginManager(uiPrefs, dialogs, pluginDirectory, Path.Combine(directory, "plugin-data"));
        restarted.Discover();
        check(restarted.Packages.Single().State == "待加载", "enabled state is persisted for the next process start");
        await CheckFreshProcess(pluginDirectory, Path.Combine(directory, "plugin-data"), 1);
        check(true, "Agent UI and plugin settings restore in a fresh process");
        var zipPath = Path.Combine(directory, "fixture.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var file in Directory.GetFiles(pluginTarget))
            {
                var entry = zip.CreateEntry(Path.GetFileName(file));
                using var output = entry.Open();
                if (Path.GetFileName(file) == "extension.json")
                {
                    using var writer = new StreamWriter(output);
                    writer.Write(File.ReadAllText(file).Replace("amlab.agent", "amlab.fixture"));
                }
                else { using var input = File.OpenRead(file); input.CopyTo(output); }
            }
        }
        await manager.InstallAsync(zipPath);
        check(manager.Packages.Any(p => p.Id == "amlab.fixture" && p.State == "已安装，未启用") && manager.Panels.Count == 0,
            "ZIP install adds a package without executing it");
        var badZip = Path.Combine(directory, "traversal.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(badZip, System.IO.Compression.ZipArchiveMode.Create))
        { using var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open()); writer.Write("must not escape"); }
        var rejected = false;
        try { await manager.InstallAsync(badZip); } catch (InvalidDataException) { rejected = true; }
        check(rejected && !File.Exists(Path.Combine(pluginDirectory, "escape.txt")) && !Directory.GetDirectories(pluginDirectory, ".install-*").Any(),
            "installer rejects traversal and cleans its staging directory");
        var incompatibleZip = Path.Combine(directory, "incompatible.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(incompatibleZip, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("extension.json").Open());
            writer.Write(File.ReadAllText(Path.Combine(pluginTarget, "extension.json")).Replace("\"apiVersion\": 1", "\"apiVersion\": 99"));
        }
        rejected = false;
        try { await manager.InstallAsync(incompatibleZip); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "installer rejects incompatible plugin API before activation");
        await manager.RemoveAsync(manager.Packages.Single(p => p.Id == "amlab.fixture"));
        check(!Directory.Exists(Path.Combine(pluginDirectory, "amlab.fixture", "1.0.0")), "unloaded plugin package can be uninstalled immediately");
        await manager.RemoveAsync(package);
        check(package.State == "待重启卸载" && Directory.Exists(pluginTarget), "loaded plugin uninstall is deferred until restart");
        // Parent process still holds the first package DLL open. Use a separate package
        // for a real two-process shutdown/restart uninstall, without a surviving loader.
        var removalRoot = Path.Combine(directory, "removal-plugins");
        var removalPackage = Path.Combine(removalRoot, "amlab.agent", "1.0.0");
        var removalData = Path.Combine(directory, "removal-data");
        Directory.CreateDirectory(removalPackage); Directory.CreateDirectory(Path.Combine(removalData, "amlab.agent"));
        foreach (var file in Directory.GetFiles(pluginSource)) File.Copy(file, Path.Combine(removalPackage, Path.GetFileName(file)));
        File.Copy(Path.Combine(directory, "plugin-data", "amlab.agent", "settings.json"), Path.Combine(removalData, "amlab.agent", "settings.json"));
        await CheckFreshProcess(removalRoot, removalData, 1, remove: true);
        await CheckFreshProcess(removalRoot, removalData, 0);
        check(!Directory.Exists(removalPackage) && File.Exists(Path.Combine(removalData, "amlab.agent", "settings.json")), "fresh process removes the package and preserves plugin settings");
        Console.WriteLine("UI review output: " + directory);
    }
    private static async Task CheckFreshProcess(string directory, string data, int count, bool remove = false)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(PreferencesAndAgentChecks).Assembly.Location, "--plugin-probe", directory, data, count.ToString() }) start.ArgumentList.Add(arg);
        if (remove) start.ArgumentList.Add("--remove");
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException(await output + await error);
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }
    private static void Layout(FrameworkElement view, int width, int height)
    {
        view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        view.Width = width; view.Height = height; view.Measure(new Size(width,height)); view.Arrange(new Rect(0,0,width,height)); view.UpdateLayout();
    }
    private static void Save(FrameworkElement view, string path, int width, int height)
    {
        Layout(view, width, height);
        var image = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void SaveWindow(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void ExerciseScrollBar(System.Windows.Controls.Primitives.ScrollBar bar, double x, double y)
    {
        bar.ApplyTemplate();
        var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
        var thumb = track.Thumb;
        thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(x,y) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(x,y,false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
    }
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string Body = ""; public Uri? LastUri;
        public HttpStatusCode Status = HttpStatusCode.OK; public bool Delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Delay) await Task.Delay(Timeout.Infinite, cancellationToken);
            LastUri = request.RequestUri;
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(request.Method == HttpMethod.Get ? "{\"data\":[{\"id\":\"test-model\"}]}" : "{\"choices\":[{\"message\":{\"content\":\"test reply\"}}]}") };
        }
    }
    private sealed class NoSlicing : ISlicingService
    {
        public Task<AlgorithmList> GetAlgorithmsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ParameterTemplateList> GetParametersAsync(string algorithm, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<SliceServerMessage> SliceAsync(SliceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
