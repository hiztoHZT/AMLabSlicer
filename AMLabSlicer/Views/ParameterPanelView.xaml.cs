using System;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AMLabSlicer.Core.Parameters;

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

        private void BoundedNumericTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CoerceBoundedNumericTextBox(sender as TextBox);
        }

        private void BoundedNumericTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            CoerceBoundedNumericTextBox(sender as TextBox);
            e.Handled = true;
        }

        private static void CoerceBoundedNumericTextBox(TextBox? textBox)
        {
            if (textBox?.DataContext is not SliceParameter parameter)
            {
                return;
            }

            if (parameter.ControlType != UIControlType.NumericBox &&
                parameter.ControlType != UIControlType.Slider)
            {
                return;
            }

            if (parameter.MinValue == null && parameter.MaxValue == null)
            {
                return;
            }

            if (!TryParseDouble(textBox.Text, out var value))
            {
                return;
            }

            var clamped = value;
            var warning = string.Empty;

            if (parameter.MinValue is double minValue && value < minValue)
            {
                clamped = minValue;
                warning = $"参数“{parameter.DisplayName}”不能小于 {FormatNumber(minValue)}，已调整为 {FormatNumber(clamped)}。";
            }

            if (parameter.MaxValue is double maxValue && value > maxValue)
            {
                clamped = maxValue;
                warning = $"参数“{parameter.DisplayName}”不能大于 {FormatNumber(maxValue)}，已调整为 {FormatNumber(clamped)}。";
            }

            parameter.Value = clamped;
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

            if (!string.IsNullOrEmpty(warning))
            {
                MessageBox.Show(Window.GetWindow(textBox), warning, "参数超出范围", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static bool TryParseDouble(string? text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G", CultureInfo.CurrentCulture);
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

    public class MinWidthToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var width = value is double actualWidth ? actualWidth : 0;
            var thresholdText = parameter?.ToString() ?? "0";
            var threshold = double.TryParse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            return width >= threshold ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class AlgorithmDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            var displayName = ReadStringProperty(value, "DisplayName");
            var algorithmId = ReadStringProperty(value, "AlgorithmId");
            var normalized = NormalizeLabel(displayName, algorithmId);

            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;

            return value.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string? ReadStringProperty(object value, string propertyName)
            => value.GetType().GetProperty(propertyName)?.GetValue(value)?.ToString();

        private static string NormalizeLabel(string? label, string? fallback)
        {
            var text = label?.Trim();
            var fallbackText = fallback?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(text))
                return fallbackText;

            if (!text.StartsWith("{", StringComparison.Ordinal))
                return text;

            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return fallbackText;

                foreach (var key in new[] { "displayName", "display_name", "name", "algorithmId", "algorithm_id" })
                {
                    if (document.RootElement.TryGetProperty(key, out var property) &&
                        property.ValueKind == JsonValueKind.String)
                    {
                        var value = property.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(value))
                            return value;
                    }
                }
            }
            catch (JsonException)
            {
                return fallbackText;
            }

            return fallbackText;
        }
    }
}
