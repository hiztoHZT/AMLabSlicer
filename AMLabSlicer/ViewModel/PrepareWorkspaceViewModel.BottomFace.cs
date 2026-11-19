using AMLabSlicer.Commands;
using AMLabSlicer.Core.Commands;
using AMLabSlicer.Services;
using CommunityToolkit.Mvvm.Input;
using System.Numerics;

namespace AMLabSlicer.ViewModel;

public partial class PrepareWorkspaceViewModel
{
    [RelayCommand]
    private async Task AutoLayFlatAsync()
    {
        if (!CanTransformSelection) return;
        var selected = SelectedItems.ToArray();
        var snapshots = selected.Select(item => BottomFacePlacement.Capture(item.Node!)).ToArray();
        var result = await Task.Run(() =>
        {
            var commands = new List<ICommandAction>();
            var errors = new List<string>();
            foreach (var snapshot in snapshots)
            {
                try { commands.Add(BottomFacePlacement.CreateCommand(snapshot)); }
                catch (InvalidOperationException ex) { errors.Add($"{snapshot.Node.Name}：{ex.Message}"); }
            }
            return (commands, errors);
        });
        if (!CanTransformSelection || !SelectedItems.SequenceEqual(selected) || snapshots.Any(s =>
            s.Node.ModelMatrix != s.Before || (s.Node.Parent == null ? Matrix4x4.Identity : SceneTransforms.GetWorldMatrix(s.Node.Parent)) != s.ParentWorld))
        {
            _dialogs.ShowMessage("计算期间选择或模型姿态已改变，本次底面操作未执行。", "底面");
            return;
        }
        if (result.commands.Count > 0) History.ExecuteCommand(new BatchCommand(result.commands, "自动底面"));
        if (result.errors.Count > 0) _dialogs.ShowMessage(string.Join("\n", result.errors), "部分模型未调整底面");
    }
}
