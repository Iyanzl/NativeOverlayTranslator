using System.Windows;
using System.Drawing;
using System.Runtime.InteropServices;
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

Assert(HoverPerformancePolicy.TimerInterval == TimeSpan.FromMilliseconds(180), "hover timer interval is fast");
Assert(HoverPerformancePolicy.PointerStableDuration(HoverMode.Word) <= TimeSpan.FromMilliseconds(220), "word hover stabilizes quickly");
Assert(HoverPerformancePolicy.PointerStableDuration(HoverMode.Phrase) <= TimeSpan.FromMilliseconds(320), "phrase hover stabilizes quickly");
Assert(HoverPerformancePolicy.InputQuietMilliseconds(HoverMode.Word) <= 180, "word hover input quiet window is short");
Assert(HoverPerformancePolicy.RequiredStableTicks(HoverMode.Word) == 1, "word hover needs one OCR confirmation");
Assert(HoverPerformancePolicy.RequiredStableTicks(HoverMode.Phrase) == 2, "phrase hover needs two OCR confirmations");
Assert(!HoverPerformancePolicy.ShouldAppendTooltip(HoverMode.Word, enabled: true, debugEnabled: false), "word hover never appends area tooltip OCR");
Assert(!HoverPerformancePolicy.ShouldAppendTooltip(HoverMode.Phrase, enabled: true, debugEnabled: false), "phrase hover never appends area tooltip OCR");
Assert(HoverPerformancePolicy.ShouldAppendTooltip(HoverMode.Sentence, enabled: true, debugEnabled: false), "sentence hover can append tooltip OCR");
Assert(!HoverPerformancePolicy.ShouldAppendTooltip(HoverMode.Sentence, enabled: true, debugEnabled: true), "OCR debug never appends tooltip OCR");
var wordRegion = HoverPerformancePolicy.CaptureRegion(500, 500, HoverMode.Word, new Rect(0, 0, 1920, 1080));
Assert(wordRegion.Width <= 180 && wordRegion.Height <= 56, "word hover OCR region is compact");

var hoverLine = new OcrTextLine("Force: P AutoFit", new Rect(100, 20, 210, 20), 0.92);
var pickedForce = HoverTextSelector.SelectHoverText(hoverLine, 126, HoverMode.Word);
Assert(pickedForce.Text == "Force", "word hover trims punctuation and right-side text");
var pickedAutoFit = HoverTextSelector.SelectHoverText(hoverLine, 246, HoverMode.Word);
Assert(pickedAutoFit.Text == "AutoFit", "word hover can select a later word from the same OCR line");
var pickedSingleLetter = HoverTextSelector.SelectHoverText(hoverLine, 193, HoverMode.Word);
Assert(pickedSingleLetter.Text == "", "word hover ignores stray single-letter tokens");

var phraseLine = new OcrTextLine("Open recent project: Advanced settings", new Rect(100, 20, 360, 20), 0.92);
var pickedFirstPhrase = HoverTextSelector.SelectHoverText(phraseLine, 175, HoverMode.Phrase);
Assert(pickedFirstPhrase.Text == "Open recent project", "phrase hover selects the clause under the pointer");
var pickedSecondPhrase = HoverTextSelector.SelectHoverText(phraseLine, 365, HoverMode.Phrase);
Assert(pickedSecondPhrase.Text == "Advanced settings", "phrase hover does not translate the full OCR line");
var pickedSentence = HoverTextSelector.SelectHoverText(phraseLine, 175, HoverMode.Sentence);
Assert(pickedSentence.Text == phraseLine.Text, "sentence hover keeps the full OCR line");

var longPhraseLine = new OcrTextLine("Open the recently modified project from disk", new Rect(100, 20, 430, 20), 0.92);
var pickedLongPhrase = HoverTextSelector.SelectHoverText(longPhraseLine, 320, HoverMode.Phrase);
Assert(pickedLongPhrase.Text.Contains("modified", StringComparison.Ordinal), "long phrase keeps the word under the pointer");
Assert(pickedLongPhrase.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5, "long phrase is limited to a short word window");

