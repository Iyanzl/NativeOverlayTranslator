using System.Drawing;
using System.Windows;

namespace NativeOverlayTranslator.Services;

public static class ImageOverlayStyleSampler
{
    private const int ColorBucketSize = 24;
    private const double BackgroundMergeDistance = 44;
    private const double ForegroundMergeDistance = 52;
    private const double ForegroundContrastThreshold = 48;

    public static ImageOverlayStyle Sample(Bitmap bitmap, Rect bounds)
    {
        var rect = Clamp(bitmap, bounds);
        var background = EstimateBackground(bitmap, rect);
        var foreground = EstimateForeground(bitmap, rect, background);
        var fontWeight = EstimateInkDensity(bitmap, rect, foreground) > 0.065
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

    private static Color EstimateBackground(Bitmap bitmap, Rectangle rect)
    {
        var outer = Expand(bitmap, rect, 4);
        var samples = new List<PixelSample>();
        var stride = CalculateStride(outer, 5000);
        for (var y = outer.Top; y < outer.Bottom; y += stride)
        {
            for (var x = outer.Left; x < outer.Right; x += stride)
            {
                if (!rect.Contains(x, y))
                {
                    samples.Add(new PixelSample(x, y, bitmap.GetPixel(x, y)));
                }
            }
        }

        if (samples.Count < 12)
        {
            AddRectangleBorderSamples(bitmap, rect, samples);
        }

        return samples.Count == 0
            ? Color.FromArgb(245, 245, 245)
            : FindDominantCluster(samples, BackgroundMergeDistance, preferTextShape: false);
    }

    private static Color EstimateForeground(Bitmap bitmap, Rectangle rect, Color background)
    {
        var samples = SampleRectangle(bitmap, rect, 16000)
            .Where(sample => ColorDistance(sample.Color, background) >= ForegroundContrastThreshold)
            .ToList();

        if (samples.Count == 0)
        {
            return Luminance(background) < 0.48 ? Color.WhiteSmoke : Color.FromArgb(20, 20, 20);
        }

        return FindDominantCluster(samples, ForegroundMergeDistance, preferTextShape: true);
    }

    private static double EstimateInkDensity(Bitmap bitmap, Rectangle rect, Color foreground)
    {
        var total = 0;
        var ink = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                total++;
                if (ColorDistance(bitmap.GetPixel(x, y), foreground) <= ForegroundMergeDistance)
                {
                    ink++;
                }
            }
        }

        return total == 0 ? 0 : ink / (double)total;
    }

    private static List<PixelSample> SampleRectangle(Bitmap bitmap, Rectangle rect, int maximumSamples)
    {
        var samples = new List<PixelSample>();
        var stride = CalculateStride(rect, maximumSamples);
        for (var y = rect.Top; y < rect.Bottom; y += stride)
        {
            for (var x = rect.Left; x < rect.Right; x += stride)
            {
                samples.Add(new PixelSample(x, y, bitmap.GetPixel(x, y)));
            }
        }

        return samples;
    }

    private static int CalculateStride(Rectangle rect, int maximumSamples)
    {
        var area = Math.Max(1L, (long)rect.Width * rect.Height);
        return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(area / (double)maximumSamples)));
    }

    private static void AddRectangleBorderSamples(Bitmap bitmap, Rectangle rect, List<PixelSample> samples)
    {
        var step = Math.Max(1, Math.Min(rect.Width, rect.Height) / 12);
        for (var x = rect.Left; x < rect.Right; x += step)
        {
            samples.Add(new PixelSample(x, rect.Top, bitmap.GetPixel(x, rect.Top)));
            var bottom = Math.Max(rect.Top, rect.Bottom - 1);
            samples.Add(new PixelSample(x, bottom, bitmap.GetPixel(x, bottom)));
        }

        for (var y = rect.Top; y < rect.Bottom; y += step)
        {
            samples.Add(new PixelSample(rect.Left, y, bitmap.GetPixel(rect.Left, y)));
            var right = Math.Max(rect.Left, rect.Right - 1);
            samples.Add(new PixelSample(right, y, bitmap.GetPixel(right, y)));
        }
    }

    private static Color FindDominantCluster(IReadOnlyList<PixelSample> samples, double mergeDistance, bool preferTextShape)
    {
        var buckets = samples
            .GroupBy(sample => ColorBucket.FromColor(sample.Color))
            .Select(group => new ColorCluster(group.ToList()))
            .OrderBy(cluster => cluster.Center.R)
            .ThenBy(cluster => cluster.Center.G)
            .ThenBy(cluster => cluster.Center.B)
            .ToList();

        ClusterCandidate? best = null;
        foreach (var seed in buckets)
        {
            var merged = buckets
                .Where(cluster => ColorDistance(cluster.Center, seed.Center) <= mergeDistance)
                .SelectMany(cluster => cluster.Samples)
                .ToList();
            var candidate = new ClusterCandidate(merged, preferTextShape);
            if (best is null || candidate.Score > best.Score ||
                Math.Abs(candidate.Score - best.Score) < 0.001 && candidate.Count > best.Count)
            {
                best = candidate;
            }
        }

        return best?.Center ?? samples[0].Color;
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

    private readonly record struct PixelSample(int X, int Y, Color Color);

    private readonly record struct ColorBucket(int R, int G, int B)
    {
        public static ColorBucket FromColor(Color color)
        {
            return new ColorBucket(color.R / ColorBucketSize, color.G / ColorBucketSize, color.B / ColorBucketSize);
        }
    }

    private sealed class ColorCluster(IReadOnlyList<PixelSample> samples)
    {
        public IReadOnlyList<PixelSample> Samples { get; } = samples;
        public Color Center { get; } = AverageColor(samples);
    }

    private sealed class ClusterCandidate
    {
        public ClusterCandidate(IReadOnlyList<PixelSample> samples, bool preferTextShape)
        {
            Count = samples.Count;
            Center = AverageColor(samples);
            if (!preferTextShape)
            {
                Score = Count;
                return;
            }

            var horizontalSpread = samples.Select(sample => sample.X).Distinct().Count();
            var verticalSpread = samples.Select(sample => sample.Y).Distinct().Count();
            var shapeFactor = 1.0
                + Math.Min(1.0, horizontalSpread / 24.0)
                + Math.Min(0.65, verticalSpread / 16.0);
            Score = Count * shapeFactor;
        }

        public int Count { get; }
        public Color Center { get; }
        public double Score { get; }
    }

    private static Color AverageColor(IReadOnlyList<PixelSample> samples)
    {
        if (samples.Count == 0)
        {
            return Color.Empty;
        }

        return Color.FromArgb(
            (int)Math.Round(samples.Average(sample => sample.Color.R)),
            (int)Math.Round(samples.Average(sample => sample.Color.G)),
            (int)Math.Round(samples.Average(sample => sample.Color.B)));
    }
}

public sealed record ImageOverlayStyle(Color Background, Color Foreground, FontWeight FontWeight);
