namespace NativeOverlayTranslator.Models;

public enum HotkeyAction
{
    TogglePanel,
    FullOcr,
    RegionOcr,
    ScreenshotTranslate,
    ManualOverlay,
    EditOverlays,
    LockOverlays,
    ClearOverlays,
    ToggleHoverTranslate,
    ToggleClipboardDoubleCopy
}

public sealed class HotkeyEditorItem
{
    public HotkeyAction Action { get; init; }
    public string DisplayName { get; init; } = "";
    public string Gesture { get; set; } = "";
}
