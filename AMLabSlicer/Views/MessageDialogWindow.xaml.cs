using System.Windows;

namespace AMLabSlicer.Views;

public partial class MessageDialogWindow : ThemedWindow
{
    public MessageDialogWindow(string message, string title = "提示", bool warning = false)
    {
        InitializeComponent();
        AMLabSlicer.Plugin.Wpf.Localization.UiText.SetText(this, TitleProperty, title);
        AMLabSlicer.Plugin.Wpf.Localization.UiText.SetText(MessageText, System.Windows.Controls.TextBlock.TextProperty, message);
        MessageIcon.Text = warning ? "!" : "i";
        if (warning) MessageIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarningBrush");
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) => Close();
    public static void ShowMessage(Window? owner, string message, string title = "提示", bool warning = false)
    {
        var window = new MessageDialogWindow(message, title, warning);
        if (owner is { IsVisible: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
    }
}
