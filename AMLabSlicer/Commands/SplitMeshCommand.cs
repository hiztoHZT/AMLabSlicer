using System.Collections.ObjectModel;
using AMLabSlicer.Core.Commands;
using AMLabSlicer.ViewModel;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.Commands;

public sealed class SplitMeshCommand : ICommandAction
{
    private readonly GroupNode _parent;
    private readonly MeshNode _source;
    private readonly IReadOnlyList<MeshNode> _parts;
    private readonly ObservableCollection<OutlinerNodeViewModel> _outlinerChildren;
    private readonly IReadOnlyList<OutlinerNodeViewModel> _previousChildren;

    public SplitMeshCommand(GroupNode parent, MeshNode source, IReadOnlyList<MeshNode> parts,
        ObservableCollection<OutlinerNodeViewModel> outlinerChildren)
    {
        _parent = parent;
        _source = source;
        _parts = parts;
        _outlinerChildren = outlinerChildren;
        _previousChildren = outlinerChildren.ToList();
    }

    public string Name => $"拆分 {_source.Name ?? "模型"}";

    public void Execute()
    {
        _parent.RemoveChildNode(_source);
        foreach (var part in _parts) _parent.AddChildNode(part);
        _outlinerChildren.Clear();
        foreach (var part in _parts)
            _outlinerChildren.Add(OutlinerNodeViewModel.BuildTree(part, part.Name ?? "拆分对象"));
    }

    public void Undo()
    {
        foreach (var part in _parts) _parent.RemoveChildNode(part);
        _parent.AddChildNode(_source);
        _outlinerChildren.Clear();
        foreach (var item in _previousChildren) _outlinerChildren.Add(item);
    }
}
