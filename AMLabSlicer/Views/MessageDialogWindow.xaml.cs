using System.Windows;
using System.Windows.Controls;
using AMLabSlicer.Plugin.Wpf.Localization;

namespace AMLabSlicer.Views;

public partial class MessageDialogWindow : ThemedWindow
{
    public MessageDialogWindow(string message, string title = "提示", bool warning = false)
    {
        InitializeComponent();
        UiText.SetText(this, TitleProperty, title);
        UiText.SetText(MessageText, System.Windows.Controls.TextBlock.TextProperty, message);
        MessageIcon.Text = warning ? "!" : "i";
        if (warning) MessageIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarningBrush");
    }
    private MessageBoxResult _choice = MessageBoxResult.Cancel;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _choice = MessageBoxResult.Yes;
        Close();
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        _choice = MessageBoxResult.No;
        Close();
    }

    public static MessageBoxResult ShowChoice(Window? owner, string message, string title, string confirm, string? decline = null)
    {
        var window = new MessageDialogWindow(message, title, warning: true);
        UiText.SetText(window.ConfirmButton, ContentControl.ContentProperty, confirm);
        window.CancelButton.Visibility = Visibility.Visible;
        if (decline != null)
        {
            UiText.SetText(window.DeclineButton, ContentControl.ContentProperty, decline);
            window.DeclineButton.Visibility = Visibility.Visible;
        }
        ShowOwned(window, owner);
        return window._choice;
    }

    public static void ShowMessage(Window? owner, string message, string title = "提示", bool warning = false)
    {
        var window = new MessageDialogWindow(message, title, warning);
        ShowOwned(window, owner);
    }

    private static void ShowOwned(Window window, Window? owner)
    {
        if (owner is { IsVisible: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
    }
}
