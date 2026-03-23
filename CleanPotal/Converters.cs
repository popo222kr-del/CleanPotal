using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CleanPotal
{
    // 진행률 % -> 색상
    public sealed class PercentToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int p = 0;

            if (value is int i)
                p = i;
            else if (value != null && int.TryParse(value.ToString(), out int parsed))
                p = parsed;

            if (p <= 29) return Brushes.Red;
            if (p <= 59) return Brushes.Orange;
            if (p <= 89) return Brushes.Gold;
            return Brushes.Green;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    // ProgressBar 템플릿: Value/Maximum/ActualWidth -> 채움 폭
    public sealed class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length != 3)
                return 0.0;

            double v = System.Convert.ToDouble(values[0]);
            double max = System.Convert.ToDouble(values[1]);
            double w = System.Convert.ToDouble(values[2]);

            if (max <= 0 || w <= 0)
                return 0.0;

            double ratio = v / max;

            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            double width = (w - 2) * ratio;
            return width < 0 ? 0 : width;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => Array.Empty<object>();
    }
    public sealed class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value?.ToString()?.Trim() ?? "";

            return status switch
            {
                "진행" => Brushes.DodgerBlue,
                "보류" => Brushes.Red,
                "완료" => Brushes.Green,
                _ => Brushes.Black
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    // bool -> Visibility
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool bb && bb;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}