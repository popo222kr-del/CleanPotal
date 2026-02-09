using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace CleanPortal
{
    public class GroupItemsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var groups = value as IEnumerable<GroupVm>;
            if (groups == null) return new List<ItemVm>();

            string groupName = parameter?.ToString() ?? "";
            var g = groups.FirstOrDefault(x =>
                string.Equals(x.Group, groupName, StringComparison.OrdinalIgnoreCase));

            // List<ItemVm>로 통일
            return g?.Items ?? new List<ItemVm>();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
