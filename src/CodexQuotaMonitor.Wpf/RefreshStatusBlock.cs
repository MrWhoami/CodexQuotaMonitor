using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace CodexQuotaMonitor.Wpf;

public sealed class RefreshStatusBlock : FrameworkElement
{
    private readonly Brush _background;
    private readonly Pen _borderPen;
    private DateTimeOffset? _refreshedAt;
    private string _status = "WAIT";
    private System.Windows.Media.Color _accentColor = Formatting.ColorFromHex("#91A0B5");

    public RefreshStatusBlock(string background)
    {
        _background = new SolidColorBrush(Formatting.ColorFromHex(background));
        _borderPen = new Pen(new SolidColorBrush(Formatting.ColorFromHex("#273449")), 1);
        SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void SetStatus(DateTimeOffset? refreshedAt, string status, string accentHex)
    {
        _refreshedAt = refreshedAt;
        _status = status;
        _accentColor = Formatting.ColorFromHex(accentHex);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = Math.Max(50, ActualWidth);
        var height = Math.Max(34, ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRectangle(_background, _borderPen, new Rect(0.5, 0.5, width - 1, height - 1));

        var accent = new SolidColorBrush(_accentColor);
        var soft = new SolidColorBrush(Formatting.ColorFromHex("#D8E3F0"));

        DrawText(dc, "LAST REF", 6, 3.5, 7, FontWeights.Bold, soft, dpi);
        DrawCenteredText(
            dc,
            FormatTime(_refreshedAt),
            new Rect(0, 14.5, width, 14.0),
            9.4,
            FontWeights.SemiBold,
            accent,
            dpi);

        if (!string.IsNullOrWhiteSpace(_status))
        {
            DrawCenteredText(
                dc,
                _status,
                new Rect(0, height - 14.0, width, 10.0),
                7,
                FontWeights.Bold,
                accent,
                dpi);
        }
    }

    private static string FormatTime(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("HH:mm", CultureInfo.CurrentCulture) : "--:--";
    }

    private static void DrawText(
        DrawingContext dc,
        string text,
        double x,
        double y,
        double size,
        FontWeight weight,
        Brush brush,
        double pixelsPerDip)
    {
        dc.DrawText(MakeText(text, size, weight, brush, pixelsPerDip), new Point(x, y));
    }

    private static void DrawCenteredText(
        DrawingContext dc,
        string text,
        Rect bounds,
        double size,
        FontWeight weight,
        Brush brush,
        double pixelsPerDip)
    {
        var formatted = MakeText(text, size, weight, brush, pixelsPerDip);
        var textGeometry = formatted.BuildGeometry(new Point(0, 0));
        var textBounds = textGeometry.Bounds;
        if (textBounds.IsEmpty)
        {
            dc.DrawText(formatted, new Point(bounds.Left, bounds.Top));
            return;
        }

        var x = bounds.Left + (bounds.Width - textBounds.Width) / 2.0 - textBounds.Left;
        var y = bounds.Top + (bounds.Height - textBounds.Height) / 2.0 - textBounds.Top;
        dc.DrawText(formatted, new Point(x, y));
    }

    private static FormattedText MakeText(
        string text,
        double size,
        FontWeight weight,
        Brush brush,
        double pixelsPerDip)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip);
    }
}
