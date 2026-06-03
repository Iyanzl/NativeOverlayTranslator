using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public interface IHookTextSource
{
    string Name { get; }
    bool IsAvailableFor(TargetWindowInfo target);
    Task StartAsync(TargetWindowInfo target, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    event EventHandler<HookTextReceivedEventArgs>? TextReceived;
}

public sealed class HookTextReceivedEventArgs : EventArgs
{
    public required string SourceText { get; init; }
    public string? Context { get; init; }
}
