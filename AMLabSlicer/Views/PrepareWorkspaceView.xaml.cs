using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private double _lastAgentPanelWidth = 360;
        private PreferencesViewModel? _preferences;
        private bool _updatingPanelConstraints;
        private double _availableWorkspaceWidth;

        public PrepareWorkspaceView()
        {
            InitializeComponent();

            Loaded += PrepareWorkspaceView_Loaded;
            Unloaded += PrepareWorkspaceView_Unloaded;
            DataContextChanged += PrepareWorkspaceView_DataContextChanged;
            LeftPanelHost.SizeChanged += WorkspaceHost_SizeChanged;
            ViewportHost.SizeChanged += WorkspaceHost_SizeChanged;
            AgentPanelHost.SizeChanged += WorkspaceHost_SizeChanged;
            AgentContentHost.SizeChanged += WorkspaceHost_SizeChanged;
            OutlinerHost.SizeChanged += WorkspaceHost_SizeChanged;
            ParameterHost.SizeChanged += WorkspaceHost_SizeChanged;
            MainSplitter.DragCompleted += (_, _) => { _lastOpenPanelWidth = LeftPanelColumn.ActualWidth; UpdatePanelConstraints(settlePanels: true); };
        }

        protected override Size MeasureOverride(Size constraint)
        {
            CheckAvailableWidth(constraint.Width);
            CheckAvailableHeight(constraint.Height);
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            CheckAvailableWidth(arrangeBounds.Width);
            CheckAvailableHeight(arrangeBounds.Height);
            return base.ArrangeOverride(arrangeBounds);
        }

        private void CheckAvailableWidth(double width)
        {
            width -= MainContainer.Margin.Left + MainContainer.Margin.Right;
            if (!double.IsFinite(width) || width <= 0 || Math.Abs(width - _availableWorkspaceWidth) < .01) return;
            _availableWorkspaceWidth = width;
            UpdatePanelConstraints(settlePanels: true);
        }

        private void CheckAvailableHeight(double height)
        {
            height -= MainContainer.Margin.Top + MainContainer.Margin.Bottom;
            if (!double.IsFinite(height) || height <= 0) return;
            // The upper fixed row must fit the current window, not just the size at the last drag.
            var maximumOutlinerHeight = Math.Max(0, height - SplitterWidth - ParameterRow.MinHeight);
            OutlinerRow.MinHeight = Math.Min(120, maximumOutlinerHeight);
            OutlinerRow.MaxHeight = maximumOutlinerHeight;
            if (OutlinerRow.Height.IsAbsolute && OutlinerRow.Height.Value > maximumOutlinerHeight)
                OutlinerRow.Height = new GridLength(maximumOutlinerHeight);
            if (!ParameterRow.Height.IsStar) ParameterRow.Height = new GridLength(1, GridUnitType.Star);
        }

        private void PrepareWorkspaceView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateRoundedClip(LeftPanelHost);
            UpdateRoundedClip(ViewportHost);
            UpdateRoundedClip(AgentPanelHost);
            UpdateRoundedClip(AgentContentHost);
            UpdateRoundedClip(OutlinerHost);
            UpdateRoundedClip(ParameterHost);

            if (DataContext is PrepareWorkspaceViewModel vm)
            {
                AttachWorkspaceViewModel(vm);
                ApplyParameterPanelState(vm.IsParameterPanelOpen, immediate: true);
                ApplyAgentPanelState(vm.IsAgentPanelOpen, immediate: true);
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
                ApplyAgentPanelState(vm.IsAgentPanelOpen, immediate: true);
            }
        }

        private void AttachWorkspaceViewModel(INotifyPropertyChanged? viewModel)
        {
            if (ReferenceEquals(_workspaceViewModel, viewModel))
                return;

            if (_workspaceViewModel != null)
                _workspaceViewModel.PropertyChanged -= WorkspaceViewModel_PropertyChanged;
            if (_preferences != null) _preferences.PropertyChanged -= Preferences_PropertyChanged;

            _workspaceViewModel = viewModel;

            if (_workspaceViewModel != null)
                _workspaceViewModel.PropertyChanged += WorkspaceViewModel_PropertyChanged;
            _preferences = (viewModel as PrepareWorkspaceViewModel)?.AppPrefs;
            if (_preferences != null)
            {
                _lastAgentPanelWidth = _preferences.DeveloperPanelWidth;
                _preferences.PropertyChanged += Preferences_PropertyChanged;
            }
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
            if (sender is not PrepareWorkspaceViewModel vm) return;
            if (e.PropertyName == nameof(PrepareWorkspaceViewModel.IsParameterPanelOpen))
                Dispatcher.Invoke(() => ApplyParameterPanelState(vm.IsParameterPanelOpen, immediate: false));
            if (e.PropertyName == nameof(PrepareWorkspaceViewModel.IsAgentPanelOpen))
                Dispatcher.Invoke(() => ApplyAgentPanelState(vm.IsAgentPanelOpen, immediate: false));
        }

        private void Preferences_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PreferencesViewModel.DeveloperPanelWidth) && _preferences != null)
            {
                _lastAgentPanelWidth = _preferences.DeveloperPanelWidth;
                if (DataContext is PrepareWorkspaceViewModel { IsAgentPanelOpen: true }) ApplyAgentPanelState(true, true);
            }
        }
        private void UpdatePanelConstraints(bool settlePanels = false)
        {
            if (_updatingPanelConstraints || _availableWorkspaceWidth <= 0) return;
            _updatingPanelConstraints = true;
            try
            {
                var vm = DataContext as PrepareWorkspaceViewModel;
                var leftOpen = vm?.IsParameterPanelOpen != false;
                var rightOpen = vm?.IsAgentPanelOpen == true;
                var leftRequest = LeftPanelColumn.Width.IsAbsolute && LeftPanelColumn.Width.Value > 0 ? LeftPanelColumn.Width.Value : _lastOpenPanelWidth;
                var rightRequest = AgentPanelColumn.Width.IsAbsolute && AgentPanelColumn.Width.Value > 0 ? AgentPanelColumn.Width.Value : _lastAgentPanelWidth;
                var fitted = WorkspacePanelSizing.Fit(_availableWorkspaceWidth, leftRequest, rightRequest, leftOpen, rightOpen);
                // Reset minima before changing maxima: both panels must fit in the same budget.
                var previousLeftMinimum = LeftPanelColumn.MinWidth;
                var previousRightMinimum = AgentPanelColumn.MinWidth;
                LeftPanelColumn.MinWidth = 0; AgentPanelColumn.MinWidth = 0;
                LeftPanelColumn.MaxWidth = Math.Max(fitted.LeftMinimum, Math.Min(640, _availableWorkspaceWidth - fitted.Right - 300 - (leftOpen ? 4 : 0) - (rightOpen ? 4 : 0)));
                AgentPanelColumn.MaxWidth = Math.Max(fitted.RightMinimum, Math.Min(600, _availableWorkspaceWidth - fitted.Left - 300 - (leftOpen ? 4 : 0) - (rightOpen ? 4 : 0)));
                if (settlePanels)
                {
                    LeftPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null); LeftPanelHost.BeginAnimation(OpacityProperty, null);
                    AgentPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null); AgentPanelHost.BeginAnimation(OpacityProperty, null);
                    LeftPanelColumn.Width = new GridLength(fitted.Left); AgentPanelColumn.Width = new GridLength(fitted.Right);
                    LeftPanelColumn.MinWidth = fitted.LeftMinimum; AgentPanelColumn.MinWidth = fitted.RightMinimum;
                    LeftPanelHost.Visibility = MainSplitter.Visibility = leftOpen ? Visibility.Visible : Visibility.Collapsed;
                    AgentPanelHost.Visibility = AgentSplitter.Visibility = rightOpen ? Visibility.Visible : Visibility.Collapsed;
                    LeftPanelHost.Opacity = leftOpen ? 1 : 0; AgentPanelHost.Opacity = rightOpen ? 1 : 0;
                    PanelSplitterColumn.Width = new GridLength(leftOpen ? 4 : 0); AgentSplitterColumn.Width = new GridLength(rightOpen ? 4 : 0);
                }
                else
                {
                    LeftPanelColumn.MinWidth = Math.Min(previousLeftMinimum, LeftPanelColumn.MaxWidth);
                    AgentPanelColumn.MinWidth = Math.Min(previousRightMinimum, AgentPanelColumn.MaxWidth);
                }
            }
            finally { _updatingPanelConstraints = false; }
        }
        private void AgentSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            UpdatePanelConstraints(settlePanels: true);
            if (_preferences != null && AgentPanelColumn.ActualWidth >= 300)
                _preferences.DeveloperPanelWidth = AgentPanelColumn.ActualWidth;
        }
        private void ApplyAgentPanelState(bool isOpen, bool immediate)
        {
            immediate |= _preferences?.EnablePanelAnimations == false;
            if (immediate) { AgentPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null); AgentPanelHost.BeginAnimation(OpacityProperty, null); }
            UpdatePanelConstraints();
            AgentPanelColumn.MinWidth = 0;
            if (isOpen)
            {
                AgentPanelHost.Visibility = Visibility.Visible;
                AgentSplitter.Visibility = Visibility.Visible;
                AgentSplitterColumn.Width = new GridLength(SplitterWidth);
                var target = Math.Min(_lastAgentPanelWidth, AgentPanelColumn.MaxWidth);
                if (immediate)
                {
                    AgentPanelColumn.Width = new GridLength(target); AgentPanelHost.Opacity = 1;
                    AgentPanelColumn.MinWidth = Math.Min(300, target); UpdatePanelConstraints();
                }
                else AnimatePanel(AgentPanelColumn, AgentPanelHost, target, 1, () => { AgentPanelColumn.MinWidth = Math.Min(300, target); UpdatePanelConstraints(); });
            }
            else
            {
                if (AgentPanelColumn.ActualWidth > 1) _lastAgentPanelWidth = AgentPanelColumn.ActualWidth;
                void Hide() { AgentSplitterColumn.Width = new GridLength(0); AgentPanelHost.Visibility = Visibility.Collapsed; AgentSplitter.Visibility = Visibility.Collapsed; UpdatePanelConstraints(); }
                if (immediate) { AgentPanelColumn.Width = new GridLength(0); AgentPanelHost.Opacity = 0; Hide(); }
                else AnimatePanel(AgentPanelColumn, AgentPanelHost, 0, 0, Hide);
            }
        }

        private void ApplyParameterPanelState(bool isOpen, bool immediate)
        {
            immediate |= _preferences?.EnablePanelAnimations == false;
            if (immediate) { LeftPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null); LeftPanelHost.BeginAnimation(OpacityProperty, null); }
            UpdatePanelConstraints();
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

                AnimatePanel(LeftPanelColumn, LeftPanelHost, targetWidth, 1, () => RestoreOpenPanelMinimum(targetWidth));
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
                UpdatePanelConstraints();
                return;
            }

            AnimatePanel(LeftPanelColumn, LeftPanelHost, 0, 0, () =>
            {
                PanelSplitterColumn.Width = new GridLength(0);
                LeftPanelHost.Visibility = Visibility.Collapsed;
                MainSplitter.Visibility = Visibility.Collapsed;
                UpdatePanelConstraints();
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
            UpdatePanelConstraints();
        }

        private void AnimatePanel(ColumnDefinition column, Border host, double targetWidth, double targetOpacity, Action? completed)
        {
            column.BeginAnimation(ColumnDefinition.WidthProperty, null);
            host.BeginAnimation(OpacityProperty, null);

            var currentWidth = Math.Max(0, column.ActualWidth);
            column.Width = new GridLength(currentWidth);
            host.Opacity = Math.Max(0, Math.Min(1, host.Opacity));

            var widthAnimation = new GridLengthAnimation
            {
                From = new GridLength(currentWidth),
                To = new GridLength(targetWidth),
                Duration = PanelAnimationDuration,
                FillBehavior = FillBehavior.Stop
            };

            widthAnimation.Completed += (_, _) =>
            {
                column.BeginAnimation(ColumnDefinition.WidthProperty, null);
                host.BeginAnimation(OpacityProperty, null);
                column.Width = new GridLength(targetWidth);
                host.Opacity = targetOpacity;
                completed?.Invoke();
            };

            var opacityAnimation = new DoubleAnimation(targetOpacity, PanelAnimationDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            column.BeginAnimation(ColumnDefinition.WidthProperty, widthAnimation);
            host.BeginAnimation(OpacityProperty, opacityAnimation);
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
