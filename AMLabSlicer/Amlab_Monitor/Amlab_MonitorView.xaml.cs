using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AMLabSlicer.Views;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace AMLabSlicer.Amlab_Monitor;

public partial class Amlab_MonitorView : UserControl
{
    private const double Amlab_MonitorCameraMaxFraction = .8;
    private const double Amlab_MonitorCameraDefaultHeightFraction = .6;
    private const double Amlab_MonitorCameraMinHeight = 120;

    private Amlab_MonitorViewModel? _attachedViewModel;
    private Point? _zoomAnchor;
    private Point? _panPoint;
    private (double Available, bool CameraOpen, bool ConsoleOpen, double CameraFraction, double ConsoleWidth)? _panelLayout;
    private (double Available, double? Preferred)? _cameraHeightLayout;

    public Amlab_MonitorView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _attachedViewModel = DataContext as Amlab_MonitorViewModel;
            InvalidateMeasure();
            if (_attachedViewModel is { } viewModel)
            {
                viewModel.PropertyChanged += Amlab_MonitorLayout_PropertyChanged;
                viewModel.ReloadDeviceSettings();
                if (viewModel.Amlab_MonitorConnectCommand.CanExecute(null))
                    viewModel.Amlab_MonitorConnectCommand.Execute(null);
            }
        };
        Unloaded += (_, _) =>
        {
            EndPan();
            Amlab_MonitorZoomBadge.BeginAnimation(OpacityProperty, null);
            _attachedViewModel?.Amlab_MonitorStopCommand.Execute(null);
            if (_attachedViewModel is { } viewModel)
            {
                viewModel.PropertyChanged -= Amlab_MonitorLayout_PropertyChanged;
                viewModel.DisconnectDevice();
                viewModel.MoonrakerApiKey = "";
            }
            _attachedViewModel = null;
        };
    }

    private void Amlab_MonitorLayout_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Amlab_MonitorViewModel.Amlab_MonitorCameraPanelOpen)
            or nameof(Amlab_MonitorViewModel.Amlab_MonitorConsolePanelOpen))
            InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (DataContext is Amlab_MonitorViewModel model)
        {
            var margin = Amlab_MonitorLayout.Margin;
            UpdateAmlab_MonitorPanelWidths(model, constraint.Width - margin.Left - margin.Right);
            UpdateAmlab_MonitorCameraHeight(model, constraint.Height - margin.Top - margin.Bottom);
        }
        return base.MeasureOverride(constraint);
    }

    private void UpdateAmlab_MonitorPanelWidths(Amlab_MonitorViewModel model, double available)
    {
        if (!double.IsFinite(available) || available <= 0)
            return;

        var cameraOpen = model.Amlab_MonitorCameraPanelOpen;
        var consoleOpen = model.Amlab_MonitorConsolePanelOpen;
        var layout = (available, cameraOpen, consoleOpen, model.Amlab_MonitorCameraPanelFraction, model.Amlab_MonitorConsolePanelWidth);
        if (_panelLayout == layout)
            return;
        _panelLayout = layout;

        var cameraDivider = cameraOpen ? 4 : 0;
        var consoleDivider = consoleOpen ? 4 : 0;
        var budget = available - 300 - cameraDivider - consoleDivider;
        var cameraMaximum = available * Amlab_MonitorCameraMaxFraction;
        var cameraWidth = available * model.Amlab_MonitorCameraPanelFraction;
        if (consoleOpen)
            cameraWidth = Math.Min(cameraWidth, budget - model.Amlab_MonitorConsolePanelWidth);

        var widths = WorkspacePanelSizing.Fit(available, cameraWidth, model.Amlab_MonitorConsolePanelWidth,
            cameraOpen, consoleOpen, cameraMaximum);

        Amlab_MonitorCameraSplitterColumn.Width = new GridLength(cameraDivider);
        Amlab_MonitorConsoleSplitterColumn.Width = new GridLength(consoleDivider);
        SetPanelWidth(Amlab_MonitorCameraColumn, widths.Left, widths.LeftMinimum,
            cameraOpen ? Math.Min(cameraMaximum, budget - widths.Right) : 0);
        SetPanelWidth(Amlab_MonitorConsoleColumn, widths.Right, widths.RightMinimum,
            consoleOpen ? Math.Min(600, budget - widths.Left) : 0);
    }

    private void UpdateAmlab_MonitorCameraHeight(Amlab_MonitorViewModel model, double available)
    {
        if (!double.IsFinite(available) || available <= 0)
            return;

        var layout = (available, model.Amlab_MonitorCameraHeight);
        if (_cameraHeightLayout == layout)
            return;
        _cameraHeightLayout = layout;

        var splitterHeight = Amlab_MonitorCameraHeightSplitterRow.Height.Value;
        var maximum = Math.Max(0, available - splitterHeight - Amlab_MonitorInspectionRow.MinHeight);
        var minimum = Math.Min(Amlab_MonitorCameraMinHeight, maximum);
        var preferred = model.Amlab_MonitorCameraHeight ?? ((available - splitterHeight) * Amlab_MonitorCameraDefaultHeightFraction);
        Amlab_MonitorCameraRow.MinHeight = minimum;
        Amlab_MonitorCameraRow.MaxHeight = maximum;
        Amlab_MonitorCameraRow.Height = new GridLength(Math.Clamp(preferred, minimum, maximum));
    }

    private void Amlab_MonitorCameraHeightSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!e.Canceled)
            RememberAmlab_MonitorCameraHeight();
    }

    private void Amlab_MonitorCameraHeightSplitter_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
            RememberAmlab_MonitorCameraHeight();
    }

    private void RememberAmlab_MonitorCameraHeight()
    {
        if (DataContext is Amlab_MonitorViewModel model)
            model.Amlab_MonitorCameraHeight = Amlab_MonitorCameraRow.Height.Value;
        InvalidateMeasure();
    }
    private static void SetPanelWidth(ColumnDefinition column, double width, double minimum, double maximum)
    {
        column.MinWidth = minimum;
        column.MaxWidth = Math.Max(minimum, maximum);
        column.Width = new GridLength(width);
    }

    private void Amlab_MonitorSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!e.Canceled)
            RememberAmlab_MonitorPanelWidth(sender);
        InvalidateMeasure();
    }

    private void Amlab_MonitorSplitter_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
            RememberAmlab_MonitorPanelWidth(sender);
    }

    private void RememberAmlab_MonitorPanelWidth(object splitter)
    {
        if (DataContext is not Amlab_MonitorViewModel model)
            return;

        if (ReferenceEquals(splitter, Amlab_MonitorCameraSplitter))
        {
            if (_panelLayout is { } layout)
                model.Amlab_MonitorCameraPanelFraction = Amlab_MonitorCameraColumn.Width.Value / layout.Available;
        }
        else
            model.Amlab_MonitorConsolePanelWidth = Amlab_MonitorConsoleColumn.Width.Value;
        InvalidateMeasure();
    }

    private void Amlab_MonitorConsole_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0 && e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - e.ExtentHeightChange - 1)
            ((ScrollViewer)sender).ScrollToEnd();
    }

    private void MoonrakerKey_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is Amlab_MonitorViewModel viewModel && sender is PasswordBox password)
            viewModel.MoonrakerApiKey = password.Password;
    }

    private void ImageViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var limits = GetPanLimits(Amlab_MonitorZoomScale.ScaleX);
        if (limits.LengthSquared == 0 || !Amlab_MonitorImageViewport.CaptureMouse())
            return;

        _panPoint = e.GetPosition(Amlab_MonitorImageViewport);
        e.Handled = true;
    }

    private void ImageViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panPoint is not { } previous)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndPan();
            return;
        }

        var point = e.GetPosition(Amlab_MonitorImageViewport);
        var delta = point - previous;
        _panPoint = point;
        SetPanOffset(Amlab_MonitorZoomOffset.X + delta.X, Amlab_MonitorZoomOffset.Y + delta.Y, Amlab_MonitorZoomScale.ScaleX);
        e.Handled = true;
    }

    private void ImageViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panPoint is null)
            return;

        EndPan();
        e.Handled = true;
    }

    private void ImageViewport_LostMouseCapture(object sender, MouseEventArgs e) => _panPoint = null;

    private void EndPan()
    {
        _panPoint = null;
        if (Amlab_MonitorImageViewport.IsMouseCaptured)
            Amlab_MonitorImageViewport.ReleaseMouseCapture();
    }

    private void ImageViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Amlab_MonitorCameraImage.Source is null)
            return;

        var slider = Amlab_MonitorZoomSlider;
        _zoomAnchor = e.GetPosition(Amlab_MonitorImageViewport);
        try
        {
            slider.Value = Math.Clamp(slider.Value * Math.Pow(1.15, e.Delta / 120d), slider.Minimum, slider.Maximum);
        }
        finally { _zoomAnchor = null; }
        e.Handled = true;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyZoom(e.NewValue, _zoomAnchor);
        if (Amlab_MonitorCameraImage?.Source is null || Amlab_MonitorZoomReadout is null)
            return;

        Amlab_MonitorZoomReadout.Text = e.NewValue.ToString("0%");
        Amlab_MonitorZoomBadge.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop,
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(1, TimeSpan.Zero),
                new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(5)),
                new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(5.25))
            }
        });
    }

    private void CameraSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var surface = (Border)sender;
        var radius = surface.CornerRadius.TopLeft;
        surface.Clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
    }

    private void ImageViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Amlab_MonitorZoomSlider is { } slider)
            ApplyZoom(slider.Value);
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        Amlab_MonitorZoomSlider.Value = Amlab_MonitorZoomSlider.Minimum;
        ApplyZoom(Amlab_MonitorZoomSlider.Value);
    }

    private void ApplyZoom(double zoom, Point? anchor = null)
    {
        if (Amlab_MonitorZoomScale is null || Amlab_MonitorCameraImage?.Source is not BitmapSource)
            return;

        var size = Amlab_MonitorImageViewport.RenderSize;
        var center = new Point(size.Width / 2, size.Height / 2);
        var relative = (anchor ?? center) - center;
        var ratio = zoom / Amlab_MonitorZoomScale.ScaleX;
        SetPanOffset(
            relative.X - (relative.X - Amlab_MonitorZoomOffset.X) * ratio,
            relative.Y - (relative.Y - Amlab_MonitorZoomOffset.Y) * ratio,
            zoom);
        Amlab_MonitorZoomScale.ScaleX = zoom;
        Amlab_MonitorZoomScale.ScaleY = zoom;
    }

    private void SetPanOffset(double x, double y, double zoom)
    {
        var limits = GetPanLimits(zoom);
        Amlab_MonitorZoomOffset.X = Math.Clamp(x, -limits.X, limits.X);
        Amlab_MonitorZoomOffset.Y = Math.Clamp(y, -limits.Y, limits.Y);
    }

    private Vector GetPanLimits(double zoom)
    {
        if (zoom <= 1 || Amlab_MonitorCameraImage.Source is not BitmapSource image)
            return default;

        var size = Amlab_MonitorImageViewport.RenderSize;
        var fit = Math.Min(size.Width / image.Width, size.Height / image.Height);
        return new Vector(
            Math.Max(0, (image.Width * fit * zoom - size.Width) / 2),
            Math.Max(0, (image.Height * fit * zoom - size.Height) / 2));
    }
}
