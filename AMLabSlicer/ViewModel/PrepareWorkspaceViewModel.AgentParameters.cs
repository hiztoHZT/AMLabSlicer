using AMLabSlicer.Plugin.Abstractions;

namespace AMLabSlicer.ViewModel;

public partial class PrepareWorkspaceViewModel
{
    private static bool AgentCanEdit(string key) => key is "atom.layer_height_ratio" or "layer_height";

    internal SliceParameterSnapshot? ReadAgentSliceParameters()
    {
        if (_parametersLoading || SelectedAlgorithm?.AlgorithmId != _activeParameterAlgorithmId ||
            string.IsNullOrEmpty(_parameterContext)) return null;

        return new(_parameterContext, SelectedAlgorithm.AlgorithmId, Parameters
            .Where(p => p.ControlType != AMLabSlicer.Core.Parameters.UIControlType.TextBox)
            .Select(p =>
            new SliceParameterEntry(p.Key, p.DisplayName, p.Value, p.Unit, p.Description,
                p.MinValue, p.MaxValue, AgentCanEdit(p.Key))).ToArray());
    }

    internal void ApplyAgentSliceParameters(SliceParameterSnapshot snapshot, IReadOnlyList<SliceParameterChange> changes)
    {
        if (_parametersLoading || _parameterContext != snapshot.Context ||
            SelectedAlgorithm?.AlgorithmId != snapshot.Algorithm)
            throw new InvalidOperationException("当前参数配置已切换，请重新生成修改建议。");

        var pending = changes.Select(change =>
        {
            var current = Parameters.SingleOrDefault(p => p.Key == change.Key);
            var original = snapshot.Parameters.SingleOrDefault(p => p.Key == change.Key);
            if (current == null || original == null || !AgentCanEdit(change.Key) ||
                !double.IsFinite(change.Value) || change.Value < current.MinValue || change.Value > current.MaxValue)
                throw new InvalidOperationException("建议包含不允许修改或超出范围的参数。");
            if (!Equals(current.Value, original.Value))
                throw new InvalidOperationException("参数值已经改变，请重新生成修改建议。");
            return (Parameter: current, change.Value);
        }).ToArray();

        foreach (var change in pending) change.Parameter.Value = change.Value;
    }
}
