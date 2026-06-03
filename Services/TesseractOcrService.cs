using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class TesseractOcrService(AppSettings settings) : ITextCaptureService
{
    public string Name => "Tesseract OCR";

    public async Task<IReadOnlyList<OcrTextLine>> CaptureAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken)
    {
        var captureBounds = ResolveCaptureBounds(target, region);
        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
        {
            return [];
        }

        if (!File.Exists(settings.TesseractPath))
        {
            return
            [
                new OcrTextLine($"Tesseract not found: {settings.TesseractPath}", captureBounds, 1)
            ];
        }

        var tempImage = Path.Combine(Path.GetTempPath(), $"not_ocr_{Guid.NewGuid():N}.png");
        string? processedImage = null;
        try
        {
            CaptureScreenRegion(captureBounds, tempImage);
            var preprocess = PreprocessForOcr(tempImage);
            processedImage = preprocess.ImagePath;
            return await RecognizeWithFallbackAsync(processedImage, captureBounds, preprocess.Scale, cancellationToken);
        }
        finally
        {
            TryDelete(tempImage);
            if (processedImage is not null && processedImage != tempImage)
            {
                TryDelete(processedImage);
            }
        }
    }

    public async Task<IReadOnlyList<OcrTextLine>> CaptureWordsAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken)
    {
        var captureBounds = ResolveCaptureBounds(target, region);
        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
        {
            return [];
        }

        if (!File.Exists(settings.TesseractPath))
        {
            return
            [
                new OcrTextLine($"Tesseract not found: {settings.TesseractPath}", captureBounds, 1)
            ];
        }

        var tempImage = Path.Combine(Path.GetTempPath(), $"not_ocr_{Guid.NewGuid():N}.png");
        string? processedImage = null;
        try
        {
            CaptureScreenRegion(captureBounds, tempImage);
            var preprocess = PreprocessForOcr(tempImage);
            processedImage = preprocess.ImagePath;
            return await RecognizeWordsAsync(processedImage, captureBounds, preprocess.Scale, cancellationToken);
        }
        finally
        {
            TryDelete(tempImage);
            if (processedImage is not null && processedImage != tempImage)
            {
                TryDelete(processedImage);
            }
        }
    }

    public async Task<IReadOnlyList<OcrTextLine>> CaptureImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file was not found.", imagePath);
        }

        if (!File.Exists(settings.TesseractPath))
        {
            throw new FileNotFoundException("Tesseract was not found.", settings.TesseractPath);
        }

        var padded = AddImagePadding(imagePath, 10);
        var preprocess = PreprocessForOcr(padded.ImagePath);
        try
        {
            return (await RecognizeWithFallbackAsync(preprocess.ImagePath, new Rect(-padded.Padding, -padded.Padding, 0, 0), preprocess.Scale, cancellationToken))
                .Where(line => line.Bounds.Right >= 0 && line.Bounds.Bottom >= 0)
                .Select(line => new OcrTextLine(
                    line.Text,
                    new Rect(Math.Max(0, line.Bounds.X), Math.Max(0, line.Bounds.Y), line.Bounds.Width, line.Bounds.Height),
                    line.Confidence))
                .ToList();
        }
        finally
        {
            if (preprocess.ImagePath != imagePath)
            {
                TryDelete(preprocess.ImagePath);
            }

            if (padded.ImagePath != imagePath)
            {
                TryDelete(padded.ImagePath);
            }
        }
    }

    private async Task<IReadOnlyList<OcrTextLine>> RecognizeWithFallbackAsync(string imagePath, Rect captureBounds, double coordinateScale, CancellationToken cancellationToken)
    {
        var primaryPsm = GetPrimaryPsm();
        var primary = ParseTsv(await RunTesseractAsync(imagePath, primaryPsm, cancellationToken), captureBounds, coordinateScale);

        if (primary.Count >= 5 || primaryPsm == 11 || IsJapaneseSource())
        {
            return primary;
        }

        var sparse = ParseTsv(await RunTesseractAsync(imagePath, 11, cancellationToken), captureBounds, coordinateScale);
        return sparse.Count > primary.Count ? sparse : primary;
    }

    private async Task<IReadOnlyList<OcrTextLine>> RecognizeWordsAsync(string imagePath, Rect captureBounds, double coordinateScale, CancellationToken cancellationToken)
    {
        var primaryPsm = IsJapaneseSource() ? GetPrimaryPsm() : 11;
        var primary = ParseWordsTsv(await RunTesseractAsync(imagePath, primaryPsm, cancellationToken), captureBounds, coordinateScale);

        if (IsJapaneseSource())
        {
            return primary;
        }

        return primary;
    }

    private int GetPrimaryPsm()
    {
        if (IsJapaneseSource())
        {
            return 6;
        }

        return Math.Clamp(settings.OcrPageSegmentationMode, 3, 13);
    }

    private bool IsJapaneseSource()
    {
        return settings.SourceLanguage.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || settings.SourceLanguage.Equals("jpn", StringComparison.OrdinalIgnoreCase)
            || settings.SourceLanguage.Equals("japanese", StringComparison.OrdinalIgnoreCase)
            || settings.OcrLanguages.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(lang => lang.Equals("jpn", StringComparison.OrdinalIgnoreCase));
    }

    private OcrPreprocessResult PreprocessForOcr(string imagePath)
    {
        if (IsEnglishSource())
        {
            return ScaleImage(imagePath, 3.0);
        }

        if (!IsJapaneseSource())
        {
            return new OcrPreprocessResult(imagePath, 1.0);
        }

        return ScaleImage(imagePath, 2.0);
    }

    private bool IsEnglishSource()
    {
        return settings.SourceLanguage.Equals("en", StringComparison.OrdinalIgnoreCase)
            || settings.SourceLanguage.Equals("eng", StringComparison.OrdinalIgnoreCase)
            || settings.SourceLanguage.Equals("english", StringComparison.OrdinalIgnoreCase)
            || settings.OcrLanguages.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(lang => lang.Equals("eng", StringComparison.OrdinalIgnoreCase));
    }

    private static OcrPreprocessResult ScaleImage(string imagePath, double scale)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"not_ocr_pre_{Guid.NewGuid():N}.png");
        using var source = new Bitmap(imagePath);
        using var scaled = new Bitmap((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        scaled.Save(outputPath, ImageFormat.Png);
        return new OcrPreprocessResult(outputPath, scale);
    }

    private static Rect ResolveCaptureBounds(TargetWindowInfo? target, Rect? region)
    {
        if (region is { } explicitRegion)
        {
            return PadRegion(Normalize(explicitRegion), 16);
        }

        if (target is not null && NativeMethods.GetWindowRect(target.Handle, out var rect))
        {
            return new Rect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        return new Rect(
            System.Windows.Forms.SystemInformation.VirtualScreen.Left,
            System.Windows.Forms.SystemInformation.VirtualScreen.Top,
            System.Windows.Forms.SystemInformation.VirtualScreen.Width,
            System.Windows.Forms.SystemInformation.VirtualScreen.Height);
    }

    private static Rect PadRegion(Rect rect, double padding)
    {
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        var x = Math.Max(screen.Left, rect.X - padding);
        var y = Math.Max(screen.Top, rect.Y - padding);
        var right = Math.Min(screen.Right, rect.Right + padding);
        var bottom = Math.Min(screen.Bottom, rect.Bottom + padding);
        return new Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static OcrPaddedImage AddImagePadding(string imagePath, int padding)
    {
        if (padding <= 0)
        {
            return new OcrPaddedImage(imagePath, 0);
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"not_ocr_pad_{Guid.NewGuid():N}.png");
        using var source = new Bitmap(imagePath);
        using var padded = new Bitmap(source.Width + padding * 2, source.Height + padding * 2, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(padded);
        graphics.Clear(Color.White);
        graphics.DrawImage(source, padding, padding, source.Width, source.Height);
        padded.Save(outputPath, ImageFormat.Png);
        return new OcrPaddedImage(outputPath, padding);
    }

    private static Rect Normalize(Rect rect)
    {
        var x = Math.Min(rect.Left, rect.Right);
        var y = Math.Min(rect.Top, rect.Bottom);
        return new Rect(x, y, Math.Abs(rect.Width), Math.Abs(rect.Height));
    }

    private static void CaptureScreenRegion(Rect bounds, string outputPath)
    {
        using var bitmap = new Bitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), 0, 0, bitmap.Size);
        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private async Task<string> RunTesseractAsync(string imagePath, int psm, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.TesseractPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(settings.TesseractPath) ?? Environment.CurrentDirectory
        };

        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(settings.OcrLanguages);
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add(psm.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("tsv");

        var tessdata = Path.Combine(Path.GetDirectoryName(settings.TesseractPath) ?? "", "tessdata");
        if (Directory.Exists(tessdata))
        {
            startInfo.Environment["TESSDATA_PREFIX"] = tessdata;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start tesseract.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        if (process.ExitCode == 0)
        {
            return output;
        }

        var error = await errorTask;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Tesseract exited with {process.ExitCode}." : error.Trim());
    }

    private static IReadOnlyList<OcrTextLine> ParseTsv(string tsv, Rect captureBounds, double coordinateScale)
    {
        return MergeWordsIntoTsvLines(ReadWordsFromTsv(tsv, captureBounds, coordinateScale))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToList();
    }

    private static IReadOnlyList<OcrTextLine> ParseWordsTsv(string tsv, Rect captureBounds, double coordinateScale)
    {
        return ReadWordsFromTsv(tsv, captureBounds, coordinateScale)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => new OcrTextLine(word.Text, word.Bounds, word.Confidence >= 0 ? word.Confidence / 100.0 : 0))
            .ToList();
    }

    private static IReadOnlyList<OcrWord> ReadWordsFromTsv(string tsv, Rect captureBounds, double coordinateScale)
    {
        var words = new List<OcrWord>();
        using var reader = new StringReader(tsv);
        _ = reader.ReadLine();

        while (reader.ReadLine() is { } row)
        {
            var columns = row.Split('\t');
            if (columns.Length < 12 || columns[0] != "5")
            {
                continue;
            }

            var text = columns[11].Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var blockNumber) ||
                !int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var paragraphNumber) ||
                !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber) ||
                !int.TryParse(columns[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wordNumber) ||
                !int.TryParse(columns[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left) ||
                !int.TryParse(columns[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) ||
                !int.TryParse(columns[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
                !int.TryParse(columns[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                continue;
            }

            var conf = double.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedConf) ? parsedConf : -1;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            words.Add(new OcrWord(
                text,
                new Rect(
                    captureBounds.X + left / coordinateScale,
                    captureBounds.Y + top / coordinateScale,
                    width / coordinateScale,
                    height / coordinateScale),
                conf,
                blockNumber,
                paragraphNumber,
                lineNumber,
                wordNumber));
        }

        return words;
    }

    private static IReadOnlyList<OcrTextLine> MergeWordsIntoTsvLines(IEnumerable<OcrWord> words)
    {
        var orderedWords = words
            .OrderBy(word => word.BlockNumber)
            .ThenBy(word => word.ParagraphNumber)
            .ThenBy(word => word.LineNumber)
            .ThenBy(word => word.WordNumber)
            .ThenBy(word => word.Bounds.Left)
            .ToList();

        var logicalLines = orderedWords
            .GroupBy(word => new { word.BlockNumber, word.ParagraphNumber, word.LineNumber })
            .SelectMany(group => SplitLogicalLine(group.OrderBy(word => word.WordNumber).ThenBy(word => word.Bounds.Left)))
            .ToList();

        if (logicalLines.Count > 0)
        {
            return logicalLines
                .OrderBy(line => line.Bounds.Top)
                .ThenBy(line => line.Bounds.Left)
                .ToList();
        }

        return MergeWordsIntoVisualLines(orderedWords);
    }

    private static IEnumerable<OcrTextLine> SplitLogicalLine(IEnumerable<OcrWord> words)
    {
        var current = new Accumulator();
        foreach (var word in words)
        {
            if (!current.IsEmpty && current.GapTo(word) > Math.Max(180, Math.Max(current.Height, word.Bounds.Height) * 14))
            {
                yield return current.ToLine();
                current = new Accumulator();
            }

            current.Add(word.Text, word.Bounds, word.Confidence);
        }

        if (!current.IsEmpty)
        {
            yield return current.ToLine();
        }
    }

    private static IReadOnlyList<OcrTextLine> MergeWordsIntoVisualLines(IEnumerable<OcrWord> words)
    {
        var orderedWords = words
            .OrderBy(word => word.Bounds.Top)
            .ThenBy(word => word.Bounds.Left)
            .ToList();

        var lines = new List<Accumulator>();
        foreach (var word in orderedWords)
        {
            var midY = word.Bounds.Top + word.Bounds.Height / 2;
            var line = lines
                .Where(candidate => candidate.CanAccept(word))
                .OrderBy(candidate => Math.Abs(candidate.MidY - midY))
                .FirstOrDefault();

            if (line is null)
            {
                line = new Accumulator();
                lines.Add(line);
            }

            line.Add(word.Text, word.Bounds, word.Confidence);
        }

        return lines
            .OrderBy(line => line.Bounds.Top)
            .Select(line => line.ToLine())
            .ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for temporary screenshots.
        }
    }

    private sealed record OcrWord(string Text, Rect Bounds, double Confidence, int BlockNumber, int ParagraphNumber, int LineNumber, int WordNumber);

    private sealed record OcrPreprocessResult(string ImagePath, double Scale);

    private sealed record OcrPaddedImage(string ImagePath, int Padding);

    private sealed class Accumulator
    {
        private readonly StringBuilder _text = new();
        private Rect? _lastBounds;
        private Rect _bounds = Rect.Empty;
        private double _confidenceTotal;
        private int _confidenceCount;

        public Rect Bounds => _bounds;
        public bool IsEmpty => _bounds.IsEmpty;
        public double Height => _bounds.IsEmpty ? 0 : _bounds.Height;
        public double MidY => _bounds.IsEmpty ? 0 : _bounds.Top + _bounds.Height / 2;

        public double GapTo(OcrWord word)
        {
            return _bounds.IsEmpty ? 0 : word.Bounds.Left - _bounds.Right;
        }

        public bool CanAccept(OcrWord word)
        {
            if (_bounds.IsEmpty)
            {
                return true;
            }

            var midY = word.Bounds.Top + word.Bounds.Height / 2;
            var sameVisualLine = Math.Abs(MidY - midY) <= Math.Max(8, Math.Max(Height, word.Bounds.Height) * 0.60);
            if (!sameVisualLine)
            {
                return false;
            }

            var gap = word.Bounds.Left - _bounds.Right;
            if (gap <= 0)
            {
                return true;
            }

            var maxJoinGap = Math.Max(86, Math.Max(Height, word.Bounds.Height) * 7.0);
            return gap <= maxJoinGap;
        }

        public void Add(string text, Rect bounds, double confidence)
        {
            var addSpace = ShouldInsertSpace(_lastBounds, bounds, text);
            if (_text.Length > 0)
            {
                if (addSpace)
                {
                    _text.Append(' ');
                }
            }

            _text.Append(text);
            _lastBounds = bounds;
            _bounds = _bounds.IsEmpty ? bounds : Rect.Union(_bounds, bounds);
            if (confidence >= 0)
            {
                _confidenceTotal += confidence;
                _confidenceCount++;
            }
        }

        public OcrTextLine ToLine()
        {
            var confidence = _confidenceCount == 0 ? 0 : _confidenceTotal / _confidenceCount / 100.0;
            return new OcrTextLine(_text.ToString(), _bounds, confidence);
        }

        private static bool ShouldInsertSpace(Rect? previousBounds, Rect currentBounds, string currentText)
        {
            if (previousBounds is null)
            {
                return false;
            }

            if (ContainsCjkOrKana(currentText))
            {
                return false;
            }

            var gap = currentBounds.Left - previousBounds.Value.Right;
            if (IsLatinWordBoundary(currentText) && gap >= -1)
            {
                return true;
            }

            return gap > Math.Max(3, previousBounds.Value.Height * 0.18);
        }

        private static bool IsLatinWordBoundary(string currentText)
        {
            return currentText.Length > 0 && char.IsLetterOrDigit(currentText[0]);
        }

        private static bool ContainsCjkOrKana(string text)
        {
            return text.Any(c =>
                c is >= '\u3040' and <= '\u30ff' ||
                c is >= '\u4e00' and <= '\u9fff');
        }
    }
}
