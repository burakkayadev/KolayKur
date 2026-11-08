using System.Globalization;
using System.Windows.Data;

namespace KolayKur.App.Converters;

/// <summary>
/// İlk değer biçim metni (ör. yerelleştirilmiş "Seçilenleri kur ({0})"), kalanlar biçim bağımsız değişkenleridir.
/// Biçim metni <c>Loc</c> indeksleyicisinden bağlandığı için dil değişince metin yenilenir.
/// </summary>
public sealed class FormatTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is string format
            ? string.Format(culture, format, values.Skip(1).ToArray())
            : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
