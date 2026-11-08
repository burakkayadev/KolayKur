using System.Globalization;
using System.Windows.Data;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;

namespace KolayKur.App;

/// <summary>
/// İşlem listesi metinleri için. <c>ConverterParameter</c>: <c>Status</c> (değerler: durum, dil),
/// <c>Kind</c> (tür, dil) veya <c>Message</c> (durum, ileti, dil). Dil değerleri dil değişince yeniden çevirmeyi sağlar.
/// </summary>
public sealed class OperationTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        (parameter as string, values) switch
        {
            ("Status", [OperationStatus status, ..]) => Loc.Instance[OperationTexts.StatusKey(status)],
            ("Kind", [OperationKind kind, ..]) => Loc.Instance[OperationTexts.KindKey(kind)],
            ("Message", [OperationStatus status, var message, ..]) =>
                OperationTexts.MessageText(status, message as string),
            _ => string.Empty,
        };

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
