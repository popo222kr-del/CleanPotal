using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace CleanPotal
{
    public class BoolToDataGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool on = value is bool b && b;
            return on ? new DataGridLength(153) : new DataGridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
