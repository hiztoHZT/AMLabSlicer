using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AMLabSlicer.Views
{
    public partial class MainWindow : Window
    {
        private const int WmGetMinMaxInfo = 0x0024;
        private const int MonitorDefaultToNearest = 0x00000002;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(WndProc);
            }
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
                WindowState = WindowState.Maximized;
            else
                WindowState = WindowState.Normal;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmGetMinMaxInfo)
            {
                handled = TryAdjustMaximizedSizeToWorkArea(hwnd, lParam);
            }

            return IntPtr.Zero;
        }

        private static bool TryAdjustMaximizedSizeToWorkArea(IntPtr hwnd, IntPtr lParam)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MonitorInfo();
            if (!GetMonitorInfo(monitor, monitorInfo))
            {
                return false;
            }

            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var workArea = monitorInfo.WorkArea;
            var monitorArea = monitorInfo.MonitorArea;

            var window = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
            var dpiScale = GetDpiScale(window);
            var minWidth = window?.MinWidth > 0 ? window.MinWidth : 800;
            var minHeight = window?.MinHeight > 0 ? window.MinHeight : 600;

            minMaxInfo.MinTrackSize.X = (int)Math.Ceiling(minWidth * dpiScale.X);
            minMaxInfo.MinTrackSize.Y = (int)Math.Ceiling(minHeight * dpiScale.Y);
            minMaxInfo.MaxPosition.X = workArea.Left - monitorArea.Left;
            minMaxInfo.MaxPosition.Y = workArea.Top - monitorArea.Top;
            minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
            minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;

            Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return true;
        }

        private static PointD GetDpiScale(Window? window)
        {
            if (window == null)
            {
                return new PointD(1, 1);
            }

            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget == null)
            {
                return new PointD(1, 1);
            }

            var transform = source.CompositionTarget.TransformToDevice;
            return new PointD(transform.M11, transform.M22);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, MonitorInfo monitorInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        private readonly struct PointD
        {
            public PointD(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MonitorInfo
        {
            public int Size = Marshal.SizeOf<MonitorInfo>();
            public Rect MonitorArea;
            public Rect WorkArea;
            public int Flags;
        }
    }
}
