using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class PlaceholderHookTextSource : IHookTextSource
{
    public string Name => "Hook placeholder";

    public event EventHandler<HookTextReceivedEventArgs>? TextReceived;

    public bool IsAvailableFor(TargetWindowInfo target) => false;

    public Task StartAsync(TargetWindowInfo target, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void EmitForTests(string text)
    {
        TextReceived?.Invoke(this, new HookTextReceivedEventArgs { SourceText = text });
    }
}