var clipboardAttempts = 0;
var clipboardText = await ClipboardTextReader.TryReadTextAsync(
    () => ++clipboardAttempts < 3
        ? throw new COMException("Clipboard busy", unchecked((int)0x800401D0))
        : "  copied text  ",
    CancellationToken.None,
    (_, _) => Task.CompletedTask);
Assert(clipboardText == "copied text", "clipboard text is returned after OpenClipboard retries");
Assert(clipboardAttempts == 3, "clipboard busy retry uses bounded attempts");

var exhaustedAttempts = 0;
var exhaustedClipboard = await ClipboardTextReader.TryReadTextAsync(
    () =>
    {
        exhaustedAttempts++;
        throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
    },
    CancellationToken.None,
    (_, _) => Task.CompletedTask);
Assert(exhaustedClipboard is null, "persistent OpenClipboard contention is handled without throwing");
Assert(exhaustedAttempts == 6, "clipboard retries stop after the configured limit");

using var lightBitmap = CreateTextSample(Color.FromArgb(245, 245, 240), Color.FromArgb(28, 32, 38), bold: false);
var lightStyle = ImageOverlayStyleSampler.Sample(lightBitmap, new Rect(8, 8, 44, 20));
Assert(ColorDistance(lightStyle.Background, Color.FromArgb(245, 245, 240)) < 24, "light background is preserved");
Assert(ColorDistance(lightStyle.Foreground, Color.FromArgb(28, 32, 38)) < 55, "dark source text color is detected");
Assert(lightStyle.FontWeight == System.Windows.FontWeights.Normal, "regular text is not forced bold");

using var darkBitmap = CreateTextSample(Color.FromArgb(24, 28, 34), Color.FromArgb(230, 238, 245), bold: true);
var darkStyle = ImageOverlayStyleSampler.Sample(darkBitmap, new Rect(8, 8, 44, 20));
Assert(ColorDistance(darkStyle.Background, Color.FromArgb(24, 28, 34)) < 28, "dark background is preserved");
Assert(ColorDistance(darkStyle.Foreground, Color.FromArgb(230, 238, 245)) < 60, "light source text color is detected");
Assert(darkStyle.FontWeight == System.Windows.FontWeights.SemiBold, "bold-looking text keeps stronger weight");

using var noisyBitmap = CreateNoisyTextSample();
var noisyStyle = ImageOverlayStyleSampler.Sample(noisyBitmap, new Rect(8, 8, 48, 20));
Assert(ColorDistance(noisyStyle.Foreground, Color.FromArgb(28, 32, 38)) < 70, "colored noise does not dominate text color");

Console.WriteLine("OCR service tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static Bitmap CreateTextSample(Color background, Color foreground, bool bold)
{
    var bitmap = new Bitmap(64, 36);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(background);
    using var brush = new SolidBrush(foreground);
    using var font = new Font("Arial", 14, bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
    graphics.DrawString("Ab", font, brush, 8, 6);
    if (bold)
    {
        graphics.DrawString("Ab", font, brush, 9, 6);
    }

    return bitmap;
}

static double ColorDistance(Color a, Color b)
{
    var dr = a.R - b.R;
    var dg = a.G - b.G;
    var db = a.B - b.B;
    return Math.Sqrt(dr * dr + dg * dg + db * db);
}

static Bitmap CreateNoisyTextSample()
{
    var bitmap = CreateTextSample(Color.FromArgb(245, 245, 240), Color.FromArgb(28, 32, 38), bold: false);
    for (var y = 9; y < 16; y++)
    {
        bitmap.SetPixel(47, y, Color.Red);
        bitmap.SetPixel(48, y, Color.Blue);
    }

    return bitmap;
}
