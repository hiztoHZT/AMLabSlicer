using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AMLabSlicer.Amlab_Monitor.Grpc;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AMLabSlicer.Amlab_Monitor;

public partial class Amlab_MonitorViewModel
{
    private byte[]? _frameBytes;
    private ulong _frameNumber;
    private DetectionOptions? _detectedOptions;
    private DetectionOptions? _cameraOptions;
    private CancellationTokenSource? _cameraFrameCancellation;
    private CancellationTokenSource? _optionUpdate;
    private bool _cameraRecheck;
    private bool _settingOptions;

    partial void OnAmlab_MonitorSelectedModelChanged(VisionModel? oldValue, VisionModel? newValue)
    {
        if (_settingOptions || oldValue?.Id == newValue?.Id)
            return;

        _settingOptions = true;
        Amlab_MonitorConfidence = newValue?.DefaultConfidence.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        Amlab_MonitorIou = newValue?.DefaultIou.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        _settingOptions = false;
        QueueOptionUpdate(immediate: true);
    }

    partial void OnAmlab_MonitorConfidenceChanged(string value) => QueueOptionUpdate();
    partial void OnAmlab_MonitorIouChanged(string value) => QueueOptionUpdate();

    private void QueueOptionUpdate(bool immediate = false)
    {
        if (_settingOptions)
            return;

        _optionUpdate?.Cancel();
        RefreshReport();
        if (_frameBytes is null && _cameraOptions is null)
            return;

        var options = ReadOptions();
        if (options is null)
            return;

        var update = new CancellationTokenSource();
        _optionUpdate = update;
        Amlab_MonitorStopCommand.NotifyCanExecuteChanged();
        _ = UpdateOptionsAsync(options, immediate, update);
    }

    private async Task UpdateOptionsAsync(DetectionOptions options, bool immediate, CancellationTokenSource update)
    {
        try
        {
            if (!immediate)
                await Task.Delay(180, update.Token);
            update.Token.ThrowIfCancellationRequested();

            if (_cameraOptions is not null)
            {
                _cameraOptions = options;
                _cameraRecheck = true;
                _cameraFrameCancellation?.Cancel();
            }
            else if (!Amlab_MonitorBusy && _frameBytes is { } image)
            {
                Amlab_MonitorStatus = Text("正在重新检测当前帧…");
                var result = await VisionClient.DetectFrameAsync(options, image, update.Token);
                update.Token.ThrowIfCancellationRequested();
                ApplyResult(image, result, options);
            }
        }
        catch (Exception error)
        {
            if (!update.IsCancellationRequested)
                ShowFailure(error);
        }
        finally
        {
            if (ReferenceEquals(_optionUpdate, update))
            {
                _optionUpdate = null;
                Amlab_MonitorStopCommand.NotifyCanExecuteChanged();
            }
            update.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(Amlab_MonitorCanDetect))]
    private async Task Amlab_MonitorDetectImageAsync(CancellationToken token)
    {
        if (ReadOptions() is not { } options)
            return;
        var dialog = new OpenFileDialog
        {
            Title = Text("打开检测图像"),
            Filter = "JPEG / PNG (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        StartDetection();
        try
        {
            var image = await Amlab_MonitorVisionClient.ReadImageAsync(dialog.FileName, token);
            var result = await VisionClient.DetectFrameAsync(options, image, token);
            token.ThrowIfCancellationRequested();
            ApplyResult(image, result, options);
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                ShowFailure(error);
        }
        finally
        {
            FinishDetection(token);
            if (!token.IsCancellationRequested && _frameBytes is not null &&
                ReadOptions(reportError: false) is { } current && !current.Equals(options))
                QueueOptionUpdate(immediate: true);
        }
    }

    [RelayCommand(CanExecute = nameof(Amlab_MonitorCanDetect))]
    private async Task Amlab_MonitorStartCameraAsync(CancellationToken token)
    {
        if (ReadOptions() is not { } options)
            return;
        if (!Uri.TryCreate(Amlab_MonitorSnapshotUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Amlab_MonitorStatus = Text("请输入 HTTP 或 HTTPS 摄像头快照地址");
            return;
        }

        StartDetection();
        _cameraOptions = options;
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var frameCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                _cameraFrameCancellation = frameCancellation;
                try
                {
                    var frameOptions = _cameraOptions;
                    var recheck = _cameraRecheck;
                    _cameraRecheck = false;
                    var image = recheck && _frameBytes is { } current ? current :
                        await Amlab_MonitorVisionClient.ReadSnapshotAsync(uri, frameCancellation.Token);
                    var result = await VisionClient.DetectFrameAsync(frameOptions, image, frameCancellation.Token);
                    frameCancellation.Token.ThrowIfCancellationRequested();
                    // Keep the displayed frame stable while a valid edit waits to be applied.
                    if (ReadOptions(reportError: false) is not { } desired || desired.Equals(frameOptions))
                        ApplyResult(image, result, frameOptions);
                    await Task.Delay(150, frameCancellation.Token);
                }
                catch (Exception) when (frameCancellation.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    // The next iteration uses the new options and the currently displayed frame.
                }
                finally { _cameraFrameCancellation = null; }
            }
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
                ShowFailure(error);
        }
        finally
        {
            _cameraOptions = null;
            _cameraRecheck = false;
            _optionUpdate?.Cancel();
            FinishDetection(token);
        }
    }

