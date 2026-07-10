using System.Windows;
using System.Windows.Interop;
using Clipboard = System.Windows.Clipboard;

namespace NativeOverlayTranslator.Services;

public sealed class ClipboardDoubleCopyWatcher : IDisposable
{
    private readonly Window _owner;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private HwndSource? _source;
    private string? _lastText;
    private DateTimeOffset _lastSeenAt;
    private bool _disposed;

    public event EventHandler<string>? DoubleCopied;

    public ClipboardDoubleCopyWatcher(Window owner)
    {
        _owner = owner;
    }

    public void Start()
    {
        var handle = new WindowInteropHelper(_owner).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(handle);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            HandleClipboardUpdate();
        }

        return 0;
    }

    private async void HandleClipboardUpdate()
    {
        var entered = false;
        try
        {
            await _readGate.WaitAsync(_disposeCts.Token);
            entered = true;
            var text = await ClipboardTextReader.TryReadTextAsync(ReadClipboardText, _disposeCts.Token);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var now = DateTimeOffset.Now;
            if (text == _lastText && now - _lastSeenAt < TimeSpan.FromMilliseconds(1400))
            {
                DoubleCopied?.Invoke(this, text);
            }

            _lastText = text;
            _lastSeenAt = now;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Clipboard update failed error='{ex.Message}'");
        }
        finally
        {
            if (entered)
            {
                _readGate.Release();
            }
        }
    }

    private static string? ReadClipboardText()
    {
        return Clipboard.ContainsText() ? Clipboard.GetText() : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var handle = new WindowInteropHelper(_owner).Handle;
        if (handle != 0)
        {
            NativeMethods.RemoveClipboardFormatListener(handle);
        }

        _source?.RemoveHook(WndProc);
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _disposed = true;
    }
}
