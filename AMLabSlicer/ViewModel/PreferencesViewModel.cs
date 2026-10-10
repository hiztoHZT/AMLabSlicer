using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using AMLabSlicer.Models;
using AMLabSlicer.Services;

namespace AMLabSlicer.ViewModel;

public partial class PreferencesViewModel : ObservableObject
{
    public record LanguageChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
    public LanguageChoice[] Languages { get; } = [new("zh-CN", "中文"), new("en", "English")];
    [ObservableProperty] private string _language = "zh-CN";
    private readonly IPreferencesStore? _store;
    private readonly IAppearanceService? _appearance;
    private bool _loading;
    public string[] Themes { get; } = ["深色", "浅色"];
    public double[] FontSizes { get; } = [10, 11, 12, 13, 14, 16, 18, 20];
    public IReadOnlyList<string> FontFamilies { get; }
    public string[] Sections { get; } = ["外观与排版", "视图与渲染", "操作与历史", "运行与诊断"];
    public string SettingsLocation => _store?.Location ?? "内存设置";
    [ObservableProperty] private string _saveStatus = "设置自动保存并即时生效";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSection), nameof(IsViewportSection), nameof(IsHistorySection), nameof(IsRuntimeSection))]
    private string _selectedSection = "外观与排版";
    public bool IsAppearanceSection => SelectedSection == Sections[0];
    public bool IsViewportSection => SelectedSection == Sections[1];
    public bool IsHistorySection => SelectedSection == Sections[2];
    public bool IsRuntimeSection => SelectedSection == Sections[3];
    [ObservableProperty] private bool _showEngineHostConsole;
    [ObservableProperty] private string _theme = "深色";
    [ObservableProperty] private string _fontFamilyName = "Microsoft YaHei";
    [ObservableProperty] private double _fontSize = 12;
    [ObservableProperty] private bool _enableSSAO = false;
    [ObservableProperty] private bool _showCoordinateSystem = true;
    [ObservableProperty] private bool _showViewCube = true;
    [ObservableProperty] private bool _showBuildGrid = true;
    [ObservableProperty] private bool _showViewportStatus = true;
    [ObservableProperty] private bool _useOrthographic = false;
    [ObservableProperty] private bool _enableDeleteConfirm = true;
    [ObservableProperty] private bool _enableArrangeConfirm = true;
    [ObservableProperty] private bool _enableSplitConfirm = true;
    [ObservableProperty] private bool _splitUndoable = false;
    [ObservableProperty] private bool _enablePanelAnimations = true;
    [ObservableProperty] private bool _openDeveloperPanelOnStartup = false;
    [ObservableProperty] private string _agentEndpoint = "http://localhost:11434/v1/";
    [ObservableProperty] private string _agentModel = "";
    [ObservableProperty] private string _agentApiKeyVariable = "";
    [ObservableProperty] private string _agentSystemPrompt = "";
    private int _agentTimeoutSeconds = 120;
    public int AgentTimeoutSeconds { get => _agentTimeoutSeconds; set => SetProperty(ref _agentTimeoutSeconds, Math.Clamp(value, 10, 600)); }
    private bool _enableFXAA = true;
    public bool EnableFXAA { get => _enableFXAA; set { if (SetProperty(ref _enableFXAA, value)) OnPropertyChanged(nameof(FXAALevel)); } }
    public FXAALevel FXAALevel => EnableFXAA ? FXAALevel.High : FXAALevel.None;
    private int _undoStackDepth = 25;
    public int UndoStackDepth { get => _undoStackDepth; set => SetProperty(ref _undoStackDepth, Math.Clamp(value, 0, 200)); }
    private double _developerPanelWidth = 360;
    public double DeveloperPanelWidth { get => _developerPanelWidth; set => SetProperty(ref _developerPanelWidth, double.IsFinite(value) ? Math.Clamp(value, 300, 600) : 360); }

    public PreferencesViewModel(IPreferencesStore? store = null, IAppearanceService? appearance = null)
    {
        _store = store;
        _appearance = appearance;
        FontFamilies = appearance?.FontFamilies ?? ["Microsoft YaHei", "Segoe UI", "Consolas"];
        Apply(store?.Load() ?? new());
        if (!string.IsNullOrEmpty(store?.LoadWarning)) SaveStatus = store.LoadWarning;
        PropertyChanged += (_, e) =>
        {
            if (_loading || e.PropertyName is nameof(SaveStatus) or nameof(SelectedSection) or nameof(IsAppearanceSection) or nameof(IsViewportSection) or nameof(IsHistorySection) or nameof(IsRuntimeSection) or nameof(FXAALevel)) return;
            var settings = Snapshot();
            _appearance?.Apply(settings);
            try { _store?.Save(settings); SaveStatus = e.PropertyName == nameof(ShowEngineHostConsole) ? "设置已保存，Host 控制台显示将在下次启动时生效" : "设置已保存并即时生效"; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { SaveStatus = "设置保存失败：" + ex.Message; }
        };
    }
    partial void OnFontSizeChanged(double value) { if (!double.IsFinite(value) || value < 10 || value > 20) FontSize = double.IsFinite(value) ? Math.Clamp(value, 10, 20) : 12; }
    partial void OnLanguageChanged(string value) { if (value is not ("zh-CN" or "en")) Language = "zh-CN"; }
    public UserPreferences Snapshot() => new()
    {
        Theme = Theme,
        Language = Language,
        ShowEngineHostConsole = ShowEngineHostConsole,
        FontFamilyName = FontFamilyName,
        FontSize = FontSize,
        EnableSSAO = EnableSSAO,
        ShowCoordinateSystem = ShowCoordinateSystem,
        ShowViewCube = ShowViewCube,
        ShowBuildGrid = ShowBuildGrid,
        ShowViewportStatus = ShowViewportStatus,
        UseOrthographic = UseOrthographic,
        EnableDeleteConfirm = EnableDeleteConfirm,
        EnableArrangeConfirm = EnableArrangeConfirm,
        EnableSplitConfirm = EnableSplitConfirm,
        SplitUndoable = SplitUndoable,
        EnablePanelAnimations = EnablePanelAnimations,
        OpenDeveloperPanelOnStartup = OpenDeveloperPanelOnStartup,
        EnableFXAA = EnableFXAA,
        UndoStackDepth = UndoStackDepth,
        DeveloperPanelWidth = DeveloperPanelWidth,
        AgentTimeoutSeconds = AgentTimeoutSeconds,
        AgentSystemPrompt = AgentSystemPrompt,
        AgentApiKeyVariable = AgentApiKeyVariable,
        AgentModel = AgentModel,
        AgentEndpoint = AgentEndpoint,
    };
    private void Apply(UserPreferences settings)
    {
        settings.Normalize();
        _loading = true;
        Theme = settings.Theme;
        Language = settings.Language;
        ShowEngineHostConsole = settings.ShowEngineHostConsole;
        FontFamilyName = settings.FontFamilyName;
        FontSize = settings.FontSize;
        EnableSSAO = settings.EnableSSAO;
        ShowCoordinateSystem = settings.ShowCoordinateSystem;
        ShowViewCube = settings.ShowViewCube;
        ShowBuildGrid = settings.ShowBuildGrid;
        ShowViewportStatus = settings.ShowViewportStatus;
        UseOrthographic = settings.UseOrthographic;
        EnableDeleteConfirm = settings.EnableDeleteConfirm;
        EnableArrangeConfirm = settings.EnableArrangeConfirm;
        EnableSplitConfirm = settings.EnableSplitConfirm;
        SplitUndoable = settings.SplitUndoable;
        EnablePanelAnimations = settings.EnablePanelAnimations;
        OpenDeveloperPanelOnStartup = settings.OpenDeveloperPanelOnStartup;
        EnableFXAA = settings.EnableFXAA;
        UndoStackDepth = settings.UndoStackDepth;
        DeveloperPanelWidth = settings.DeveloperPanelWidth;
        AgentTimeoutSeconds = settings.AgentTimeoutSeconds;
        AgentSystemPrompt = settings.AgentSystemPrompt;
        AgentApiKeyVariable = settings.AgentApiKeyVariable;
        AgentModel = settings.AgentModel;
        AgentEndpoint = settings.AgentEndpoint;
        _loading = false;
        _appearance?.Apply(Snapshot());
    }
    [RelayCommand] private void ResetDefaults()
    {
        Apply(new UserPreferences
        {
            AgentEndpoint = AgentEndpoint, AgentModel = AgentModel, AgentApiKeyVariable = AgentApiKeyVariable,
            AgentSystemPrompt = AgentSystemPrompt, AgentTimeoutSeconds = AgentTimeoutSeconds,
            DeveloperPanelWidth = DeveloperPanelWidth, OpenDeveloperPanelOnStartup = OpenDeveloperPanelOnStartup,
            EnablePanelAnimations = EnablePanelAnimations
        });
        try { _store?.Save(Snapshot()); SaveStatus = "已恢复默认设置"; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { SaveStatus = "默认设置保存失败：" + ex.Message; }
    }
}
