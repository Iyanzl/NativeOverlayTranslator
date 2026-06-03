namespace NativeOverlayTranslator.Models;

public sealed class AppSettings
{
    public string? LastTargetProcessPath { get; set; }
    public string UiLanguage { get; set; } = "en-US";
    public string TranslationEndpoint { get; set; } = "http://127.0.0.1:5001/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemma-4-31B";
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "Chinese";
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.Tesseract;
    public string TesseractPath { get; set; } = @"D:\Program Files\Tesseract-OCR\tesseract.exe";
    public string OcrLanguages { get; set; } = "jpn+eng+chi_sim";
    public int OcrPageSegmentationMode { get; set; } = 11;
    public string PaddleOcrEndpoint { get; set; } = "http://127.0.0.1:8868/ocr";
    public string? LocalTranslationModelPath { get; set; }
    public string? LocalVisionModelPath { get; set; }
    public bool ClipboardDoubleCopyEnabled { get; set; } = true;
    public double ClipboardDisplaySeconds { get; set; } = 6;
    public bool HoverTranslateEnabled { get; set; }
    public HoverMode HoverMode { get; set; } = HoverMode.Phrase;
    public bool HoverTooltipTranslateEnabled { get; set; }
    public double HoverWordDisplaySeconds { get; set; } = 0.5;
    public double HoverPhraseDisplaySeconds { get; set; } = 2;
    public double HoverSentenceDisplaySeconds { get; set; } = 3;
    public bool OcrDebugEnabled { get; set; }
    public Dictionary<string, string> Hotkeys { get; set; } = CreateDefaultHotkeys();

    public void EnsureDefaults()
    {
        Hotkeys ??= [];
        if (string.Equals(TranslationEndpoint, "http://localhost:11434/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            TranslationEndpoint = "http://127.0.0.1:5001/v1/chat/completions";
        }

        if (string.Equals(Model, "HY-MT1.5-7B-Q8_0.gguf", StringComparison.OrdinalIgnoreCase))
        {
            Model = "gemma-4-31B";
        }

        if (OcrPageSegmentationMode == 0)
        {
            OcrPageSegmentationMode = 11;
        }

        foreach (var pair in CreateDefaultHotkeys())
        {
            Hotkeys.TryAdd(pair.Key, pair.Value);
        }
    }

    public static Dictionary<string, string> CreateDefaultHotkeys()
    {
        return new Dictionary<string, string>
        {
            [HotkeyAction.TogglePanel.ToString()] = "Ctrl+Alt+Space",
            [HotkeyAction.FullOcr.ToString()] = "Ctrl+Alt+F",
            [HotkeyAction.RegionOcr.ToString()] = "Ctrl+Alt+R",
            [HotkeyAction.ScreenshotTranslate.ToString()] = "Ctrl+Alt+S",
            [HotkeyAction.ManualOverlay.ToString()] = "Ctrl+Alt+M",
            [HotkeyAction.EditOverlays.ToString()] = "Ctrl+Alt+E",
            [HotkeyAction.LockOverlays.ToString()] = "Ctrl+Alt+L",
            [HotkeyAction.ClearOverlays.ToString()] = "",
            [HotkeyAction.ToggleHoverTranslate.ToString()] = "Ctrl+Alt+H",
            [HotkeyAction.ToggleClipboardDoubleCopy.ToString()] = "Ctrl+Alt+C"
        };
    }
}
