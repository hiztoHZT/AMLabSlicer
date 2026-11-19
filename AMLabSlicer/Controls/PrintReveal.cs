using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AMLabSlicer.Controls;

public enum PrintRevealProfile
{
    AMLabLogo,
    UniformLayers
}

/// <summary>
/// Reveals its child along the AMLab logo's four bands, including its curved top.
/// Clip is owned by this decorator; apply any additional clip to its child/parent.
/// </summary>
public sealed class PrintReveal : Decorator
{
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile), typeof(PrintRevealProfile), typeof(PrintReveal),
        new PropertyMetadata(PrintRevealProfile.AMLabLogo, OnMaskChanged),
        value => value is PrintRevealProfile.AMLabLogo or PrintRevealProfile.UniformLayers);

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(PrintReveal),
        new PropertyMetadata(0d, OnMaskChanged, CoerceProgress), IsFinite);

    public static readonly DependencyProperty LayerHeightProperty = DependencyProperty.Register(
        nameof(LayerHeight), typeof(double), typeof(PrintReveal),
        new PropertyMetadata(4d, OnMaskChanged), value => IsFinite(value) && (double)value > 0);

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan), typeof(PrintReveal),
        new PropertyMetadata(TimeSpan.FromSeconds(3)), value => (TimeSpan)value > TimeSpan.Zero);

    public static readonly DependencyProperty AutoPlayProperty = DependencyProperty.Register(
        nameof(AutoPlay), typeof(bool), typeof(PrintReveal),
        new PropertyMetadata(true, OnAutoPlayChanged));

    public static readonly DependencyProperty IsLoopingProperty = DependencyProperty.Register(
        nameof(IsLooping), typeof(bool), typeof(PrintReveal), new PropertyMetadata(false));

    public static readonly DependencyProperty EasingSplineProperty = DependencyProperty.Register(
        nameof(EasingSpline), typeof(KeySpline), typeof(PrintReveal), new PropertyMetadata(CreateDefaultSpline()),
        value => value is KeySpline);

    private static KeySpline CreateDefaultSpline()
    {
        var spline = new KeySpline(0.42, 0, 0.58, 1);
        spline.Freeze();
        return spline;
    }

    private readonly RectangleGeometry _finished = new();
    private readonly RectangleGeometry _current = new();
    private readonly GeometryGroup _mask = new() { FillRule = FillRule.Nonzero };

    public PrintReveal()
    {
        Clip = _mask;
        Loaded += (_, _) => { if (AutoPlay) Replay(); };
        Unloaded += (_, _) => Stop();
        SizeChanged += (_, _) => UpdateMask();
    }

    /// <summary>AMLabLogo follows the original SVG bands; UniformLayers scans arbitrary content.</summary>
    public PrintRevealProfile Profile
    {
        get => (PrintRevealProfile)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    /// <summary>Normalized reveal progress, clamped to [0, 1]. Use AutoPlay=False for binding.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Layer height in DIP for UniformLayers only; ignored by AMLabLogo.</summary>
    public double LayerHeight
    {
        get => (double)GetValue(LayerHeightProperty);
        set => SetValue(LayerHeightProperty, value);
    }

    /// <summary>Duration of one direction; a looping round trip takes twice this time. Applied on Replay.</summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>Replay on each Loaded event; false allows external Progress control.</summary>
    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    public event EventHandler? Completed;

    /// <summary>Repeat 0->1->0 forever. Applied on Replay; Completed is not raised for infinite playback.</summary>
    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    /// <summary>Cubic Bezier timing curve; defaults to ease-in-out (0.42,0,0.58,1). Applied on Replay.</summary>
    public KeySpline EasingSpline
    {
        get => (KeySpline)GetValue(EasingSplineProperty);
        set => SetValue(EasingSplineProperty, value);
    }

    /// <summary>Restart with Bezier timing, either once or indefinitely forward/backward.</summary>
    public void Replay()
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(Duration),
            FillBehavior = FillBehavior.HoldEnd,
            AutoReverse = IsLooping,
            RepeatBehavior = IsLooping ? RepeatBehavior.Forever : new RepeatBehavior(1)
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(Duration), (KeySpline)EasingSpline.Clone()));
        animation.Completed += (_, _) => Completed?.Invoke(this, EventArgs.Empty);
        BeginAnimation(ProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Remove the animation clock and retain the currently displayed progress.</summary>
    public void Stop()
    {
        var progress = Progress;
        BeginAnimation(ProgressProperty, null);
        SetCurrentValue(ProgressProperty, progress);
    }

    public void Reset()
    {
        BeginAnimation(ProgressProperty, null);
        SetCurrentValue(ProgressProperty, 0d);
    }

    public void ShowAll()
    {
        BeginAnimation(ProgressProperty, null);
        SetCurrentValue(ProgressProperty, 1d);
    }

    private static bool IsFinite(object value) => double.IsFinite((double)value);
    private static object CoerceProgress(DependencyObject d, object value) => Math.Clamp((double)value, 0, 1);
    private static void OnMaskChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PrintReveal)d).UpdateMask();

    private static void OnAutoPlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PrintReveal)d;
        if (!(bool)e.NewValue) control.Stop();
        else if (control.IsLoaded) control.Replay();
    }

    private void UpdateMask()
    {
        double width = ActualWidth, height = ActualHeight;
        _mask.Children.Clear();
        _mask.Transform = Transform.Identity;
        if (Profile == PrintRevealProfile.AMLabLogo)
        {
            UpdateLogoMask(width, height);
            return;
        }

        _mask.Children.Add(_finished);
        _mask.Children.Add(_current);
        _finished.Rect = Rect.Empty;
        _current.Rect = Rect.Empty;
        if (width <= 0 || height <= 0 || Progress <= 0) return;
        if (Progress >= 1)
        {
            _finished.Rect = new Rect(0, 0, width, height);
            return;
        }

        // A partial top row gets the same scan time as other rows. Bound the count
        // for pathological sub-pixel layer heights; normal DIP values are unaffected.
        double layers = Math.Min(1_000_000, Math.Ceiling(height / LayerHeight));
        double step = Math.Max(LayerHeight, height / layers);
        double position = Progress * layers;
        double row = Math.Floor(position);
        double fraction = position - row;
        double completedHeight = Math.Min(height, row * step);
        double rowHeight = Math.Min(step, height - completedHeight);
        double scanWidth = width * fraction;
        if (completedHeight > 0)
            _finished.Rect = new Rect(0, height - completedHeight, width, completedHeight);
        if (scanWidth > 0 && rowHeight > 0)
            _current.Rect = new Rect(row % 2 == 0 ? 0 : width - scanWidth,
                height - completedHeight - rowHeight, scanWidth, rowHeight);
    }

    private void UpdateLogoMask(double width, double height)
    {
        if (width <= 0 || height <= 0 || Progress <= 0) return;

        // Coordinates from Assets/Images/AMLab_logo_rgb.svg, viewBox 0 0 283.5 219.1.
        // Match the SVG's centered Uniform fit, including letterboxing in square hosts.
        const double logoWidth = 283.5, logoHeight = 219.1;
        double scale = Math.Min(width / logoWidth, height / logoHeight);
        _mask.Transform = new MatrixTransform(scale, 0, 0, scale,
            (width - logoWidth * scale) / 2, (height - logoHeight * scale) / 2);
        void Rectangle(double x, double y, double w, double h)
        {
            if (w > 0 && h > 0) _mask.Children.Add(new RectangleGeometry(new Rect(x, y, w, h)));
        }

        if (Progress >= 1)
        {
            Rectangle(0, 0, logoWidth, logoHeight);
            return;
        }

        double position = Progress * 4;
        int band = (int)Math.Floor(position);
        double fraction = position - band;
        // Exact band boundaries, rather than quarters of the host's height.
        double[] boundaries = { 219.1, 175.3, 131.5, 87.7 };
        Rectangle(0, boundaries[band], logoWidth, logoHeight - boundaries[band]);
        if (band < 3)
        {
            double scanWidth = logoWidth * fraction;
            Rectangle(band % 2 == 0 ? 0 : logoWidth - scanWidth,
                boundaries[band + 1], scanWidth, boundaries[band] - boundaries[band + 1]);
            return;
        }

        // Fourth band: travel right->left along the straight section, then around
        // the quarter-circle bend and UP. A rotating radial cut keeps the front
        // perpendicular to the band; the unchanged SVG supplies the exact contour.
        const double centerX = 131.5, centerRadius = (87.7 + 43.8) / 2;
        const double straightLength = logoWidth - centerX;
        double arcLength = centerRadius * Math.PI / 2;
        double distance = fraction * (straightLength + arcLength);
        if (distance <= straightLength)
        {
            Rectangle(logoWidth - distance, 44, distance, 43.7);
            return;
        }

        Rectangle(centerX, 0, straightLength, 87.7);
        double angle = (distance - straightLength) / centerRadius;
        const double radius = 140; // Larger than outer contour; intersect with the top band area.
        var sector = new StreamGeometry();
        using (var context = sector.Open())
        {
            context.BeginFigure(new Point(centerX, 0), true, true);
            context.LineTo(new Point(centerX, radius), true, false);
            context.ArcTo(new Point(centerX - radius * Math.Sin(angle), radius * Math.Cos(angle)),
                new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        _mask.Children.Add(new CombinedGeometry(GeometryCombineMode.Intersect, sector,
            new RectangleGeometry(new Rect(0, 0, centerX, 87.7))));
    }
}
