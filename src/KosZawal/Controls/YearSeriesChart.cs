using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KosZawal.Controls;

public class YearSeriesChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<HospitalYearSeries>?> YearsProperty =
        AvaloniaProperty.Register<YearSeriesChart, IReadOnlyList<HospitalYearSeries>?>(nameof(Years));

    public IReadOnlyList<HospitalYearSeries>? Years
    {
        get => GetValue(YearsProperty);
        set => SetValue(YearsProperty, value);
    }

    static YearSeriesChart()
    {
        AffectsRender<YearSeriesChart>(YearsProperty);
    }

    public override void Render(DrawingContext context)
    {
        var years = Years ?? [];
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (years.Count == 0 || Bounds.Width < 40 || Bounds.Height < 40)
            return;

        var culture = CultureInfo.GetCultureInfo("pl-PL");
        var left = 42.0;
        var right = 12.0;
        var top = 18.0;
        var bottom = 28.0;
        var plot = new Rect(left, top, Math.Max(10, Bounds.Width - left - right), Math.Max(10, Bounds.Height - top - bottom));
        var max = Math.Max(1, years.Max(item => item.Hospitalizations) * 1.15);
        var grid = new Pen(new SolidColorBrush(Color.Parse("#E7E1D6")), 1);
        var barBrush = new SolidColorBrush(Color.Parse("#0E6E6A"));
        var kosBrush = new SolidColorBrush(Color.Parse("#C4474F"));

        for (var tick = 0; tick <= 4; tick++)
        {
            var value = max * tick / 4;
            var y = plot.Bottom - plot.Height * (value / max);
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = new FormattedText(
                value.ToString("N0", culture),
                culture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                10,
                new SolidColorBrush(Color.Parse("#5C6B78")));
            context.DrawText(label, new Point(2, y - 6));
        }

        var slot = plot.Width / years.Count;
        var width = Math.Max(8, slot * 0.55);
        for (var i = 0; i < years.Count; i++)
        {
            var item = years[i];
            var x = plot.Left + slot * i + (slot - width) / 2;
            var height = plot.Height * (item.Hospitalizations / max);
            var rect = new Rect(x, plot.Bottom - height, width, height);
            context.FillRectangle(item.IsKos ? kosBrush : barBrush, rect);

            var yearLabel = new FormattedText(
                item.Year.ToString(culture),
                culture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                11,
                new SolidColorBrush(Color.Parse("#5C6B78")));
            context.DrawText(yearLabel, new Point(x + width / 2 - 12, plot.Bottom + 6));
        }
    }
}
