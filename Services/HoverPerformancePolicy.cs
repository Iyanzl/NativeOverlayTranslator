using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public static class HoverPerformancePolicy
{
    public static TimeSpan TimerInterval { get; } = TimeSpan.FromMilliseconds(180);

    public static TimeSpan CaptureDelay { get; } = TimeSpan.FromMilliseconds(10);

    public static TimeSpan PointerStableDuration(HoverMode mode)
    {
        return TimeSpan.FromMilliseconds(mode switch
        {
            HoverMode.Word => 180,
            HoverMode.Phrase => 280,
            HoverMode.Sentence => 420,
            _ => 280
        });
    }

    public static uint InputQuietMilliseconds(HoverMode mode)
    {
        return mode switch
        {
            HoverMode.Word => 140,
            HoverMode.Phrase => 220,
            HoverMode.Sentence => 320,
            _ => 220
        };
    }

    public static int RequiredStableTicks(HoverMode mode)
    {
        return mode switch
        {
            HoverMode.Word => 1,
            HoverMode.Phrase => 2,
            HoverMode.Sentence => 2,
            _ => 2
        };
    }

    public static bool ShouldAppendTooltip(HoverMode mode, bool enabled, bool debugEnabled)
    {
        return enabled && !debugEnabled && mode == HoverMode.Sentence;
    }

    public static Rect CaptureRegion(int x, int y, HoverMode mode, Rect screen)
    {
        var rect = mode switch
        {
            HoverMode.Word => new Rect(x - 72, y - 24, 176, 54),
            HoverMode.Sentence => new Rect(x - 220, y - 50, 960, 176),
            _ => new Rect(x - 70, y - 30, 420, 104)
        };

        var left = Math.Clamp(rect.Left, screen.Left, screen.Right - 1);
        var top = Math.Clamp(rect.Top, screen.Top, screen.Bottom - 1);
        var right = Math.Clamp(rect.Right, left + 1, screen.Right);
        var bottom = Math.Clamp(rect.Bottom, top + 1, screen.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }
}
