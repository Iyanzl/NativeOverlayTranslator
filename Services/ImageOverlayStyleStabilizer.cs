using System.Windows;

namespace NativeOverlayTranslator.Services;

public sealed class ImageOverlayStyleStabilizer
{
    private readonly List<StyleEntry> _entries = [];

    public ImageOverlayStyle Resolve(string sourceText, Rect bounds, ImageOverlayStyle sampledStyle)
    {
        var normalizedText = TextTranslationFilter.Normalize(sourceText);
        var existing = _entries
            .Where(entry => string.Equals(entry.SourceText, normalizedText, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new { Entry = entry, Distance = BoundsDistance(entry.Bounds, bounds) })
            .Where(item => item.Distance <= MatchTolerance(item.Entry.Bounds, bounds))
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        if (existing is not null)
        {
            existing.Entry.Bounds = bounds;
            return existing.Entry.Style;
        }

        _entries.Add(new StyleEntry(normalizedText, bounds, sampledStyle));
        return sampledStyle;
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private static double BoundsDistance(Rect first, Rect second)
    {
        var firstCenterX = first.Left + first.Width / 2;
        var firstCenterY = first.Top + first.Height / 2;
        var secondCenterX = second.Left + second.Width / 2;
        var secondCenterY = second.Top + second.Height / 2;
        var centerDistance = Math.Abs(firstCenterX - secondCenterX) + Math.Abs(firstCenterY - secondCenterY);
        var sizeDistance = Math.Abs(first.Width - second.Width) * 0.35 + Math.Abs(first.Height - second.Height) * 0.5;
        return centerDistance + sizeDistance;
    }

    private static double MatchTolerance(Rect first, Rect second)
    {
        return Math.Clamp(Math.Max(first.Height, second.Height) * 0.75, 8, 24);
    }

    private sealed class StyleEntry(string sourceText, Rect bounds, ImageOverlayStyle style)
    {
        public string SourceText { get; } = sourceText;
        public Rect Bounds { get; set; } = bounds;
        public ImageOverlayStyle Style { get; } = style;
    }
}
