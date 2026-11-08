using System.Windows;
using System.Windows.Controls;

namespace KolayKur.App.Views.Layout;

/// <summary>
/// Kartları eşit genişlikli sütunlara satır satır dizen panel. Sütun sayısı
/// max(1, ⌊(genişlik + aralık) / (MinColumnWidth + aralık)⌋); satır yüksekliği o satırın en uzun kartıdır.
/// Sınırsız genişlikte (ör. yatay kaydırmalı bir ScrollViewer içinde) tek sütun ve MinColumnWidth kullanır.
/// </summary>
public sealed class CardGridPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(CardGridPanel),
        new FrameworkPropertyMetadata(220d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(CardGridPanel),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private double SafeMinWidth => double.IsNaN(MinColumnWidth) ? 0 : Math.Max(0, MinColumnWidth);

    private double SafeSpacing => double.IsNaN(Spacing) ? 0 : Math.Max(0, Spacing);

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = Layout(availableSize.Width);
        var spacing = SafeSpacing;
        var rowHeights = new List<double>();
        var column = 0;
        var rowHeight = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++column == columns)
            {
                rowHeights.Add(rowHeight);
                column = 0;
                rowHeight = 0;
            }
        }

        if (column > 0)
        {
            rowHeights.Add(rowHeight);
        }

        var height = rowHeights.Sum() + (Math.Max(0, rowHeights.Count - 1) * spacing);
        var width = double.IsInfinity(availableSize.Width)
            ? (columns * itemWidth) + ((columns - 1) * spacing)
            : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width);
        var spacing = SafeSpacing;
        var children = InternalChildren;
        var top = 0d;
        for (var rowStart = 0; rowStart < children.Count; rowStart += columns)
        {
            var rowEnd = Math.Min(rowStart + columns, children.Count);
            var rowHeight = 0d;
            for (var i = rowStart; i < rowEnd; i++)
            {
                rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            }

            for (var i = rowStart; i < rowEnd; i++)
            {
                var left = (i - rowStart) * (itemWidth + spacing);
                children[i].Arrange(new Rect(left, top, itemWidth, rowHeight));
            }

            top += rowHeight + spacing;
        }

        return finalSize;
    }

    private (int Columns, double ItemWidth) Layout(double width)
    {
        var minWidth = SafeMinWidth;
        var spacing = SafeSpacing;
        if (double.IsInfinity(width) || double.IsNaN(width))
        {
            return (1, minWidth);
        }

        var columns = (int)Math.Max(1, Math.Floor((width + spacing) / (minWidth + spacing)));
        var itemWidth = Math.Max(0, (width - ((columns - 1) * spacing)) / columns);
        return (columns, itemWidth);
    }
}
