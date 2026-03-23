using System;
using System.Globalization;
using System.Windows.Data;

namespace AMLabSlicer.Converters
{
    public class HalfWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double actualWidth)
            {
                double half = actualWidth / 2;
                return half < 320 ? 320 : half;
            }
            return 800.0; 
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}