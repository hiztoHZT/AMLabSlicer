using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AMLabSlicer.Views
{
    public partial class ParameterPanelView : UserControl
    {
        public ParameterPanelView()
        {
            InitializeComponent();
        }
    }

    /// <summary>
    /// 根据 SliceParameter 的类型自动选择对应的 DataTemplate
    /// </summary>
    public class ParameterTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? BooleanTemplate { get; set; }
        public DataTemplate? NumberTemplate  { get; set; }
        public DataTemplate? SliderTemplate  { get; set; }
        public DataTemplate? EnumTemplate    { get; set; }
        public DataTemplate? DefaultTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is AMLabSlicer.Core.Parameters.SliceParameter param)
            {
                // 布尔
                if (param.ParameterType == typeof(bool))
                    return BooleanTemplate;

                // 有 Min/Max → 滑块
                if (param.MinValue.HasValue && param.MaxValue.HasValue)
                    return SliderTemplate;

                // 数值
                if (param.ParameterType == typeof(int) ||
                    param.ParameterType == typeof(double) ||
                    param.ParameterType == typeof(float))
                    return NumberTemplate;

                // 枚举 / 下拉
                if (param.ParameterType.IsEnum || param.Options != null)
                    return EnumTemplate;

                return DefaultTemplate ?? base.SelectTemplate(item, container);
            }
            return base.SelectTemplate(item, container);
        }
    }

    /// <summary>
    /// 当值为 null 或空字符串时折叠元素
    /// </summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public static readonly NullToCollapsedConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null || (value is string s && string.IsNullOrEmpty(s))
                ? Visibility.Collapsed
                : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
