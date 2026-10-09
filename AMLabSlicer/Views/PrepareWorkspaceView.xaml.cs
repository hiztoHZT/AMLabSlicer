using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AMLabSlicer.ViewModel;

namespace AMLabSlicer.Views
{
    public partial class PrepareWorkspaceView : UserControl
    {
        private const double DefaultPanelWidth = 320;
        private const double MinimumOpenPanelWidth = 280;
        private const double SplitterWidth = 4;
        private static readonly Duration PanelAnimationDuration = new(TimeSpan.FromMilliseconds(180));

        private INotifyPropertyChanged? _workspaceViewModel;
        private double _lastOpenPanelWidth = DefaultPanelWidth;

        public PrepareWorkspaceView()
        {
            InitializeComponent();

            Loaded += PrepareWorkspaceView_Loaded;
            Unloaded += PrepareWorkspaceView_Unloaded;
            DataContextChanged += PrepareWorkspaceView_DataContextChanged;
            LeftPanelHost.SizeChanged += WorkspaceHost_SizeChanged;
            ViewportHost.SizeChanged += WorkspaceHost_SizeChanged;
        }

        private void PrepareWorkspaceView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateRoundedClip(LeftPanelHost);
            UpdateRoundedClip(ViewportHost);

            if (DataContext is PrepareWorkspaceViewModel vm)
            {
                AttachWorkspaceViewModel(vm);
                ApplyParameterPanelState(vm.IsParameterPanelOpen, immediate: true);
            }
        }

        private void PrepareWorkspaceView_Unloaded(object sender, RoutedEventArgs e)
        {
            AttachWorkspaceViewModel(null);
        }

        private void PrepareWorkspaceView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            AttachWorkspaceViewModel(e.NewValue as INotifyPropertyChanged);

