using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class LocalizationService
{
    public const string English = "en-US";
    public const string Chinese = "zh-CN";

    private static readonly Dictionary<string, Dictionary<string, string>> Resources = new()
    {
        [English] = new Dictionary<string, string>
        {
            ["LanguageEnglish"] = "English",
            ["LanguageChinese"] = "简体中文",
            ["RefreshWindows"] = "Refresh windows",
            ["HidePanel"] = "Hide panel",
            ["Subtitle"] = "Configure every shortcut below. Leave a hotkey blank to disable it.",
            ["UiLanguage"] = "UI language",
            ["TargetWindow"] = "Target window",
            ["FullOcr"] = "Full OCR",
            ["RegionOcr"] = "Region OCR",
            ["Screenshot"] = "Screenshot",
            ["NewOverlay"] = "New overlay",
            ["TestImage"] = "Test 1.png",
            ["EnableHover"] = "Enable hover translate",
            ["HoverMode"] = "Hover mode",
            ["HoverOcrEngine"] = "Hover OCR engine",
            ["HoverWord"] = "Word",
            ["HoverPhrase"] = "Phrase",
            ["HoverSentence"] = "Sentence",
            ["HoverDisplaySeconds"] = "Display seconds",
            ["HoverTooltip"] = "Translate hover popups/tooltips",
            ["EnableClipboard"] = "Enable Ctrl+C twice translate",
            ["ClipboardDisplaySeconds"] = "Ctrl+C display seconds",
            ["OcrDebug"] = "OCR debug: show recognized text only",
            ["TranslationApi"] = "Translation API",
            ["Endpoint"] = "Endpoint",
            ["Model"] = "Model",
            ["ApiKey"] = "API Key",
            ["SourceLanguage"] = "Source language",
            ["TargetLanguage"] = "Target language",
            ["OcrEngine"] = "OCR engine",
            ["TesseractPath"] = "Tesseract path",
            ["OcrLanguages"] = "OCR languages",
            ["PaddleOcrEndpoint"] = "PaddleOCR endpoint",
            ["RapidOcrEndpoint"] = "RapidOCR endpoint",
            ["MangaOcrEndpoint"] = "MangaOCR endpoint",
            ["Hotkeys"] = "Hotkeys",
            ["HotkeyHelp"] = "Format: Ctrl+Alt+Space, Ctrl+Shift+F8, Alt+Q. Blank disables a shortcut.",
            ["SaveSettings"] = "Save settings",
            ["OverlayHelp"] = "Overlay edit: Alt + left mouse drag. Locked overlays click through to the target app.",
            ["EditOverlays"] = "Edit overlays",
            ["LockOverlays"] = "Lock overlays",
            ["ClearOverlays"] = "Clear overlays",
            ["TranslationsOverlays"] = "Translations and overlays",
            ["ColumnSource"] = "Source",
            ["ColumnOriginal"] = "Original",
            ["ColumnTranslation"] = "Translation",
            ["ColumnUpdated"] = "Updated",
            ["Ready"] = "Ready.",
            ["BuildInfo"] = "Current build: Tesseract OCR, configurable hotkeys, editable overlays, per-app translation memory, OpenAI-compatible translation API.",
            ["ShowPanel"] = "Show panel",
            ["Exit"] = "Exit",
            ["NoTarget"] = "No target window selected.",
            ["TargetBound"] = "Target bound: {0}",
            ["Recognizing"] = "{0}: recognizing...",
            ["Completed"] = "{0}: completed, {1} line(s).",
            ["Failed"] = "{0}: failed - {1}",
            ["DoubleCopyDetected"] = "Double-copy detected. Translating clipboard text...",
            ["ClipboardCompleted"] = "Clipboard translation completed.",
            ["OverlayEditEnabled"] = "Overlay edit mode enabled. Use Alt + left mouse drag.",
            ["OverlayLocked"] = "Overlays locked and click-through.",
            ["OverlaysCleared"] = "Current target overlays cleared.",
            ["SettingsSaved"] = "Settings saved.",
            ["SettingsHotkeysSaved"] = "Settings saved. Hotkeys registered.",
            ["HotkeyIssue"] = "Hotkey issue: {0}",
            ["HoverEnabled"] = "Hover translate enabled.",
            ["HoverDisabled"] = "Hover translate disabled.",
            ["ClipboardEnabled"] = "Double-copy translate enabled.",
            ["ClipboardDisabled"] = "Double-copy translate disabled."
        },
        [Chinese] = new Dictionary<string, string>
        {
            ["LanguageEnglish"] = "English",
            ["LanguageChinese"] = "简体中文",
            ["RefreshWindows"] = "刷新窗口",
            ["HidePanel"] = "隐藏面板",
            ["Subtitle"] = "下面可以配置每个快捷键，留空表示禁用。",
            ["UiLanguage"] = "界面语言",
            ["TargetWindow"] = "目标窗口",
            ["FullOcr"] = "全图 OCR",
            ["RegionOcr"] = "区域 OCR",
            ["Screenshot"] = "截图翻译",
            ["NewOverlay"] = "新建覆盖层",
            ["TestImage"] = "测试 1.png",
            ["EnableHover"] = "启用悬停翻译",
            ["HoverMode"] = "悬停模式",
            ["HoverWord"] = "单词",
            ["HoverPhrase"] = "短句",
            ["HoverSentence"] = "整句",
            ["HoverDisplaySeconds"] = "显示秒数",
            ["HoverTooltip"] = "翻译悬停弹出说明",
            ["EnableClipboard"] = "启用 Ctrl+C 两次翻译",
            ["ClipboardDisplaySeconds"] = "Ctrl+C 显示秒数",
            ["OcrDebug"] = "OCR 调试：只显示识别文本",
            ["TranslationApi"] = "翻译接口",
            ["Endpoint"] = "接口地址",
            ["Model"] = "模型",
            ["ApiKey"] = "API Key",
            ["SourceLanguage"] = "源语言",
            ["TargetLanguage"] = "目标语言",
            ["TesseractPath"] = "Tesseract 路径",
            ["OcrLanguages"] = "OCR 语言",
            ["Hotkeys"] = "快捷键",
            ["HotkeyHelp"] = "格式示例：Ctrl+Alt+Space、Ctrl+Shift+F8、Alt+Q。留空表示禁用。",
            ["SaveSettings"] = "保存设置",
            ["OverlayHelp"] = "覆盖层编辑：Alt + 鼠标左键拖动。锁定后点击穿透，不影响目标程序。",
            ["EditOverlays"] = "编辑覆盖层",
            ["LockOverlays"] = "锁定覆盖层",
            ["ClearOverlays"] = "清空覆盖层",
            ["TranslationsOverlays"] = "翻译与覆盖层",
            ["ColumnSource"] = "来源",
            ["ColumnOriginal"] = "原文",
            ["ColumnTranslation"] = "译文",
            ["ColumnUpdated"] = "更新时间",
            ["Ready"] = "准备就绪。",
            ["BuildInfo"] = "当前版本：Tesseract OCR、可配置快捷键、可编辑覆盖层、按软件保存翻译记忆、OpenAI-compatible 翻译接口。",
            ["ShowPanel"] = "显示面板",
            ["Exit"] = "退出",
            ["NoTarget"] = "未选择目标窗口。",
            ["TargetBound"] = "已绑定目标：{0}",
            ["Recognizing"] = "{0}：正在识别...",
            ["Completed"] = "{0}：完成，{1} 行。",
            ["Failed"] = "{0}：失败 - {1}",
            ["DoubleCopyDetected"] = "检测到 Ctrl+C 两次，正在翻译剪贴板文本...",
            ["ClipboardCompleted"] = "剪贴板翻译完成。",
            ["OverlayEditEnabled"] = "覆盖层已进入编辑模式。使用 Alt + 鼠标左键拖动。",
            ["OverlayLocked"] = "覆盖层已锁定并点击穿透。",
            ["OverlaysCleared"] = "当前目标覆盖层已清空。",
            ["SettingsSaved"] = "设置已保存。",
            ["SettingsHotkeysSaved"] = "设置已保存，快捷键已注册。",
            ["HotkeyIssue"] = "快捷键问题：{0}",
            ["HoverEnabled"] = "悬停翻译已启用。",
            ["HoverDisabled"] = "悬停翻译已禁用。",
            ["ClipboardEnabled"] = "Ctrl+C 两次翻译已启用。",
            ["ClipboardDisabled"] = "Ctrl+C 两次翻译已禁用。"
        }
    };

    private string _language;

    public LocalizationService(string language)
    {
        _language = Normalize(language);
    }

    public string Language
    {
        get => _language;
        set => _language = Normalize(value);
    }

    public IReadOnlyList<LanguageOption> GetLanguages()
    {
        return
        [
            new LanguageOption(English, T("LanguageEnglish")),
            new LanguageOption(Chinese, T("LanguageChinese"))
        ];
    }

    public string T(string key)
    {
        if (Resources.TryGetValue(_language, out var localized) && localized.TryGetValue(key, out var value))
        {
            return value;
        }

        return Resources[English].TryGetValue(key, out var fallback) ? fallback : key;
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }

    public static string Normalize(string? language)
    {
        return string.Equals(language, Chinese, StringComparison.OrdinalIgnoreCase) ? Chinese : English;
    }
}

public sealed record LanguageOption(string Code, string Name);
