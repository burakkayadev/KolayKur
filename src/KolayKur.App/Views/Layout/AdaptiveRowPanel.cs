using System.Windows;
using System.Windows.Controls;

namespace KolayKur.App.Views.Layout;

/// <summary>
/// İki çocuklu uyarlanır satır: birinci çocuk (başlık/açıklama ya da sayı metni) solda, ikinci çocuk (denetim ya da
/// düğmeler) sağda tek satırda durur. Birinci çocuğa kalan genişlik <see cref="MinPrimaryWidth"/>'ten azsa ikinci
/// çocuk birincinin ALTINA, sola hizalı geçer; böylece hiçbir şey kırpılmaz ya da "1…" gibi daralmaz.
/// </summary>
public sealed class AdaptiveRowPanel : Panel
{
    public static readonly DependencyProperty MinPrimaryWidthProperty = DependencyProperty.Register(
        nameof(MinPrimaryWidth), typeof(double), typeof(AdaptiveRowPanel),
        new FrameworkPropertyMetadata(220d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveRowPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty StackedSpacingProperty = DependencyProperty.Register(
        nameof(StackedSpacing), typeof(double), typeof(AdaptiveRowPanel),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Tek satırda birinci çocuğa en az ayrılması gereken genişlik.</summary>
    public double MinPrimaryWidth
    {
        get => (double)GetValue(MinPrimaryWidthProperty);
        set => SetValue(MinPrimaryWidthProperty, value);
    }

    /// <summary>Tek satırda iki çocuk arasındaki yatay boşluk.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>İkinci çocuk alta geçince iki çocuk arasındaki dikey boşluk.</summary>
    public double StackedSpacing
    {
        get => (double)GetValue(StackedSpacingProperty);
        set => SetValue(StackedSpacingProperty, value);
    }

    private static double Safe(double value) => double.IsNaN(value) ? 0 : Math.Max(0, value);

    private UIElement? Primary => InternalChildren.Count > 0 ? InternalChildren[0] : null;

    private UIElement? Secondary => InternalChildren.Count > 1 ? InternalChildren[1] : null;

    // Sınırsız genişlikte (ör. kaydırmalı ortam) tek satır varsayılır.
    private bool FitsOnOneRow(double availableWidth, double secondaryWidth) =>
        double.IsInfinity(availableWidth) || availableWidth - secondaryWidth - Safe(Spacing) >= Safe(MinPrimaryWidth);

    protected override Size MeasureOverride(Size availableSize)
    {
        var primary = Primary;
        var secondary = Secondary;
        if (secondary is null)
        {
            primary?.Measure(availableSize);
            return primary?.DesiredSize ?? default;
        }

        secondary.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var secondaryWidth = secondary.DesiredSize.Width;

        if (FitsOnOneRow(availableSize.Width, secondaryWidth))
        {
            var primaryWidth = double.IsInfinity(availableSize.Width)
                ? double.PositiveInfinity
                : availableSize.Width - secondaryWidth - Safe(Spacing);
            primary!.Measure(new Size(primaryWidth, availableSize.Height));
            return new Size(
                primary.DesiredSize.Width + Safe(Spacing) + secondaryWidth,
                Math.Max(primary.DesiredSize.Height, secondary.DesiredSize.Height));
        }

        // Dar: ikinci çocuk (ör. WrapPanel) kullanılabilir genişliğe göre yeniden ölçülür.
        primary!.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        secondary.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(
            Math.Max(primary.DesiredSize.Width, secondary.DesiredSize.Width),
            primary.DesiredSize.Height + Safe(StackedSpacing) + secondary.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var primary = Primary;
        var secondary = Secondary;
        if (secondary is null)
        {
            primary?.Arrange(new Rect(finalSize));
            return finalSize;
        }

        var secondaryWidth = secondary.DesiredSize.Width;
        if (FitsOnOneRow(finalSize.Width, secondaryWidth))
        {
            var rowHeight = finalSize.Height;
            var primaryWidth = Math.Max(0, finalSize.Width - secondaryWidth - Safe(Spacing));
            primary!.Arrange(new Rect(0, 0, primaryWidth, rowHeight));
            secondary.Arrange(new Rect(finalSize.Width - secondaryWidth, 0, secondaryWidth, rowHeight));
            return finalSize;
        }

        var primaryHeight = primary!.DesiredSize.Height;
        primary.Arrange(new Rect(0, 0, finalSize.Width, primaryHeight));
        var top = primaryHeight + Safe(StackedSpacing);
        secondary.Arrange(new Rect(0, top, Math.Min(finalSize.Width, secondary.DesiredSize.Width), secondary.DesiredSize.Height));
        return finalSize;
    }
}
