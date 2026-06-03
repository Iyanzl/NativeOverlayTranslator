namespace NativeOverlayTranslator.Models;

public enum HoverMode
{
    Word,
    Phrase,
    Sentence
}

public sealed record HoverModeOption(HoverMode Mode, string Name);
