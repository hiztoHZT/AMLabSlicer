using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace AMLabSlicer.Views
{
    public partial class ParameterPanelView : UserControl
    {
        public ParameterPanelView()
        {
            InitializeComponent();
        }

        private void ScrollCategoriesLeft_Click(object sender, RoutedEventArgs e)
        {
            CategoryScrollViewer.ScrollToHorizontalOffset(Math.Max(0, CategoryScrollViewer.HorizontalOffset - 96));
        }

        private void ScrollCategoriesRight_Click(object sender, RoutedEventArgs e)
        {
            CategoryScrollViewer.ScrollToHorizontalOffset(CategoryScrollViewer.HorizontalOffset + 96);
        }

        private void CategoryScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            CategoryScrollViewer.ScrollToHorizontalOffset(CategoryScrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
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
