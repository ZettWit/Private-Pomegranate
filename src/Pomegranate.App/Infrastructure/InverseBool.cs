using System.Globalization;
using System.Windows.Data;

namespace Pomegranate.App.Infrastructure;

/// <summary>Binding converter: true ↔ false.</summary>
public sealed class InverseBool : IValueConverter
{
    public static InverseBool Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
