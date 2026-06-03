using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public interface ITextCaptureService
{
    string Name { get; }
    Task<IReadOnlyList<OcrTextLine>> CaptureAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken);
    Task<IReadOnlyList<OcrTextLine>> CaptureWordsAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken);
    Task<IReadOnlyList<OcrTextLine>> CaptureImageAsync(string imagePath, CancellationToken cancellationToken);
}
