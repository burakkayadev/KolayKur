using System.Globalization;
using System.Windows.Data;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;

namespace KolayKur.App;

/// <summary>
/// İşlem listesi metinleri için. <c>ConverterParameter</c>: <c>Status</c> (değerler: durum, dil) veya
/// <c>Kind</c> (tür, dil). Dil değerleri dil değişince yeniden çevirmeyi sağlar. Ayrıntı/neden metni
/// <c>OperationItem.DetailText</c>'ten gelir (kendi dil bildirimini yapar).
/// </summary>
public sealed class OperationTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        (parameter as string, values) switch
        {
            ("Status", [OperationStatus status, ..]) => Loc.Instance[OperationTexts.StatusKey(status)],
            ("Kind", [OperationKind kind, ..]) => Loc.Instance[OperationTexts.KindKey(kind)],
            _ => string.Empty,
        };

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
