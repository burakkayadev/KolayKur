using System.Globalization;
using System.Windows.Data;

namespace KolayKur.App.Converters;

/// <summary>
/// Sol menü RadioButton'larını bir enum özelliğine bağlar. ConverterParameter enum değeridir (ya da adı).
/// Convert: değer parametreye eşitse true. ConvertBack: true → parametre; false → <see cref="Binding.DoNothing"/>
/// (başka bir düğme seçilince eski düğmenin işaretinin kalkması kaynağı değiştirmesin).
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && Matches(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (parameter is string name && enumType.IsEnum)
        {
            return Enum.TryParse(enumType, name, ignoreCase: true, out var parsed) ? parsed : Binding.DoNothing;
        }

        return parameter;
    }

    private static bool Matches(object value, object parameter) =>
        parameter is string name && value.GetType().IsEnum
            ? string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase)
            : value.Equals(parameter);
}
