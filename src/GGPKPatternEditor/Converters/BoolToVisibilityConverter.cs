using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GGPKPatternEditor.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Check for inverse parameter
            bool inverse = parameter is string paramStr &&
                          paramStr.Equals("Inverse", StringComparison.OrdinalIgnoreCase);

            if (inverse)
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            bool result = visibility == Visibility.Visible;

            // Check for inverse parameter
            bool inverse = parameter is string paramStr &&
                          paramStr.Equals("Inverse", StringComparison.OrdinalIgnoreCase);

            return inverse ? !result : result;
        }
        return false;
    }
}