    [RelayCommand(CanExecute = nameof(Amlab_MonitorCanStop))]
    private void Amlab_MonitorStop()
    {
        _optionUpdate?.Cancel();
        Amlab_MonitorConnectCommand.Cancel();
        Amlab_MonitorDetectImageCommand.Cancel();
        Amlab_MonitorStartCameraCommand.Cancel();
    }

    private void StartDetection()
    {
        _optionUpdate?.Cancel();
        _frameBytes = null;
        _frameNumber = 0;
        _detectedOptions = null;
        Amlab_MonitorCameraFrame = null;
        ResetReport();
        RefreshReport();
        Amlab_MonitorBusy = true;
        Amlab_MonitorStatus = Text("等待检测结果…");
    }

    private void FinishDetection(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            Amlab_MonitorStatus = Text("检测已停止");
        Amlab_MonitorBusy = false;
    }

    private bool Amlab_MonitorCanDetect() => !Amlab_MonitorBusy && Amlab_MonitorSelectedModel is not null;
    private bool Amlab_MonitorCanStop() => Amlab_MonitorBusy || _optionUpdate is not null;

    private DetectionOptions? ReadOptions(bool reportError = true)
    {
        if (Amlab_MonitorSelectedModel is not { } model)
            return null;
        if (!IsThreshold(Amlab_MonitorConfidence, out var confidence) || !IsThreshold(Amlab_MonitorIou, out var iou))
        {
            if (reportError)
                Amlab_MonitorStatus = Text("置信度和 IoU 必须在 0 到 1 之间");
            return null;
        }
        return new DetectionOptions { ModelId = model.Id, Confidence = confidence, Iou = iou };
    }

    private static bool IsThreshold(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
        float.IsFinite(value) && value >= 0 && value <= 1;

    private void ApplyResult(byte[] image, DetectionResult result, DetectionOptions options)
    {
        var sameFrame = ReferenceEquals(image, _frameBytes);
        var frame = sameFrame ? (BitmapSource)Amlab_MonitorCameraFrame! : DecodeFrame(image);
        if (frame.PixelWidth != result.Width || frame.PixelHeight != result.Height)
            throw new InvalidDataException("The result dimensions do not match the displayed image.");

        if (_detectedOptions?.ModelId != options.ModelId)
            ResetReport();
        if (!sameFrame)
            _frameNumber++;
        _frameBytes = image;
        _detectedOptions = options;
        Amlab_MonitorCameraFrame = frame;
        UpdateReport(result.Defects, sameFrame);
        Amlab_MonitorStatus = result.Defects.Count == 0
            ? Text("当前帧未检出缺陷")
            : string.Format(Text("第 {0} 帧 · 检出 {1} 个缺陷"), _frameNumber, result.Defects.Count);
    }

    private static BitmapSource DecodeFrame(byte[] image)
    {
        using var buffer = new MemoryStream(image, writable: false);
        var frame = new BitmapImage();
        frame.BeginInit();
        frame.CacheOption = BitmapCacheOption.OnLoad;
        frame.StreamSource = buffer;
        frame.EndInit();
        frame.Freeze();
        return frame;
    }
}
