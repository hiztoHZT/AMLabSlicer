using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace AMLabSlicer.Core.Parameters
{
    /// <summary>
    /// 定义该参数在 UI 层应该被渲染成何种控件
    /// </summary>
    public enum UIControlType
    {
        CheckBox,
        NumericBox,
        Slider,
        ComboBox,
        TextBox
    }

    /// <summary>
    /// 通用切片参数描述对象。
    /// 包含 UI 渲染元数据的强类型参数模型
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

        /// <summary>指定的 UI 控件类型</summary>
        public UIControlType ControlType { get; set; } = UIControlType.TextBox;

        /// <summary>单位文字，如 "mm" / "%" / "°C"</summary>
        public string? Unit { get; set; }

        /// <summary>参数描述 / Tooltip 悬停提示</summary>
        public string? Description { get; set; }

        /// <summary>参数子分类 (例如：层高设置、顶底层数)</summary>
        public string Subcategory { get; set; } = string.Empty;

        /// <summary>同分类下的排序权重</summary>
        public int Order { get; set; }

        /// <summary>数值步进值 (例如 0.05)</summary>
        public double? Step { get; set; }

        /// <summary>数值下限（用于 Slider 或 NumericBox 限制）</summary>
        public double? MinValue { get; set; }

        /// <summary>数值上限（用于 Slider 或 NumericBox 限制）</summary>
        public double? MaxValue { get; set; }

        /// <summary>是否为高级参数 (默认隐藏)</summary>
        [ObservableProperty]
        private bool _isAdvanced;

        /// <summary>可见性条件表达式 (如 "support_enable==true")</summary>
        public string? VisibleIf { get; set; }

        /// <summary>启用条件表达式</summary>
        public string? EnabledIf { get; set; }

        /// <summary>针对下拉框 (ComboBox) 的可选项</summary>
        public List<string>? Options { get; set; }
    }
}
