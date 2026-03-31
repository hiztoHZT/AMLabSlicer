using System.Windows;
using System.Windows.Controls;
using AMLabSlicer.Core.Parameters;

namespace AMLabSlicer.Selectors
{
    /// <summary>
    /// Phase 2: 开发 UI 层的模板分发器
    /// 根据传入 SliceParameter.ControlType 分发 DataTemplate
    /// </summary>
    public class ParameterTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? TplCheckBox { get; set; }
        public DataTemplate? TplNumericBox { get; set; }
        public DataTemplate? TplSlider { get; set; }
        public DataTemplate? TplComboBox { get; set; }
        public DataTemplate? TplTextBox { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is SliceParameter param)
            {
                return param.ControlType switch
                {
                    UIControlType.CheckBox => TplCheckBox,
                    UIControlType.NumericBox => TplNumericBox,
                    UIControlType.Slider => TplSlider,
                    UIControlType.ComboBox => TplComboBox,
                    UIControlType.TextBox => TplTextBox,
                    _ => base.SelectTemplate(item, container)
                };
            }
            return base.SelectTemplate(item, container);
        }
    }
}
