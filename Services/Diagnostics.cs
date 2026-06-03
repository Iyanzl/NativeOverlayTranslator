using System.IO;

namespace NativeOverlayTranslator.Services;

public static class Diagnostics
{
    private static readonly object Gate = new();

    public static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "diagnostic.log");
            lock (Gate)
            {
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break translation.
        }
    }
}
