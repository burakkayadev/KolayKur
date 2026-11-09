using System.Globalization;
using System.Windows.Data;

namespace KolayKur.App.Converters;

/// <summary>Değer <c>null</c> değilse <c>true</c> (örn. kurulu sürüm var mı).</summary>
public sealed class NotNullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
