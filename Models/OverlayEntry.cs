using System.Windows;

namespace NativeOverlayTranslator.Models;

public sealed class OverlayEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProcessName { get; set; } = "";
    public string ProcessPath { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string SourceText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public Rect Bounds { get; set; }
    public Rect SourceBounds { get; set; }
    public bool IsTargetAnchored { get; set; }
    public Rect AnchorBounds { get; set; }
    public string BackgroundColor { get; set; } = "#EAF7F7F2";
    public string ForegroundColor { get; set; } = "#101418";
    public string BorderColor { get; set; } = "#4A2F3437";
    public double FontSize { get; set; } = 18;
    public bool FontSizeIsPhysicalPixels { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
