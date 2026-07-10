using System.Runtime.InteropServices;

namespace NativeOverlayTranslator.Services;

public static class ClipboardTextReader
{
    private const int ClipboardCannotOpen = unchecked((int)0x800401D0);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(160)
    ];

    public static async Task<string?> TryReadTextAsync(
        Func<string?> readText,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(readText);
        delay ??= Task.Delay;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = readText()?.Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (COMException ex) when (ex.HResult == ClipboardCannotOpen && attempt < RetryDelays.Length)
            {
                await delay(RetryDelays[attempt], cancellationToken);
            }
            catch (COMException ex) when (ex.HResult == ClipboardCannotOpen)
            {
                return null;
            }
        }

        return null;
    }
}
