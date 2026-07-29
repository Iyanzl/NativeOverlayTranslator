using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class TargetWindowStateService
{
    public TargetWindowState Read(TargetWindowInfo target)
    {
        if (target.Handle == 0 || !NativeMethods.IsWindow(target.Handle))
        {
            return TargetWindowState.Invalid;
        }

        NativeMethods.GetWindowThreadProcessId(target.Handle, out var actualProcessId);
        if (actualProcessId != target.ProcessId ||
            !NativeMethods.GetWindowRect(target.Handle, out var windowRect) ||
            !NativeMethods.GetClientRect(target.Handle, out var clientRect))
        {
            return TargetWindowState.Invalid;
        }

        var clientOrigin = new NativeMethods.POINT { X = clientRect.Left, Y = clientRect.Top };
        if (!NativeMethods.ClientToScreen(target.Handle, ref clientOrigin))
        {
            return TargetWindowState.Invalid;
        }

        var foregroundProcessId = 0;
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow != 0)
        {
            NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var foregroundPid);
            foregroundProcessId = (int)foregroundPid;
        }

        return new TargetWindowState(
            IsValid: true,
            IsVisible: NativeMethods.IsWindowVisible(target.Handle),
            IsMinimized: NativeMethods.IsIconic(target.Handle),
            ForegroundProcessId: foregroundProcessId,
            WindowBounds: ToRect(windowRect),
            ClientBounds: new Rect(
                clientOrigin.X,
                clientOrigin.Y,
                Math.Max(0, clientRect.Right - clientRect.Left),
                Math.Max(0, clientRect.Bottom - clientRect.Top)));
    }

    private static Rect ToRect(NativeMethods.RECT rect)
    {
        return new Rect(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }
}

public readonly record struct TargetWindowState(
    bool IsValid,
    bool IsVisible,
    bool IsMinimized,
    int ForegroundProcessId,
    Rect WindowBounds,
    Rect ClientBounds)
{
    public static TargetWindowState Invalid { get; } = new(
        IsValid: false,
        IsVisible: false,
        IsMinimized: false,
        ForegroundProcessId: 0,
        WindowBounds: Rect.Empty,
        ClientBounds: Rect.Empty);
}
