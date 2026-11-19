using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMLabSlicer.Amlab_Monitor.Grpc;
using AMLabSlicer.Plugin.Wpf.Localization;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;

namespace AMLabSlicer.Amlab_Monitor;

public partial class Amlab_MonitorViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amlab_MonitorDeviceColumns))]
    private bool _amlab_MonitorCameraPanelOpen = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amlab_MonitorDeviceColumns))]
    private bool _amlab_MonitorConsolePanelOpen = true;

    public int Amlab_MonitorDeviceColumns => Amlab_MonitorCameraPanelOpen || Amlab_MonitorConsolePanelOpen ? 1 : 2;

    internal double Amlab_MonitorCameraPanelFraction { get; set; } = 2d / 3;
    internal double? Amlab_MonitorCameraHeight { get; set; }
    internal double Amlab_MonitorConsolePanelWidth { get; set; } = 320;

    [RelayCommand]
    private void ToggleAmlab_MonitorCameraPanel() => Amlab_MonitorCameraPanelOpen = !Amlab_MonitorCameraPanelOpen;

    [RelayCommand]
    private void ToggleAmlab_MonitorConsolePanel() => Amlab_MonitorConsolePanelOpen = !Amlab_MonitorConsolePanelOpen;

    private readonly Func<string?> _pickWeight;
    private readonly Yolo11EngineProcess? _engine;
    private Amlab_MonitorVisionClient VisionClient => _engine is null ? Amlab_MonitorVisionClient.Standalone : _engine.Client;

    public Amlab_MonitorViewModel()
        : this(() => Amlab_MonitorSettingsViewModel.PickWeightFile(Application.Current.MainWindow)) { }

    internal Amlab_MonitorViewModel(Func<string?> pickWeight)
    {
        _pickWeight = pickWeight;
        var settings = MoonrakerConnectionSettings.Load();
        _moonrakerAddress = settings.Address;
        _deviceRefreshSeconds = settings.RefreshSeconds.ToString(CultureInfo.CurrentCulture);
    }

    internal Amlab_MonitorViewModel(Yolo11EngineProcess engine) : this() => _engine = engine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amlab_MonitorHasFrame))]
    private ImageSource? _amlab_MonitorCameraFrame;
    public bool Amlab_MonitorHasFrame => Amlab_MonitorCameraFrame is not null;
    [ObservableProperty] private ImageSource? _amlab_MonitorDefectOverlay;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amlab_MonitorOverlayLabel))]
    private bool _amlab_MonitorShowOverlay = true;

    public string Amlab_MonitorOverlayLabel => Amlab_MonitorShowOverlay ? "缺陷叠加：开" : "缺陷叠加：关";

    [ObservableProperty] private IReadOnlyList<VisionModel> _amlab_MonitorModels = Array.Empty<VisionModel>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Amlab_MonitorDetectImageCommand), nameof(Amlab_MonitorStartCameraCommand))]
    private VisionModel? _amlab_MonitorSelectedModel;

    [ObservableProperty] private string _amlab_MonitorConfidence = "";
    [ObservableProperty] private string _amlab_MonitorIou = "";
    [ObservableProperty] private string _amlab_MonitorSnapshotUrl = "";
    [ObservableProperty] private string _amlab_MonitorStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(Amlab_MonitorConnectCommand), nameof(Amlab_MonitorDetectImageCommand),
        nameof(Amlab_MonitorStartCameraCommand), nameof(Amlab_MonitorStopCommand), nameof(Amlab_MonitorQuickAddWeightCommand))]
    private bool _amlab_MonitorBusy;

    private bool _refreshCatalogAfterOperation;
    private string? _preferredModelId;

    internal void RequestModelCatalogRefresh(string? preferredModelId)
    {
        _preferredModelId = preferredModelId;
        _refreshCatalogAfterOperation = true;
        ScheduleModelCatalogRefresh();
    }

    partial void OnAmlab_MonitorBusyChanged(bool value)
    {
        if (!value && _refreshCatalogAfterOperation)
            ScheduleModelCatalogRefresh();
    }

    private void ScheduleModelCatalogRefresh()
    {
        if (Amlab_MonitorBusy)
            return;

        // Wait for the current command to finish updating CanExecute.
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_refreshCatalogAfterOperation && Amlab_MonitorConnectCommand.CanExecute(null))
                Amlab_MonitorConnectCommand.Execute(null);
        }));
    }

    [RelayCommand(CanExecute = nameof(Amlab_MonitorCanConnect))]
    private async Task Amlab_MonitorConnectAsync(CancellationToken token)
    {
        Amlab_MonitorBusy = true;
        _optionUpdate?.Cancel();
        _refreshCatalogAfterOperation = false;
        var preferredId = _preferredModelId;
        var recheck = false;
        Amlab_MonitorStatus = Text("正在连接视觉服务…");
        try
        {
            var registered = Amlab_MonitorModelStore.Load().Entries;
            if (_engine is not null)
                await _engine.EnsureStartedAsync(token);
            var models = await VisionClient.ListModelsAsync(token);
            token.ThrowIfCancellationRequested();
            var previousId = Amlab_MonitorSelectedModel?.Id;
            var confidence = Amlab_MonitorConfidence;
            var iou = Amlab_MonitorIou;
            _settingOptions = true;
            var available = models.Select(model => (model.Id, model.ModelName)).ToHashSet();
            var synchronized = registered.All(entry => available.Contains((entry.Id, entry.Name)));
            Amlab_MonitorModels = models;
            Amlab_MonitorSelectedModel = models.FirstOrDefault(model => model.Id == preferredId) ??
                models.FirstOrDefault(model => model.Id == previousId) ?? models.FirstOrDefault();
            if (previousId is not null && Amlab_MonitorSelectedModel?.Id == previousId)
            {
                Amlab_MonitorConfidence = confidence;
                Amlab_MonitorIou = iou;
            }
            else
            {
                Amlab_MonitorConfidence = Amlab_MonitorSelectedModel?.DefaultConfidence.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
                Amlab_MonitorIou = Amlab_MonitorSelectedModel?.DefaultIou.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
            }
            _settingOptions = false;
            if (synchronized && !_refreshCatalogAfterOperation)
                _preferredModelId = null;
            Amlab_MonitorStatus = Text(synchronized
                ? models.Count == 0 ? "暂无可用算法" : "视觉服务已连接"
                : "视觉服务未同步已保存的模型，请重新连接");
            recheck = _frameBytes is not null && ReadOptions(reportError: false) is { } desired &&
                (!desired.Equals(_detectedOptions) || (synchronized && desired.ModelId == preferredId));
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                Amlab_MonitorSelectedModel = null;
                Amlab_MonitorModels = Array.Empty<VisionModel>();
                ShowFailure(error);
            }
        }
        finally
        {
            _settingOptions = false;
            Amlab_MonitorBusy = false;
        }
        if (recheck)
            QueueOptionUpdate(immediate: true);
    }

    [RelayCommand(CanExecute = nameof(Amlab_MonitorCanConnect))]
    private void Amlab_MonitorQuickAddWeight()
    {
        var settings = new Amlab_MonitorSettingsViewModel(_pickWeight);
        if (settings.AddAndSave() is { } modelId)
            RequestModelCatalogRefresh(modelId);
        else if (!string.IsNullOrEmpty(settings.Amlab_MonitorStatus))
            Amlab_MonitorStatus = settings.Amlab_MonitorStatus;
    }

    private bool Amlab_MonitorCanConnect() => !Amlab_MonitorBusy;

    private static DrawingImage CreateOverlay(BitmapSource frame, IReadOnlyList<DefectBox> defects)
    {
        var bounds = new Rect(0, 0, frame.PixelWidth, frame.PixelHeight);
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(bounds) };
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Brushes.Transparent, null, bounds);
            var pen = new Pen(Brushes.OrangeRed, Math.Max(2, frame.PixelWidth / 280d));
            foreach (var defect in defects)
            {
                var rectangle = new Rect(new Point(defect.XMin, defect.YMin), new Point(defect.XMax, defect.YMax));
                context.DrawRectangle(null, pen, rectangle);
            }
            foreach (var defect in defects)
                DrawDefectLabel(context, bounds, defect);
        }
        var overlay = new DrawingImage(drawing);
        overlay.Freeze();
        return overlay;
    }

    private static string FormatDefect(DefectBox defect) => $"{defect.ClassName}  {defect.Confidence:P1}";

    private static void DrawDefectLabel(DrawingContext context, Rect image, DefectBox defect)
    {
        var fontSize = Math.Max(12, Math.Max(image.Width, image.Height) / 42);
        var padding = Math.Min(fontSize / 4, image.Width / 4);
        var text = new FormattedText(FormatDefect(defect), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            fontSize, Brushes.White, 1)
        {
            MaxTextWidth = image.Width - 2 * padding,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        var width = text.Width + 2 * padding;
        var height = text.Height + padding;
        var left = Math.Clamp(defect.XMin, 0, Math.Max(0, image.Width - width));
        var top = defect.YMin >= height ? defect.YMin - height : defect.YMin;
        top = Math.Clamp(top, 0, Math.Max(0, image.Height - height));
        context.DrawRectangle(Brushes.Firebrick, null, new Rect(left, top, width, height));
        context.DrawText(text, new Point(left + padding, top + padding / 2));
    }

    private void ShowFailure(Exception error)
    {
        Amlab_MonitorStatus = error switch
        {
            RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded }
                => Text("视觉服务暂不可用，请点击“连接视觉服务”重试"),
            RpcException rpc => rpc.Status.Detail,
            OperationCanceledException => Text("读取摄像头快照超时"),
            _ => error.Message
        };
    }

    private static string Text(string key) => UiText.Current.Translate(key);
}
