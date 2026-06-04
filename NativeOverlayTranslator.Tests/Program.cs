using System.Windows;
using NativeOverlayTranslator.Models;
using NativeOverlayTranslator.Services;

var failureLines = new[]
{
    new OcrTextLine("LunaOCR unavailable: connection refused", new Rect(0, 0, 10, 10), 1)
};

Assert(OcrFailureDetector.IsFailureResult(failureLines), "unavailable result is detected");
Assert(
    OcrFailureDetector.BuildFailureMessage("LunaOCR", failureLines).Contains("LunaOCR unavailable", StringComparison.Ordinal),
    "failure message includes backend error");

var normalLines = new[]
{
    new OcrTextLine("Start Game", new Rect(0, 0, 120, 24), 0.93)
};

Assert(!OcrFailureDetector.IsFailureResult(normalLines), "normal OCR text is not treated as failure");

var settings = new AppSettings
{
    LunaOcrEndpoint = "",
    OcrEngine = (OcrEngineKind)2,
    HoverOcrEngine = (OcrEngineKind)4
};
settings.EnsureDefaults();

Assert(settings.LunaOcrEndpoint == "http://127.0.0.1:8871/ocr", "LunaOCR endpoint default is restored");
Assert(Enum.IsDefined(typeof(OcrEngineKind), "LunaOcr"), "LunaOCR engine option exists");
Assert(settings.OcrEngine == OcrEngineKind.LunaOcr, "legacy removed OCR engine falls back to LunaOCR");
Assert(settings.HoverOcrEngine == OcrEngineKind.LunaOcr, "legacy removed hover OCR engine falls back to LunaOCR");
Assert(Enum.GetNames<OcrEngineKind>().Length == 3, "only three OCR engines remain");

Console.WriteLine("OCR service tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
