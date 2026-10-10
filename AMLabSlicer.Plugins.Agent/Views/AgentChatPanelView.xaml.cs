using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AMLabSlicer.Plugins.Agent.ViewModel;

namespace AMLabSlicer.Plugins.Agent.Views;

public partial class AgentChatPanelView : UserControl
{
    private AgentChatViewModel? _attached;
    public AgentChatPanelView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach(DataContext as AgentChatViewModel);
        Unloaded += (_, _) => Attach(null);
        DataContextChanged += (_, e) => { if (IsLoaded) Attach(e.NewValue as AgentChatViewModel); };
    }
    private void Attach(AgentChatViewModel? viewModel)
    {
        if (_attached != null) _attached.Messages.CollectionChanged -= MessagesChanged;
        _attached = viewModel;
        if (_attached != null) _attached.Messages.CollectionChanged += MessagesChanged;
    }
    private void MessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Dispatcher.BeginInvoke(() => MessageScroll.ScrollToEnd());
    private void ChatInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && DataContext is AgentChatViewModel vm && vm.SendCommand.CanExecute(null))
        { vm.SendCommand.Execute(null); e.Handled = true; }
    }
}
