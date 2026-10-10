using CommunityToolkit.Mvvm.ComponentModel;
namespace AMLabSlicer.ViewModel;

public partial class WorkspacePanelEntry(string id, string title, string icon, object? content) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public object? Content { get; } = content;
    [ObservableProperty] private bool _isSelected;
}
