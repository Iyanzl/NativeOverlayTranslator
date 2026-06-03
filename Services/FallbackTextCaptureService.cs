using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class FallbackTextCaptureService(ITextCaptureService primary, ITextCaptureService fallback) : ITextCaptureService
{
    public string Name => $"{primary.Name} -> {fallback.Name}";

    public async Task<IReadOnlyList<OcrTextLine>> CaptureAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken)
    {
        return await CaptureWithFallbackAsync(
            () => primary.CaptureAsync(target, region, cancellationToken),
            () => fallback.CaptureAsync(target, region, cancellationToken),
            cancellationToken);
    }

    public async Task<IReadOnlyList<OcrTextLine>> CaptureWordsAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken)
    {
        return await CaptureWithFallbackAsync(
            () => primary.CaptureWordsAsync(target, region, cancellationToken),
            () => fallback.CaptureWordsAsync(target, region, cancellationToken),
            cancellationToken);
    }

    public async Task<IReadOnlyList<OcrTextLine>> CaptureImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        return await CaptureWithFallbackAsync(
            () => primary.CaptureImageAsync(imagePath, cancellationToken),
            () => fallback.CaptureImageAsync(imagePath, cancellationToken),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<OcrTextLine>> CaptureWithFallbackAsync(
        Func<Task<IReadOnlyList<OcrTextLine>>> primaryCapture,
        Func<Task<IReadOnlyList<OcrTextLine>>> fallbackCapture,
        CancellationToken cancellationToken)
    {
        try
        {
            var lines = await primaryCapture();
            if (!IsUnavailableResult(lines))
            {
                return lines;
            }

            Diagnostics.Log($"OCR primary unavailable: {lines[0].Text}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"OCR primary failed; falling back. error='{ex}'");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await fallbackCapture();
    }

    private static bool IsUnavailableResult(IReadOnlyList<OcrTextLine> lines)
    {
        return lines.Count == 1 &&
               (lines[0].Text.StartsWith("PaddleOCR unavailable:", StringComparison.OrdinalIgnoreCase) ||
                lines[0].Text.StartsWith("Invalid PaddleOCR endpoint:", StringComparison.OrdinalIgnoreCase));
    }
}
