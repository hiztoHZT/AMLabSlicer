using System.ComponentModel;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.ViewModel;

public partial class PrepareWorkspaceViewModel
{
    public event EventHandler? SelectionChanged;
    private bool _updatingSelection;
    private readonly HashSet<OutlinerNodeViewModel> _trackedSelectionItems = new();
    public IReadOnlyList<OutlinerNodeViewModel> SelectedItems => AllOutlinerItems.Where(x => x.IsSelected && !x.IsEffectivelyLocked && x.IsEffectivelyVisible).ToArray();
    public bool CanTransformSelection => SelectedItems.Count > 0 && SelectedItems.All(item => !item.DescendantsAndSelf().Any(member => member.IsEffectivelyLocked));
    public OutlinerNodeViewModel? ActiveSelection { get; private set; }

    public IEnumerable<OutlinerNodeViewModel> AllOutlinerItems => OutlinerItems.SelectMany(x => x.DescendantsAndSelf());
    private void InitializeSelection() => OutlinerItems.CollectionChanged += OutlinerStructureChanged;
    private void OutlinerStructureChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var all = AllOutlinerItems.ToHashSet();
        foreach (var old in _trackedSelectionItems.Where(x => !all.Contains(x)).ToArray())
        { old.PropertyChanged -= SelectionItemChanged; old.Children.CollectionChanged -= OutlinerStructureChanged; _trackedSelectionItems.Remove(old); }
        foreach (var item in all.Where(x => !_trackedSelectionItems.Contains(x)))
        { item.PropertyChanged += SelectionItemChanged; item.Children.CollectionChanged += OutlinerStructureChanged; _trackedSelectionItems.Add(item); }
        PublishSelection();
    }
    private void SelectionItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutlinerNodeViewModel.IsSelected) or nameof(OutlinerNodeViewModel.IsLocked) or nameof(OutlinerNodeViewModel.IsVisible) or nameof(OutlinerNodeViewModel.IsEffectivelyLocked) or nameof(OutlinerNodeViewModel.IsEffectivelyVisible) or nameof(OutlinerNodeViewModel.Name))
            PublishSelection();
    }
    private void PublishSelection()
    {
        if (_updatingSelection) return;
        if (ActiveSelection == null || !SelectedItems.Contains(ActiveSelection)) ActiveSelection = SelectedItems.LastOrDefault();
        OnPropertyChanged(nameof(SelectedItems)); OnPropertyChanged(nameof(ActiveSelection));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SelectObjects(IEnumerable<OutlinerNodeViewModel> items)
    {
        var eligible = items.Where(x => _trackedSelectionItems.Contains(x) && !x.IsEffectivelyLocked && x.IsEffectivelyVisible).Distinct().ToArray();
        eligible = eligible.Where(x => !eligible.Any(parent => parent.IsAncestorOf(x))).ToArray();
        var wanted = eligible.ToHashSet();
        _updatingSelection = true;
        try { foreach (var item in AllOutlinerItems) item.IsSelected = wanted.Contains(item); ActiveSelection = eligible.LastOrDefault(); }
        finally { _updatingSelection = false; }
        PublishSelection();
    }
    public void SelectObject(SceneNode? node, bool toggle = false)
    {
        var item = AllOutlinerItems.FirstOrDefault(x => ReferenceEquals(x.Node, node));
        if (item != null && (item.IsEffectivelyLocked || !item.IsEffectivelyVisible)) return;
        if (toggle && item != null)
            SelectObjects(item.IsSelected ? SelectedItems.Where(x => x != item) : SelectedItems.Where(x => !x.IsAncestorOf(item)).Append(item));
        else if (!toggle) SelectObjects(item == null ? Array.Empty<OutlinerNodeViewModel>() : new[] { item });
    }
    public void SelectAllObjects() => SelectObjects(OutlinerItems);
    public void HideSelectedObjects()
    {
        foreach (var item in SelectedItems.ToArray())
            if (item.CanToggleVisibility) item.IsVisible = false;
    }
    public void LockSelectedObjects()
    {
        foreach (var item in SelectedItems.ToArray())
            if (item.CanToggleLock) item.IsLocked = true;
    }
    public void ShowAllObjects()
    {
        foreach (var item in AllOutlinerItems.ToArray()) item.IsVisible = true;
    }
    public void UnlockAllObjects()
    {
        foreach (var item in AllOutlinerItems.ToArray()) item.IsLocked = false;
    }
    public void InvertObjectSelection() => SelectObjects((ActiveSelection?.Parent?.Children ?? OutlinerItems).Where(x => !x.IsSelected).ToArray());
    public OutlinerNodeViewModel? FindSelectableObject(SceneNode hit)
    {
        for (SceneNode? node = hit; node != null; node = node.Parent)
        {
            var item = AllOutlinerItems.FirstOrDefault(x => x.Node == node);
            if (item == null) continue;
            for (var parent = item.Parent; parent != null; parent = parent.Parent) if (!parent.IsExpanded) item = parent;
            return item;
        }
        return null;
    }
}
