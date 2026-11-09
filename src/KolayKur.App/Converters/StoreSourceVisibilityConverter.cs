using System.Globalization;
using System.Windows;
using System.Windows.Data;
using KolayKur.Core.Models;

namespace KolayKur.App.Converters;

/// <summary>Paketin kaynağı Microsoft Store ise (büyük/küçük harf ayrımsız) görünür, değilse gizlidir.</summary>
public sealed class StoreSourceVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string source && string.Equals(source, PackageSources.MicrosoftStore, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
