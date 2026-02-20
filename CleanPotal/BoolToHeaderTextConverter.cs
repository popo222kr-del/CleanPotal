using System;
using System.Globalization;
using System.Windows.Data;

namespace CleanPotal
{
    public class BoolToHeaderTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool on = value is bool b && b;
            return on ? "관리" : "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
