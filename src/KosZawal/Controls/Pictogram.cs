using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KosZawal.Controls;

public class Pictogram : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<Pictogram, string>(nameof(Kind), "heart");

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Pictogram, IBrush?>(nameof(Foreground));

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static Pictogram()
    {
        AffectsRender<Pictogram>(KindProperty, ForegroundProperty);
    }

    public override void Render(DrawingContext context)
    {
        var color = Foreground is ISolidColorBrush brush ? brush.Color : Color.Parse("#16324F");
        var pen = new Pen(new SolidColorBrush(color), Math.Max(1.4, Bounds.Width / 14), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var fill = new SolidColorBrush(color);
        var s = Math.Min(Bounds.Width, Bounds.Height);
        if (s <= 0)
            return;

        var origin = new Point((Bounds.Width - s) / 2, (Bounds.Height - s) / 2);
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y) * Matrix.CreateScale(s / 24, s / 24)))
        {
            switch (Kind)
            {
                case "hospital":
                    DrawHospital(context, pen, fill);
                    break;
                case "contract":
                    DrawContract(context, pen, fill);
                    break;
                case "trend":
                    DrawTrend(context, pen);
                    break;
                case "map":
                    DrawPin(context, pen, fill);
                    break;
                case "regions":
                    DrawRegions(context, pen, fill);
                    break;
                case "book":
                    DrawBook(context, pen);
                    break;
                case "export":
                    DrawExport(context, pen);
                    break;
                case "refresh":
                    DrawRefresh(context, pen);
                    break;
                default:
                    DrawHeart(context, fill);
                    break;
            }
        }
    }

    private static void DrawHeart(DrawingContext context, IBrush fill)
    {
        var geometry = Geometry.Parse("M12,20.2 C7.2,16.4 3.6,13.4 3.6,9.4 C3.6,6.7 5.6,4.8 8.1,4.8 C9.6,4.8 10.9,5.5 12,6.8 C13.1,5.5 14.4,4.8 15.9,4.8 C18.4,4.8 20.4,6.7 20.4,9.4 C20.4,13.4 16.8,16.4 12,20.2 Z");
        context.DrawGeometry(fill, null, geometry);
    }

    private static void DrawHospital(DrawingContext context, Pen pen, IBrush fill)
    {
        context.DrawRectangle(null, pen, new RoundedRect(new Rect(3.5, 8, 17, 12.5), 1.5));
        context.DrawLine(pen, new Point(3.5, 12), new Point(20.5, 12));
        context.DrawRectangle(fill, null, new Rect(10.6, 3.2, 2.8, 6.2));
        context.DrawRectangle(fill, null, new Rect(8.4, 4.6, 7.2, 2.6));
        context.DrawRectangle(null, pen, new Rect(10.2, 14.2, 3.6, 6.3));
    }

    private static void DrawContract(DrawingContext context, Pen pen, IBrush fill)
    {
        var page = Geometry.Parse("M6,3.5 H14.2 L19,8.3 V20.5 H6 Z");
        context.DrawGeometry(null, pen, page);
        context.DrawLine(pen, new Point(14, 3.8), new Point(14, 8.4));
        context.DrawLine(pen, new Point(14, 8.4), new Point(18.6, 8.4));
        var check = new PolylineGeometry(new[] { new Point(8.2, 14.2), new Point(10.6, 16.6), new Point(15.4, 11.6) }, false);
        context.DrawGeometry(null, pen, check);
        context.DrawEllipse(fill, null, new Point(17.6, 17.8), 2.1, 2.1);
    }

    private static void DrawTrend(DrawingContext context, Pen pen)
    {
        var line = new PolylineGeometry(new[]
        {
            new Point(3.2, 17.5), new Point(8, 14.2), new Point(11.4, 15.6), new Point(15.2, 9.4), new Point(20.6, 6.2),
        }, false);
        context.DrawGeometry(null, pen, line);
        context.DrawLine(pen, new Point(16.4, 6.2), new Point(20.6, 6.2));
        context.DrawLine(pen, new Point(20.6, 6.2), new Point(20.6, 10.2));
    }

    private static void DrawPin(DrawingContext context, Pen pen, IBrush fill)
    {
        var pin = Geometry.Parse("M12,21.2 C12,21.2 5.2,14.4 5.2,9.6 C5.2,6 8.2,3.4 12,3.4 C15.8,3.4 18.8,6 18.8,9.6 C18.8,14.4 12,21.2 12,21.2 Z");
        context.DrawGeometry(null, pen, pin);
        context.DrawEllipse(fill, null, new Point(12, 9.5), 2.1, 2.1);
    }

    private static void DrawRegions(DrawingContext context, Pen pen, IBrush fill)
    {
        context.DrawRectangle(fill, pen, new RoundedRect(new Rect(3.2, 3.2, 7.4, 7.4), 1.4));
        context.DrawRectangle(null, pen, new RoundedRect(new Rect(13.2, 3.2, 7.4, 7.4), 1.4));
        context.DrawRectangle(null, pen, new RoundedRect(new Rect(3.2, 13.2, 7.4, 7.4), 1.4));
        context.DrawRectangle(fill, pen, new RoundedRect(new Rect(13.2, 13.2, 7.4, 7.4), 1.4));
    }

    private static void DrawBook(DrawingContext context, Pen pen)
    {
        var left = Geometry.Parse("M12,6.2 C10.2,5 7.4,4.4 4,4.6 V18.4 C7.4,18.1 10.2,18.6 12,19.6 Z");
        var right = Geometry.Parse("M12,6.2 C13.8,5 16.6,4.4 20,4.6 V18.4 C16.6,18.1 13.8,18.6 12,19.6 Z");
        context.DrawGeometry(null, pen, left);
        context.DrawGeometry(null, pen, right);
    }

    private static void DrawExport(DrawingContext context, Pen pen)
    {
        context.DrawLine(pen, new Point(12, 3.4), new Point(12, 14.2));
        var head = new PolylineGeometry(new[] { new Point(7.6, 8.2), new Point(12, 3.4), new Point(16.4, 8.2) }, false);
        context.DrawGeometry(null, pen, head);
        var tray = Geometry.Parse("M5,12.5 V19.2 H19 V12.5");
        context.DrawGeometry(null, pen, tray);
    }

    private static void DrawRefresh(DrawingContext context, Pen pen)
    {
        var arc = new ArcSegment
        {
            Point = new Point(17.2, 8.2),
            Size = new Size(6.2, 6.2),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = true,
        };
        var figure = new PathFigure { StartPoint = new Point(16.2, 5.2), IsClosed = false };
        figure.Segments?.Add(arc);
        var geometry = new PathGeometry();
        geometry.Figures?.Add(figure);
        context.DrawGeometry(null, pen, geometry);
        var head = new PolylineGeometry(new[] { new Point(13.4, 4.6), new Point(16.6, 5.2), new Point(15.6, 8.2) }, false);
        context.DrawGeometry(null, pen, head);
        context.DrawEllipse(null, pen, new Point(12, 13.6), 4.6, 4.6);
    }
}
