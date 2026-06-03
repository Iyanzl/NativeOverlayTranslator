using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace NativeOverlayTranslator.Services;

public static class ScreenCaptureService
{
    public static string CaptureRegionToPng(Rect bounds, string directory, string prefix)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        using var bitmap = new Bitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), 0, 0, bitmap.Size);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
