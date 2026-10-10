using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace AMLabSlicer.Views;

public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        SetResourceReference(StyleProperty, "ThemedWindowStyle");
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            source.AddHook(WindowMessage);
            QueueWindowRegion();
        };
        SizeChanged += (_, _) => QueueWindowRegion();
        StateChanged += (_, _) => QueueWindowRegion();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02E0) QueueWindowRegion(); // WM_DPICHANGED
        return IntPtr.Zero;
    }

    private void QueueWindowRegion() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateWindowRegion));

    private void UpdateWindowRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        var radius = WindowState == WindowState.Maximized ? 0 : (int)Math.Round(5 * GetDpiForWindow(hwnd) / 96.0);
        // Clip the native window too: WPF's rounded Border alone leaves opaque HWND corners.
        var region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, radius * 2, radius * 2);
        if (region != IntPtr.Zero && SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
        // A successful SetWindowRgn transfers ownership of the region to Windows.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}
