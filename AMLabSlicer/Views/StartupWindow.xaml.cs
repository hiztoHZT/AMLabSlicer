using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AMLabSlicer.Plugin.Wpf.Localization;

namespace AMLabSlicer.Views;

public partial class StartupWindow : Window
{
    public StartupWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text) => UiText.SetText(StatusText, TextBlock.TextProperty, text);

    private void CloseStartup(object sender, RoutedEventArgs e) => Close();

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        base.OnPreviewKeyDown(e);
    }
}
