using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AxiomFusion.CncController.Converters;

/// <summary>Converte string hexadecimal de cor (#rrggbb) em SolidColorBrush.</summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
