using CommunityToolkit.Mvvm.ComponentModel;

namespace AMLabSlicer.Core.Parameters
{
    /// <summary>
    /// 通用切片参数描述对象。
    /// 每个切片算法通过 IParameterStore 注册一系列 SliceParameter，
    /// UI 通过 ParameterTemplateSelector 自动匹配对应的控件模板渲染。
    /// </summary>
    public partial class SliceParameter : ObservableObject
    {
        public string Key { get; set; } = string.Empty;

        [ObservableProperty]
        private string _displayName = string.Empty;

        public string Category { get; set; } = string.Empty;

        public Type ParameterType { get; set; } = typeof(object);

        [ObservableProperty]
        private object? _value;

        /// <summary>单位文字，如 "mm" / "%" / "°C"</summary>
        public string? Unit { get; set; }

        /// <summary>参数描述 / Tooltip</summary>
        public string? Description { get; set; }

        /// <summary>数值下限（用于 Slider）</summary>
        public double? MinValue { get; set; }

        /// <summary>数值上限（用于 Slider）</summary>
        public double? MaxValue { get; set; }

        /// <summary>对于枚举或下拉选项列表</summary>
        public object[]? Options { get; set; }
    }
}
