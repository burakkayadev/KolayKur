using System.Globalization;
using System.Windows.Data;
using KolayKur.Core.Localization;

namespace KolayKur.App;

/// <summary>
/// Metin anahtarını ViewModel'den alan bağlamalar için. İkinci değer <see cref="Loc.Language"/> olmalıdır;
/// dil değişince bağlama yeniden değerlendirilir. Anahtar boşsa boş metin döner.
/// </summary>
public sealed class LocKeyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is string { Length: > 0 } key ? Loc.Instance[key] : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
