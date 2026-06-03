using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public static class OcrFailureDetector
{
    public static bool IsFailureResult(IReadOnlyList<OcrTextLine> lines)
    {
        return TryGetFailureText(lines, out _);
    }

    public static string BuildFailureMessage(string engineName, IReadOnlyList<OcrTextLine> lines)
    {
        return TryGetFailureText(lines, out var failureText)
            ? $"{engineName} failed. {failureText}"
            : $"{engineName} failed.";
    }

    private static bool TryGetFailureText(IReadOnlyList<OcrTextLine> lines, out string failureText)
    {
        failureText = "";
        if (lines.Count != 1)
        {
            return false;
        }

        var text = lines[0].Text.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (text.Contains(" unavailable:", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(" failed (", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Invalid ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Tesseract not found:", StringComparison.OrdinalIgnoreCase))
        {
            failureText = text;
            return true;
        }

        return false;
    }
}