            if (e.NewValue is PrepareWorkspaceViewModel vm)
            {
                ApplyParameterPanelState(vm.IsParameterPanelOpen, immediate: true);
            }
        }

        private void AttachWorkspaceViewModel(INotifyPropertyChanged? viewModel)
        {
            if (ReferenceEquals(_workspaceViewModel, viewModel))
                return;

            if (_workspaceViewModel != null)
                _workspaceViewModel.PropertyChanged -= WorkspaceViewModel_PropertyChanged;

            _workspaceViewModel = viewModel;

            if (_workspaceViewModel != null)
                _workspaceViewModel.PropertyChanged += WorkspaceViewModel_PropertyChanged;
        }

        private static void WorkspaceHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Border border)
                UpdateRoundedClip(border);
        }

        private static void UpdateRoundedClip(Border border)
        {
            if (border.ActualWidth <= 0 || border.ActualHeight <= 0)
                return;

            border.Clip = CreateRoundedRectangleGeometry(
                new Rect(0, 0, border.ActualWidth, border.ActualHeight),
                border.CornerRadius);
        }

        private static Geometry CreateRoundedRectangleGeometry(Rect rect, CornerRadius radius)
        {
            var maxRadius = Math.Min(rect.Width, rect.Height) / 2;
            var topLeft = Math.Min(radius.TopLeft, maxRadius);
            var topRight = Math.Min(radius.TopRight, maxRadius);
            var bottomRight = Math.Min(radius.BottomRight, maxRadius);
            var bottomLeft = Math.Min(radius.BottomLeft, maxRadius);

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(rect.Left + topLeft, rect.Top), true, true);

                context.LineTo(new Point(rect.Right - topRight, rect.Top), true, false);
                AddCorner(context, topRight, new Point(rect.Right, rect.Top + topRight));

                context.LineTo(new Point(rect.Right, rect.Bottom - bottomRight), true, false);
                AddCorner(context, bottomRight, new Point(rect.Right - bottomRight, rect.Bottom));

                context.LineTo(new Point(rect.Left + bottomLeft, rect.Bottom), true, false);
                AddCorner(context, bottomLeft, new Point(rect.Left, rect.Bottom - bottomLeft));

                context.LineTo(new Point(rect.Left, rect.Top + topLeft), true, false);
                AddCorner(context, topLeft, new Point(rect.Left + topLeft, rect.Top));
            }

            geometry.Freeze();
            return geometry;
        }

        private static void AddCorner(StreamGeometryContext context, double radius, Point endPoint)
        {
            if (radius <= 0)
            {
                context.LineTo(endPoint, true, false);
                return;
            }

            context.ArcTo(
                endPoint,
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                false);
        }

        private void WorkspaceViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PrepareWorkspaceViewModel.IsParameterPanelOpen) ||
                sender is not PrepareWorkspaceViewModel vm)
            {
                return;
            }

            Dispatcher.Invoke(() => ApplyParameterPanelState(vm.IsParameterPanelOpen, immediate: false));
        }

        private void ApplyParameterPanelState(bool isOpen, bool immediate)
        {
            if (isOpen)
            {
                LeftPanelHost.Visibility = Visibility.Visible;
                MainSplitter.Visibility = Visibility.Visible;
                PanelSplitterColumn.Width = new GridLength(SplitterWidth);
                LeftPanelColumn.MinWidth = 0;

                var targetWidth = CoercePanelWidth(_lastOpenPanelWidth);
                if (immediate)
                {
                    LeftPanelColumn.Width = new GridLength(targetWidth);
                    LeftPanelHost.Opacity = 1;
                    RestoreOpenPanelMinimum(targetWidth);
                    return;
                }

                AnimatePanel(targetWidth, 1, () => RestoreOpenPanelMinimum(targetWidth));
                return;
            }

            if (LeftPanelColumn.ActualWidth > 1)
                _lastOpenPanelWidth = LeftPanelColumn.ActualWidth;

            LeftPanelColumn.MinWidth = 0;
            if (immediate)
            {
                LeftPanelColumn.Width = new GridLength(0);
                PanelSplitterColumn.Width = new GridLength(0);
                LeftPanelHost.Opacity = 0;
                LeftPanelHost.Visibility = Visibility.Collapsed;
                MainSplitter.Visibility = Visibility.Collapsed;
                return;
            }

            AnimatePanel(0, 0, () =>
            {
                PanelSplitterColumn.Width = new GridLength(0);
                LeftPanelHost.Visibility = Visibility.Collapsed;
                MainSplitter.Visibility = Visibility.Collapsed;
            });
        }

        private double CoercePanelWidth(double requestedWidth)
        {
            var maxWidth = LeftPanelColumn.MaxWidth;
            if (double.IsNaN(maxWidth) || double.IsInfinity(maxWidth) || maxWidth <= 0)
                maxWidth = MainContainer.ActualWidth > 0 ? MainContainer.ActualWidth * 0.5 : requestedWidth;

            return Math.Max(0, Math.Min(Math.Max(requestedWidth, MinimumOpenPanelWidth), maxWidth));
        }

        private void RestoreOpenPanelMinimum(double targetWidth)
        {
            LeftPanelColumn.MinWidth = Math.Min(MinimumOpenPanelWidth, Math.Max(0, targetWidth));
        }

        private void AnimatePanel(double targetWidth, double targetOpacity, Action? completed)
        {
            LeftPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            LeftPanelHost.BeginAnimation(OpacityProperty, null);

            var currentWidth = Math.Max(0, LeftPanelColumn.ActualWidth);
            LeftPanelColumn.Width = new GridLength(currentWidth);
            LeftPanelHost.Opacity = Math.Max(0, Math.Min(1, LeftPanelHost.Opacity));

            var widthAnimation = new GridLengthAnimation
            {
                From = new GridLength(currentWidth),
                To = new GridLength(targetWidth),
                Duration = PanelAnimationDuration,
                FillBehavior = FillBehavior.Stop
            };

            widthAnimation.Completed += (_, _) =>
            {
                LeftPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                LeftPanelHost.BeginAnimation(OpacityProperty, null);
                LeftPanelColumn.Width = new GridLength(targetWidth);
                LeftPanelHost.Opacity = targetOpacity;
                completed?.Invoke();
            };

            var opacityAnimation = new DoubleAnimation(targetOpacity, PanelAnimationDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            LeftPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, widthAnimation);
            LeftPanelHost.BeginAnimation(OpacityProperty, opacityAnimation);
        }

        private sealed class GridLengthAnimation : AnimationTimeline
        {
            public static readonly DependencyProperty FromProperty =
                DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

            public static readonly DependencyProperty ToProperty =
                DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

            public GridLength From
            {
                get => (GridLength)GetValue(FromProperty);
                set => SetValue(FromProperty, value);
            }

            public GridLength To
            {
                get => (GridLength)GetValue(ToProperty);
                set => SetValue(ToProperty, value);
            }

            public override Type TargetPropertyType => typeof(GridLength);

            protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

            public override object GetCurrentValue(
                object defaultOriginValue,
                object defaultDestinationValue,
                AnimationClock animationClock)
            {
                var progress = animationClock.CurrentProgress ?? 1;
                var easedProgress = 1 - Math.Pow(1 - progress, 3);
                var value = From.Value + ((To.Value - From.Value) * easedProgress);
                return new GridLength(Math.Max(0, value), GridUnitType.Pixel);
            }
        }
    }
}
