using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GameMusicShare.Audio;

namespace GameMusicShare.Controls;

public sealed class SpectrumControl : FrameworkElement
{
    private const int VisualBarCount = 64;
    private readonly float[] shown = new float[VisualBarCount];

    public Color StartColor { get; set; } = Color.FromRgb(34, 211, 238);
    public Color EndColor { get; set; } = Color.FromRgb(52, 211, 153);

    public void Update(SpectrumSnapshot snapshot)
    {
        if (snapshot.Bars.Length == 0) return;
        for (var i = 0; i < shown.Length; i++)
        {
            var sourcePosition = i * (snapshot.Bars.Length - 1f) / (shown.Length - 1f);
            var left = Math.Min(snapshot.Bars.Length - 1, (int)sourcePosition);
            var right = Math.Min(snapshot.Bars.Length - 1, left + 1);
            var fraction = sourcePosition - left;
            var target = snapshot.Bars[left] + (snapshot.Bars[right] - snapshot.Bars[left]) * fraction;
            shown[i] += (target - shown[i]) * (target > shown[i] ? 0.72f : 0.26f);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var plotWidth = Math.Max(0, ActualWidth - 32);
        var plotHeight = Math.Max(0, ActualHeight - 24);
        var plot = new Rect(0, 0, plotWidth, plotHeight);

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5, 20, 37)), new Pen(new SolidColorBrush(Color.FromRgb(25, 56, 82)), 0.8), plot);

        var horizontalPen = new Pen(new SolidColorBrush(Color.FromArgb(42, 78, 129, 167)), 0.6);
        for (var line = 1; line < 4; line++)
        {
            var y = Math.Round(plotHeight * line / 4d) + 0.5;
            dc.DrawLine(horizontalPen, new Point(0, y), new Point(plotWidth, y));
        }

        var frequencies = new[] { 20d, 50, 100, 250, 500, 1000, 2000, 5000, 10000, 20000 };
        var frequencyPen = new Pen(new SolidColorBrush(Color.FromArgb(28, 65, 119, 160)), 0.5);
        foreach (var frequency in frequencies)
        {
            var x = Math.Log10(frequency / 20d) / 3d * plotWidth;
            dc.DrawLine(frequencyPen, new Point(x, 0), new Point(x, plotHeight));
        }

        var slot = plotWidth / VisualBarCount;
        var barWidth = Math.Max(2, slot - 2.25);
        for (var i = 0; i < shown.Length; i++)
        {
            var amount = i / (float)(shown.Length - 1);
            var color = Color.FromRgb(
                (byte)(StartColor.R + (EndColor.R - StartColor.R) * amount),
                (byte)(StartColor.G + (EndColor.G - StartColor.G) * amount),
                (byte)(StartColor.B + (EndColor.B - StartColor.B) * amount));
            var barHeight = Math.Max(1, shown[i] * Math.Max(1, plotHeight - 6));
            var top = plotHeight - barHeight - 2;
            var brush = new LinearGradientBrush(Color.FromArgb(235, color.R, color.G, color.B), Color.FromArgb(130, color.R, color.G, color.B), 90);
            dc.DrawRectangle(brush, null, new Rect(i * slot + 1.2, top, barWidth, barHeight));
        }

        var labels = new[] { "20", "50", "100", "250", "500", "1K", "2K", "5K", "10K", "20K" };
        var foreground = new SolidColorBrush(Color.FromRgb(117, 148, 184));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText MakeLabel(string text) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)Application.Current.FindResource("InterFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11, foreground, dpi);

        for (var i = 0; i < labels.Length; i++)
        {
            var label = MakeLabel(labels[i]);
            var x = Math.Log10(frequencies[i] / 20d) / 3d * plotWidth;
            if (i == labels.Length - 1) x -= label.Width;
            else if (i > 0) x -= label.Width / 2;
            dc.DrawText(label, new Point(Math.Max(0, x), plotHeight + 5));
        }
        dc.DrawText(MakeLabel("Hz"), new Point(plotWidth + 6, plotHeight + 5));

        var axisLabels = new[] { ("0", 0d), ("-12", .25d), ("-24", .5d), ("-48", 1d) };
        foreach (var (text, position) in axisLabels)
        {
            var label = MakeLabel(text);
            var y = position >= 1 ? plotHeight - label.Height : plotHeight * position - 2;
            dc.DrawText(label, new Point(plotWidth + 10, Math.Max(0, y)));
        }
    }
}
