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
    /// 当值为 null 或空字符串时折叠元素，用于隐藏不存在的单位
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
