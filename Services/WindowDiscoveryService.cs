using System.Diagnostics;
using System.Text;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class WindowDiscoveryService
{
    public IReadOnlyList<TargetWindowInfo> GetTopLevelWindows()
    {
        var windows = new List<TargetWindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLength(hWnd);
            if (titleLength <= 0)
            {
                return true;
            }

            var title = new StringBuilder(titleLength + 1);
            NativeMethods.GetWindowText(hWnd, title, title.Capacity);
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);

            try
            {
                using var process = Process.GetProcessById((int)pid);
                windows.Add(new TargetWindowInfo
                {
                    Handle = hWnd,
                    ProcessId = (int)pid,
                    ProcessName = process.ProcessName,
                    ProcessPath = TryGetProcessPath(process),
                    Title = title.ToString()
                });
            }
            catch
            {
                windows.Add(new TargetWindowInfo
                {
                    Handle = hWnd,
                    ProcessId = (int)pid,
                    Title = title.ToString()
                });
            }

            return true;
        }, 0);

        return windows
            .OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public TargetWindowInfo? GetForegroundWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return GetTopLevelWindows().FirstOrDefault(window => window.Handle == foreground);
    }

    private static string TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? "";
        }
        catch
        {
            return "";
        }
    }
}
