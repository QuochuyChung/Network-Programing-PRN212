using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ChatClient.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; } = false;

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        if (value is string s) isNull = string.IsNullOrEmpty(s);

        if (Invert) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
