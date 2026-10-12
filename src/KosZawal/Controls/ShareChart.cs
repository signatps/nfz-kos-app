using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KosZawal.Controls;

public class ShareChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<MonthPoint>?> PointsProperty =
        AvaloniaProperty.Register<ShareChart, IReadOnlyList<MonthPoint>?>(nameof(Points));

    public IReadOnlyList<MonthPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    static ShareChart()
    {
        AffectsRender<ShareChart>(PointsProperty);
    }

    public override void Render(DrawingContext context)
    {
        var points = Points ?? [];
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (points.Count == 0 || Bounds.Width < 40)
            return;

        var left = 36.0;
        var right = 12.0;
        var top = 16.0;
        var bottom = 28.0;
        var plot = new Rect(left, top, Math.Max(10, Bounds.Width - left - right), Math.Max(10, Bounds.Height - top - bottom));
        var max = Math.Max(0.5, points.Max(point => point.Share) * 1.15);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#C4474F")), 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var grid = new Pen(new SolidColorBrush(Color.Parse("#E7E1D6")), 1);
        var culture = CultureInfo.GetCultureInfo("pl-PL");

        for (var tick = 0; tick <= 4; tick++)
        {
            var value = max * tick / 4;
            var y = plot.Bottom - plot.Height * (value / max);
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = new FormattedText(
                value.ToString("0%", culture),
                culture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                11,
                new SolidColorBrush(Color.Parse("#5C6B78")));
            context.DrawText(label, new Point(0, y - 7));
        }

        Point At(int index)
        {
            var x = plot.Left + (points.Count == 1 ? plot.Width / 2 : plot.Width * index / (points.Count - 1));
            var y = plot.Bottom - plot.Height * (points[index].Share / max);
            return new Point(x, y);
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(0), false);
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(At(i));
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
        for (var i = 0; i < points.Count; i++)
        {
            var at = At(i);
            context.DrawEllipse(Brushes.White, pen, at, 3.2, 3.2);
            if (i % Math.Max(1, points.Count / 6) == 0 || i == points.Count - 1)
            {
                var month = points[i].Month.Length >= 7 ? points[i].Month[5..] : points[i].Month;
                var label = new FormattedText(
                    month,
                    culture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    11,
                    new SolidColorBrush(Color.Parse("#5C6B78")));
                context.DrawText(label, new Point(at.X - 8, plot.Bottom + 6));
            }
        }
    }
}
