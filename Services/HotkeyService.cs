using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class HotkeyService : IDisposable
{
    private const int BaseHotkeyId = 0x4600;
    private readonly Window _owner;
    private readonly Dictionary<int, HotkeyAction> _registered = [];
    private HwndSource? _source;
    private bool _disposed;

    public event EventHandler<HotkeyAction>? ActionRequested;

    public HotkeyService(Window owner)
    {
        _owner = owner;
    }

    public IReadOnlyList<string> Start(AppSettings settings)
    {
        var handle = new WindowInteropHelper(_owner).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        return RegisterConfiguredHotkeys(settings);
    }

    public IReadOnlyList<string> Restart(AppSettings settings)
    {
        UnregisterAll();
        return RegisterConfiguredHotkeys(settings);
    }

    private IReadOnlyList<string> RegisterConfiguredHotkeys(AppSettings settings)
    {
        var errors = new List<string>();
        var handle = new WindowInteropHelper(_owner).Handle;

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var key = action.ToString();
            if (!settings.Hotkeys.TryGetValue(key, out var gesture) || string.IsNullOrWhiteSpace(gesture))
            {
                continue;
            }

            if (!TryParseGesture(gesture, out var modifiers, out var virtualKey))
            {
                errors.Add($"{GetDisplayName(action)}: invalid hotkey '{gesture}'");
                continue;
            }

            var id = BaseHotkeyId + (int)action;
            if (!NativeMethods.RegisterHotKey(handle, id, modifiers, virtualKey))
            {
                errors.Add($"{GetDisplayName(action)}: failed to register '{gesture}'");
                continue;
            }

            _registered[id] = action;
        }

        return errors;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            ActionRequested?.Invoke(this, action);
            handled = true;
        }

        return 0;
    }

    private static bool TryParseGesture(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        var parts = gesture
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts[..^1])
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "SHIFT":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        var keyToken = NormalizeKeyToken(parts[^1]);
        if (!Enum.TryParse<Key>(keyToken, ignoreCase: true, out var key))
        {
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private static string NormalizeKeyToken(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "ESC" => "Escape",
            "DEL" => "Delete",
            "INS" => "Insert",
            "PGUP" => "PageUp",
            "PGDN" => "PageDown",
            "PLUS" => "Add",
            "MINUS" => "Subtract",
            _ => value
        };
    }

    public static string GetDisplayName(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.TogglePanel => "Show or hide panel",
            HotkeyAction.FullOcr => "Full-window OCR",
            HotkeyAction.RegionOcr => "Region OCR",
            HotkeyAction.ScreenshotTranslate => "Screenshot translate",
            HotkeyAction.ManualOverlay => "New manual overlay",
            HotkeyAction.EditOverlays => "Edit overlays",
            HotkeyAction.LockOverlays => "Lock overlays",
            HotkeyAction.ClearOverlays => "Clear current overlays",
            HotkeyAction.ToggleHoverTranslate => "Toggle hover translate",
            HotkeyAction.ToggleClipboardDoubleCopy => "Toggle double-copy translate",
            _ => action.ToString()
        };
    }

    private void UnregisterAll()
    {
        var handle = new WindowInteropHelper(_owner).Handle;
        foreach (var id in _registered.Keys.ToList())
        {
            NativeMethods.UnregisterHotKey(handle, id);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _disposed = true;
    }
}
