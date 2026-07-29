using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class TargetWindowEventWatcher : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private nint _targetHandle;

    public TargetWindowEventWatcher()
    {
        _callback = OnWinEvent;
    }

    public event EventHandler? Changed;

    public void Start(TargetWindowInfo target)
    {
        Stop();
        _targetHandle = target.Handle;
        AddHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, 0);
        AddHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND, (uint)target.ProcessId);
        AddHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, (uint)target.ProcessId);
    }

    public void Stop()
    {
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
        _targetHandle = 0;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void AddHook(uint eventMin, uint eventMax, uint processId)
    {
        var hook = NativeMethods.SetWinEventHook(
            eventMin,
            eventMax,
            0,
            _callback,
            processId,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
        if (hook != 0)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND ||
            window == _targetHandle &&
            (objectId == NativeMethods.OBJID_WINDOW || eventType < NativeMethods.EVENT_OBJECT_DESTROY))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
