using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using AMLabSlicer.Core.Commands;
using HelixToolkit.SharpDX.Model.Scene;

namespace AMLabSlicer.ViewModel;

public partial class PrepareWorkspaceViewModel
{
    public ObservableCollection<OutlinerNodeViewModel> SiblingsOf(OutlinerNodeViewModel item) => item.Parent?.Children ?? OutlinerItems;
    [RelayCommand]
    private void UngroupSelection(OutlinerNodeViewModel? group)
    {
        var groups = (group == null ? SelectedItems : new[] { group })
            .Where(x => x.IsGroup && x.Node is GroupNode && x.Node.Parent is GroupNode && AllOutlinerItems.Contains(x)).ToArray();
        groups = groups.Where(x => !groups.Any(other => other.IsAncestorOf(x))).ToArray();
        if (groups.Length == 0) return;
        History.ExecuteCommand(new UngroupObjectsCommand(this, groups));
    }

    private sealed class UngroupObjectsCommand : ICommandAction
    {
        private sealed class GroupState(PrepareWorkspaceViewModel workspace, OutlinerNodeViewModel group)
        {
            public readonly OutlinerNodeViewModel Group = group;
            public readonly GroupNode Parent = (GroupNode)group.Node.Parent!;
            public readonly ObservableCollection<OutlinerNodeViewModel> Siblings = workspace.SiblingsOf(group);
            public readonly OutlinerNodeViewModel[] Members = group.Children.ToArray();
            public readonly System.Numerics.Matrix4x4[] Matrices = group.Children.Select(x => x.Node.ModelMatrix).ToArray();
            public readonly bool[] Locks = group.Children.Select(x => x.IsLocked).ToArray();
            public readonly bool[] Visibility = group.Children.Select(x => x.IsVisible).ToArray();
            public int Index, SceneIndex;
        }
        private readonly PrepareWorkspaceViewModel _workspace;
        private readonly GroupState[] _groups;
        private readonly OutlinerNodeViewModel[] _selection;
        public string Name => "取消分组";
        public UngroupObjectsCommand(PrepareWorkspaceViewModel workspace, OutlinerNodeViewModel[] groups)
        { _workspace = workspace; _groups = groups.Select(x => new GroupState(workspace,x)).ToArray(); _selection = workspace.SelectedItems.ToArray(); }
        public void Execute()
        {
            _workspace._updatingSelection = true;
            try
            {
                foreach (var state in _groups)
                {
                    state.Index = state.Siblings.IndexOf(state.Group);
                    state.SceneIndex = state.Parent.Items.IndexOf(state.Group.Node);
                    state.Parent.RemoveChildNode(state.Group.Node); state.Siblings.Remove(state.Group);
                    for (int i = 0; i < state.Members.Length; i++)
                    {
                        var member = state.Members[i];
                        ((GroupNode)state.Group.Node).RemoveChildNode(member.Node); state.Group.Children.Remove(member);
                        member.Node.ModelMatrix = state.Matrices[i] * state.Group.Node.ModelMatrix;
                        member.IsLocked = state.Locks[i] || state.Group.IsLocked;
                        member.IsVisible = state.Visibility[i] && state.Group.IsVisible;
                        state.Parent.InsertChildNode(state.SceneIndex+i,member.Node); state.Siblings.Insert(state.Index+i,member);
                    }
                }
            }
            finally { _workspace._updatingSelection = false; }
            _workspace.SelectObjects(_groups.SelectMany(x => x.Members));
        }
        public void Undo()
        {
            _workspace._updatingSelection = true;
            try
            {
                foreach (var state in _groups.Reverse())
                {
                    for (int i = 0; i < state.Members.Length; i++)
                    {
                        var member = state.Members[i];
                        state.Parent.RemoveChildNode(member.Node); state.Siblings.Remove(member);
                        member.Node.ModelMatrix = state.Matrices[i]; member.IsLocked = state.Locks[i]; member.IsVisible = state.Visibility[i];
                        ((GroupNode)state.Group.Node).AddChildNode(member.Node); state.Group.Children.Add(member);
                    }
                    state.Parent.InsertChildNode(state.SceneIndex,state.Group.Node); state.Siblings.Insert(state.Index,state.Group);
                }
            }
            finally { _workspace._updatingSelection = false; }
            _workspace.SelectObjects(_selection);
        }
    }
    [RelayCommand]
    private void GroupSelection()
    {
        var selected = SelectedItems.ToArray();
        if (selected.Length < 2) { _dialogs.ShowMessage("请先选择两个或更多同级物体或组。", "编组"); return; }
        var parent = selected[0].Parent;
        if (selected.Any(x => x.Parent != parent) || selected[0].Node.Parent is not GroupNode sceneParent || selected.Any(x => x.Node.Parent != sceneParent))
        { _dialogs.ShowMessage("只能对同级物体或组进行编组。", "编组"); return; }
        var siblings = SiblingsOf(selected[0]);
        selected = siblings.Where(selected.Contains).ToArray();
        var names = AllOutlinerItems.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int suffix = 1; while (names.Contains($"组 {suffix}")) suffix++;
        var group = new OutlinerNodeViewModel(new GroupNode { Name = $"组 {suffix}" }, $"组 {suffix}", isGroup: true);
        History.ExecuteCommand(new GroupObjectsCommand(this, siblings, sceneParent, selected, group));
    }
    public bool ReorderObject(OutlinerNodeViewModel source, OutlinerNodeViewModel target, bool after)
    {
        if (source == target || source.Parent != target.Parent) return false;
        var siblings = SiblingsOf(source);
        if (!siblings.Contains(source) || !siblings.Contains(target)) return false;
        int before = siblings.IndexOf(source), index = siblings.IndexOf(target) + (after ? 1 : 0);
        if (before < index) index--;
        if (before == index) return false;
        History.ExecuteCommand(new ReorderObjectsCommand(siblings, before, index)); return true;
    }
    private sealed class ReorderObjectsCommand(ObservableCollection<OutlinerNodeViewModel> siblings, int before, int after) : ICommandAction
    {
        public string Name => "物体排序";
        private void Move(int from, int to)
        {
            var node = siblings[from].Node;
            if (node.Parent is GroupNode parent)
            {
                var target = siblings[to].Node;
                int sceneFrom = parent.Items.IndexOf(node), sceneTo = parent.Items.IndexOf(target);
                if (sceneFrom >= 0 && sceneTo >= 0) parent.MoveChildNode(sceneFrom, sceneTo);
            }
            siblings.Move(from, to);
        }
        public void Execute() => Move(before, after);
        public void Undo() => Move(after, before);
    }
    private sealed class GroupObjectsCommand : ICommandAction
    {
        private readonly PrepareWorkspaceViewModel _workspace;
        private readonly ObservableCollection<OutlinerNodeViewModel> _siblings;
        private readonly GroupNode _parent;
        private readonly OutlinerNodeViewModel[] _members;
        private readonly OutlinerNodeViewModel _group;
        private readonly int[] _indices;
        private readonly int[] _sceneIndices;
        public string Name => "编组";
        public GroupObjectsCommand(PrepareWorkspaceViewModel workspace, ObservableCollection<OutlinerNodeViewModel> siblings, GroupNode parent, OutlinerNodeViewModel[] members, OutlinerNodeViewModel group)
        {
            _workspace = workspace; _siblings = siblings; _parent = parent; _members = members; _group = group;
            _indices = members.Select(siblings.IndexOf).ToArray(); _sceneIndices = members.Select(x => parent.Items.IndexOf(x.Node)).ToArray();
        }
        public void Execute()
        {
            _workspace._updatingSelection = true;
            try
            {
                foreach (var member in _members) { _parent.RemoveChildNode(member.Node); _siblings.Remove(member); ((GroupNode)_group.Node).AddChildNode(member.Node); _group.Children.Add(member); }
                _parent.InsertChildNode(_sceneIndices.Min(), _group.Node); _siblings.Insert(_indices.Min(), _group);
            }
            finally { _workspace._updatingSelection = false; }
            _workspace.SelectObjects([_group]);
        }
        public void Undo()
        {
            _workspace._updatingSelection = true;
            try
            {
                _parent.RemoveChildNode(_group.Node); _siblings.Remove(_group);
                foreach (var member in _members) { ((GroupNode)_group.Node).RemoveChildNode(member.Node); _group.Children.Remove(member); }
                foreach (var i in Enumerable.Range(0,_members.Length).OrderBy(i => _sceneIndices[i])) _parent.InsertChildNode(_sceneIndices[i],_members[i].Node);
                for (int i=0;i<_members.Length;i++) _siblings.Insert(_indices[i],_members[i]);
            }
            finally { _workspace._updatingSelection = false; }
            _workspace.SelectObjects(_members);
        }
    }
}
