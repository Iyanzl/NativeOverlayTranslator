# Native Overlay Translator

Windows 本地通用翻译层 MVP。目标是先把“覆盖层、目标进程、翻译接口、手工校准、记忆恢复”做稳，再接真实 OCR 与 Hook。

## Current Scope

- WPF/.NET 9 桌面程序。
- `Ctrl+Alt+Space` 呼出/隐藏主面板。
- `Ctrl+Alt+S` 触发截图/区域翻译入口。
- 每个主要功能都支持在设置面板里修改快捷键，留空即可禁用。
- UI language supports English and Simplified Chinese.
- `Test 1.png` opens the bundled image in a dedicated translation test window. Overlays are attached to the image canvas, so moving the image window moves the translated text with it.
- 选择当前已启动的窗口/进程，并按进程保存覆盖层。
- OpenAI-compatible 翻译接口，默认指向 Ollama/llama.cpp/LM Studio 常见的 `http://localhost:11434/v1/chat/completions`。
- Tesseract OCR，默认路径 `D:\Program Files\Tesseract-OCR\tesseract.exe`。
- OCR 默认语言 `jpn+eng+chi_sim`。
- `Ctrl+C` 两次触发剪贴板翻译。
- 可创建覆盖层，覆盖层支持直接编辑译文、保存、恢复。
- 翻译记忆：手动修正后的译文会按目标软件保存，同原文优先复用。
- 覆盖层编辑模式支持 `Alt + 左键` 拖动；锁定后点击穿透。
- OCR 已接 Tesseract 截屏识别；Hook 已预留接口。

## Architecture

- `Models/TargetWindowInfo.cs`: 当前可绑定的窗口/进程。
- `Models/OverlayEntry.cs`: 覆盖层保存单位，包含原文、译文、窗口、位置、锁定状态。
- `Services/WindowDiscoveryService.cs`: Win32 顶层窗口枚举。
- `Services/ITextCaptureService.cs`: OCR/截图/区域识别接口。
- `Services/TesseractOcrService.cs`: 截屏/图片 OCR、调用 Tesseract、解析 TSV 行框。
- `ImageTranslationWindow.xaml`: Image-bound overlay test window for `1.png`.
- `Services/IHookTextSource.cs`: 后续接 LunaTranslator 类 Hook、Textractor 类 Hook 或自研注入模块的接口。
- `Services/ITranslationService.cs`: 翻译引擎接口。
- `Services/OpenAiCompatibleTranslationService.cs`: Ollama、LM Studio、OpenAI-compatible API 适配。
- `Services/SettingsStore.cs`: 设置与覆盖层 JSON 持久化。
- `Services/TranslationMemoryStore.cs`: 按软件保存人工校准译文。
- `Services/HotkeyService.cs`: 配置驱动的全局快捷键注册与分发。
- `Services/LocalizationService.cs`: English / Simplified Chinese UI text.

## Hotkeys

Default hotkeys:

- Show or hide panel: `Ctrl+Alt+Space`
- Full-window OCR: `Ctrl+Alt+F`
- Region OCR: `Ctrl+Alt+R`
- Screenshot translate: `Ctrl+Alt+S`
- New manual overlay: `Ctrl+Alt+M`
- Edit overlays: `Ctrl+Alt+E`
- Lock overlays: `Ctrl+Alt+L`
- Toggle hover translate: `Ctrl+Alt+H`
- Toggle double-copy translate: `Ctrl+Alt+C`
- Clear current overlays: disabled by default

Supported format examples: `Ctrl+Alt+Space`, `Ctrl+Shift+F8`, `Alt+Q`. Leave a field blank to disable that shortcut.
- `OverlayWindow.xaml`: 透明置顶覆盖层。

## Next Implementation Steps

1. 覆盖层锚点系统：窗口相对坐标、DPI、附近图像 hash、OCR 原文 hash。
2. OCR 引擎抽象扩展：Windows OCR、PaddleOCR、LunaOCR、视觉模型 OCR。
3. Hook 模块：先做外部接口和进程安全策略，再做具体引擎。
4. 配置 profile：按软件保存 OCR 区域、覆盖层样式、术语表、引擎选择。
5. 本地模型管理器：可选启动 llama.cpp server，并将 GGUF 模型暴露为 OpenAI-compatible API。

## Local Models

程序当前不直接加载 GGUF。建议先用 llama.cpp、LM Studio 或 Ollama 把翻译模型暴露为 OpenAI-compatible API，然后在主面板设置：

- Endpoint: `http://localhost:11434/v1/chat/completions` 或你的本地服务地址。
- Model: `HY-MT1.5-7B-Q8_0.gguf`，或本地服务显示的模型名。

`gemma-4-31B-it-UD-Q8_K_XL.gguf` 更适合后续做视觉 OCR/截图理解入口；当前第一版 OCR 走 Tesseract。

## Build

```powershell
dotnet build
```

## Run

```powershell
dotnet run
```
