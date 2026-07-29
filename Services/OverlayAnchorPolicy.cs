using System.Windows;

namespace NativeOverlayTranslator.Services;

public static class OverlayAnchorPolicy
{
    public static Rect CreateAnchor(Rect screenBounds, Rect referenceBounds)
    {
        return new Rect(
            screenBounds.X - referenceBounds.X,
            screenBounds.Y - referenceBounds.Y,
            screenBounds.Width,
            screenBounds.Height);
    }

    public static Rect ResolveScreenBounds(Rect anchorBounds, Rect referenceBounds)
    {
        return new Rect(
            referenceBounds.X + anchorBounds.X,
            referenceBounds.Y + anchorBounds.Y,
            anchorBounds.Width,
            anchorBounds.Height);
    }

    public static bool ShouldDisplay(
        bool targetValid,
        bool targetVisible,
        bool targetMinimized,
        int foregroundProcessId,
        int targetProcessId,
        int translatorProcessId)
    {
        if (!targetValid || !targetVisible || targetMinimized)
        {
            return false;
        }

        return foregroundProcessId == targetProcessId || foregroundProcessId == translatorProcessId;
    }
}
