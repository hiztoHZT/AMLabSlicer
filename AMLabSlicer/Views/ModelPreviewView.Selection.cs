using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelixToolkit.SharpDX;
using HelixToolkit.SharpDX.Model.Scene;
using HelixToolkit.Wpf.SharpDX;
using AMLabSlicer.ViewModel;
using AMLabSlicer.Services;
using AMLabSlicer.Core.Commands;

namespace AMLabSlicer.Views;

public partial class ModelPreviewView
{
    private bool _syncingSelection;
    private bool _boxSelectionArmed, _boxSelectionDragging;
    private Point _boxStart;
    private readonly Dictionary<MeshNode, string> _outlinedMeshes = new();
    private Dictionary<SceneNode, Matrix4x4> _selectionTransformStart = new();
    private SceneNode? _transformPrimary;

    private SceneNode[] SelectedObjects() => GetVM() is { } vm
        ? vm.SelectedItems.Select(x => x.Node).ToArray()
        : SelectedNode == null ? [] : [SelectedNode];

    private void WorkspaceSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            CancelObjectBox();
            if (_isModalTransformActive) ExitModalTransform(false);
            if (!string.IsNullOrEmpty(_activeTransformKey)) DeactivateTransform(false);
            var active = GetVM()?.ActiveSelection?.Node;
            bool changed = !ReferenceEquals(SelectedNode, active);
            SelectedNode = active;
            // The dependency-property callback refreshes a changed primary node.
            if (!changed)
            {
                RefreshObjectHighlight();
                RefreshToolbarEnabled();
                UpdateStatusInfo();
            }
        }
        finally { _syncingSelection = false; }
    }
    private static IEnumerable<MeshNode> ObjectMeshes(SceneNode node)
    {
        if (node is MeshNode mesh) yield return mesh;
        if (node is GroupNode group)
            foreach (var child in group.Items)
                foreach (var m in ObjectMeshes(child)) yield return m;
    }
    private bool TryComputeSelectionAabb(out Vector3 min, out Vector3 max, bool fast = false)
    {
        min = new Vector3(float.PositiveInfinity); max = new Vector3(float.NegativeInfinity);
        bool found = false;
        foreach (var node in SelectedObjects())
        {
            if (!TryComputeWorldAabb(node, out var lo, out var hi, fast)) continue;
            min = Vector3.Min(min, lo); max = Vector3.Max(max, hi); found = true;
        }
        return found;
    }
    private void ClearSelectionOutline()
    {
        foreach (var pair in _outlinedMeshes) pair.Key.PostEffects = pair.Value;
        _outlinedMeshes.Clear();
    }
    private void RefreshSelectionOutline()
    {
        ClearSelectionOutline();
        if (!(GetVM()?.AppPrefs.ShowSelectionOutline ?? true)) return;
        foreach (var node in SelectedObjects())
            foreach (var mesh in ObjectMeshes(node))
            {
                _outlinedMeshes[mesh] = mesh.PostEffects ?? "";
                mesh.PostEffects = string.IsNullOrWhiteSpace(mesh.PostEffects) ? "selectionOutline" : mesh.PostEffects + ";selectionOutline";
            }
    }
    private SceneNode? ObjectAt(Point point)
    {
        var vm = GetVM();
        foreach (var hit in MainViewport.FindHits(point) ?? [])
            if (hit.ModelHit is SceneNode sn)
            {
                var item = vm?.FindSelectableObject(sn);
                if (item is { IsEffectivelyLocked: false, IsEffectivelyVisible: true }) return item.Node;
            }
        return null;
    }
    private bool HandleObjectSelectionKey(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || _isModalTransformActive || !string.IsNullOrEmpty(_activeTransformKey)) return false;
        var vm = GetVM(); if (vm == null) return false;
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.None) { vm.HideSelectedObjects(); e.Handled = true; return true; }
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.None) { vm.LockSelectedObjects(); e.Handled = true; return true; }
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Shift) { vm.ShowAllObjects(); e.Handled = true; return true; }
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Shift) { vm.UnlockAllObjects(); e.Handled = true; return true; }
        if (e.Key == Key.G && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) vm.UngroupSelectionCommand.Execute(null);
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) vm.SelectAllObjects();
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Shift) vm.SelectObject(ObjectAt(Mouse.GetPosition(MainViewport)), true);
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Shift) vm.GroupSelectionCommand.Execute(null);
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Shift) vm.InvertObjectSelection();
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Shift)
        { _boxSelectionArmed = true; Cursor = Cursors.Cross; }
        else if (e.Key == Key.Escape)
        { CancelObjectBox(); vm.SelectObjects([]); }
        else return false;
        e.Handled = true; return true;
    }
    private void CancelObjectBox()
    {
        _boxSelectionArmed = _boxSelectionDragging = false;
        ObjectSelectionRectangle.Visibility = Visibility.Collapsed;
        MainViewport.ReleaseMouseCapture(); Cursor = Cursors.Arrow;
    }
    private void UpdateObjectBox(Point point)
    {
        var r = new Rect(_boxStart, point);
        Canvas.SetLeft(ObjectSelectionRectangle, r.Left); Canvas.SetTop(ObjectSelectionRectangle, r.Top);
        ObjectSelectionRectangle.Width = r.Width; ObjectSelectionRectangle.Height = r.Height;
    }
    // Check every vertex, not merely the center or overlap: partial enclosure never selects.
    private bool ObjectFitsBox(SceneNode node, Rect box)
    {
        if (MainViewport.Camera is not ProjectionCamera camera) return false;
        var position = new Vector3((float)camera.Position.X, (float)camera.Position.Y, (float)camera.Position.Z);
        var forward = Vector3.Normalize(new Vector3((float)camera.LookDirection.X, (float)camera.LookDirection.Y, (float)camera.LookDirection.Z));
        bool found = false;
        foreach (var mesh in ObjectMeshes(node))
        {
            if (!mesh.Visible || mesh.Geometry?.Positions == null) continue;
            var world = SceneTransforms.GetWorldMatrix(mesh);
            foreach (var vertex in mesh.Geometry.Positions)
            {
                found = true; var p = Vector3.Transform(vertex, world);
                if (Vector3.Dot(p - position, forward) <= camera.NearPlaneDistance) return false;
                var screen = MainViewport.Project(p);
                if (!double.IsFinite(screen.X) || !double.IsFinite(screen.Y) || !box.Contains(new Point(screen.X, screen.Y))) return false;
            }
        }
        return found;
    }
    private void BeginSelectionTransform()
    {
        _transformPrimary = SelectedNode;
        _selectionTransformStart = SelectedObjects().ToDictionary(x => x, x => x.ModelMatrix);
    }
    private void UpdateSelectionTransform()
    {
        if (_transformPrimary == null || !_selectionTransformStart.TryGetValue(_transformPrimary, out var start) || !Matrix4x4.Invert(start, out var inverse)) return;
        // Imported roots share the scene parent. Apply the same world delta to every selected root.
        var parentWorld = _transformPrimary.Parent == null ? Matrix4x4.Identity : SceneTransforms.GetWorldMatrix(_transformPrimary.Parent);
        if (!Matrix4x4.Invert(parentWorld, out var inverseParent)) return;
        var deltaWorld = inverseParent * inverse * _transformPrimary.ModelMatrix * parentWorld;
        foreach (var pair in _selectionTransformStart)
        {
            if (pair.Key == _transformPrimary) continue;
            var parent = pair.Key.Parent == null ? Matrix4x4.Identity : SceneTransforms.GetWorldMatrix(pair.Key.Parent);
            if (Matrix4x4.Invert(parent, out var invParent)) pair.Key.ModelMatrix = pair.Value * parent * deltaWorld * invParent;
        }
    }
    private void EndSelectionTransform(bool commit, string name)
    {
        if (_selectionTransformStart.Count == 0) return;
        if (commit)
        {
            UpdateSelectionTransform();
            // Move the entire selection together onto the platform without changing relative spacing.
            float minimum = float.PositiveInfinity;
            foreach (var node in _selectionTransformStart.Keys)
                if (SceneTransforms.TryComputeWorldAabb(node, out var min, out _)) minimum = Math.Min(minimum, min.Z);
            if (float.IsFinite(minimum) && Math.Abs(minimum) < 1e5 && Math.Abs(minimum) > .01)
                foreach (var node in _selectionTransformStart.Keys) node.ModelMatrix *= Matrix4x4.CreateTranslation(0, 0, -minimum);
            var changes = _selectionTransformStart.Select(pair => (Node: pair.Key, Before: pair.Value, After: pair.Key.ModelMatrix)).Where(x => x.Before != x.After).ToArray();
            if (changes.Length > 0) CommandDispatcher.Push(new SelectionTransformCommand(changes, name));
        }
        else foreach (var pair in _selectionTransformStart) pair.Key.ModelMatrix = pair.Value;
        _selectionTransformStart.Clear(); _transformPrimary = null;
    }
    private sealed class SelectionTransformCommand((SceneNode Node, Matrix4x4 Before, Matrix4x4 After)[] changes, string name) : ICommandAction
    {
        public string Name => name;
        public void Execute() { foreach (var c in changes) c.Node.ModelMatrix = c.After; }
        public void Undo() { foreach (var c in changes) c.Node.ModelMatrix = c.Before; }
    }
}
