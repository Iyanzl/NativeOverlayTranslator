namespace NativeOverlayTranslator.Models;

public sealed class TargetWindowInfo
{
    public nint Handle { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string ProcessPath { get; init; } = "";
    public string Title { get; init; } = "";

    public override string ToString()
    {
        var name = string.IsNullOrWhiteSpace(ProcessName) ? $"pid:{ProcessId}" : ProcessName;
        return string.IsNullOrWhiteSpace(Title) ? name : $"{name} - {Title}";
    }
}
