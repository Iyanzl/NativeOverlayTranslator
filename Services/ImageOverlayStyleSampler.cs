using System.Drawing;
using System.Windows;

namespace NativeOverlayTranslator.Services;

public static class ImageOverlayStyleSampler
{
    public static ImageOverlayStyle Sample(Bitmap bitmap, Rect bounds)
    {
        var rect = Clamp(bitmap, bounds);
        var expanded = Expand(bitmap, rect, 3);
        var background = EstimateBackground(bitmap, rect, expanded);
        var foreground = EstimateForeground(bitmap, rect, background);
        var fontWeight = EstimateInkDensity(bitmap, rect, background) > 0.08
            ? FontWeights.SemiBold
            : FontWeights.Normal;

        return new ImageOverlayStyle(
            Color.FromArgb(255, background.R, background.G, background.B),
            Color.FromArgb(255, foreground.R, foreground.G, foreground.B),
            fontWeight);
    }

    private static Rectangle Clamp(Bitmap bitmap, Rect bounds)
    {
        var x = Math.Clamp((int)Math.Floor(bounds.X), 0, Math.Max(0, bitmap.Width - 1));
        var y = Math.Clamp((int)Math.Floor(bounds.Y), 0, Math.Max(0, bitmap.Height - 1));
        var w = Math.Clamp((int)Math.Ceiling(bounds.Width), 1, bitmap.Width - x);
        var h = Math.Clamp((int)Math.Ceiling(bounds.Height), 1, bitmap.Height - y);
        return new Rectangle(x, y, w, h);
    }

    private static Rectangle Expand(Bitmap bitmap, Rectangle rect, int padding)
    {
        var left = Math.Max(0, rect.Left - padding);
        var top = Math.Max(0, rect.Top - padding);
        var right = Math.Min(bitmap.Width, rect.Right + padding);
        var bottom = Math.Min(bitmap.Height, rect.Bottom + padding);
        return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Color EstimateBackground(Bitmap bitmap, Rectangle rect, Rectangle expanded)
    {
        var samples = new List<Color>();
        AddBorderSamples(bitmap, rect, samples);

        var coarse = SampleGrid(bitmap, expanded, 7);
        if (coarse.Count > 0)
        {
            var median = MedianColor(coarse);
            samples.AddRange(coarse.Where(color => ColorDistance(color, median) < 45));
        }

        return samples.Count == 0 ? Color.FromArgb(245, 245, 245) : MedianColor(samples);
    }

    private static Color EstimateForeground(Bitmap bitmap, Rectangle rect, Color background)
    {
        var samples = SampleGrid(bitmap, rect, 10)
            .Where(color => ColorDistance(color, background) >= 55)
            .ToList();

        if (samples.Count == 0)
        {
            return Luminance(background) < 0.48 ? Color.WhiteSmoke : Color.FromArgb(20, 20, 20);
        }

        return MedianColor(samples);
    }

    private static double EstimateInkDensity(Bitmap bitmap, Rectangle rect, Color background)
    {
        var total = 0;
        var ink = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                total++;
                if (ColorDistance(bitmap.GetPixel(x, y), background) >= 55)
                {
                    ink++;
                }
            }
        }

        return total == 0 ? 0 : ink / (double)total;
    }

    private static void AddBorderSamples(Bitmap bitmap, Rectangle rect, List<Color> samples)
    {
        var expanded = Expand(bitmap, rect, 2);
        var step = Math.Max(1, Math.Min(expanded.Width, expanded.Height) / 8);
        for (var x = expanded.Left; x < expanded.Right; x += step)
        {
            samples.Add(bitmap.GetPixel(x, expanded.Top));
            samples.Add(bitmap.GetPixel(x, Math.Max(expanded.Top, expanded.Bottom - 1)));
        }

        for (var y = expanded.Top; y < expanded.Bottom; y += step)
        {
            samples.Add(bitmap.GetPixel(expanded.Left, y));
            samples.Add(bitmap.GetPixel(Math.Max(expanded.Left, expanded.Right - 1), y));
        }
    }

    private static List<Color> SampleGrid(Bitmap bitmap, Rectangle rect, int targetSteps)
    {
        var samples = new List<Color>();
        var stepX = Math.Max(1, rect.Width / targetSteps);
        var stepY = Math.Max(1, rect.Height / targetSteps);
        for (var y = rect.Top; y < rect.Bottom; y += stepY)
        {
            for (var x = rect.Left; x < rect.Right; x += stepX)
            {
                samples.Add(bitmap.GetPixel(x, y));
            }
        }

        return samples;
    }

    private static Color MedianColor(IReadOnlyCollection<Color> samples)
    {
        static int Median(IEnumerable<int> values)
        {
            var ordered = values.Order().ToList();
            return ordered[ordered.Count / 2];
        }

        return Color.FromArgb(
            Median(samples.Select(c => (int)c.R)),
            Median(samples.Select(c => (int)c.G)),
            Median(samples.Select(c => (int)c.B)));
    }

    private static double ColorDistance(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static double Luminance(Color color)
    {
        return (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
    }
}

public sealed record ImageOverlayStyle(Color Background, Color Foreground, FontWeight FontWeight);
