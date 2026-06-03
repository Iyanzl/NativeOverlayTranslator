using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using NativeOverlayTranslator.Models;
using NativeOverlayTranslator.Services;
using Clipboard = System.Windows.Clipboard;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace NativeOverlayTranslator;

public partial class MainWindow : Window
{
    private readonly WindowDiscoveryService _windowDiscovery = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly TranslationMemoryStore _memoryStore = new();
    private readonly ObservableCollection<OverlayEntry> _entries = [];
    private readonly ObservableCollection<HotkeyEditorItem> _hotkeyEditors = [];
    private readonly List<OverlayWindow> _overlayWindows = [];
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _overlayFollowTimer;
    private readonly LocalizationService _localizer;
    private TesseractOcrService _ocrService;
    private AppSettings _settings;
    private ITranslationService _translator;
    private HotkeyService? _hotkeys;
    private ClipboardDoubleCopyWatcher? _clipboardWatcher;
    private Forms.NotifyIcon? _notifyIcon;
    private TargetWindowInfo? _selectedTarget;
    private CancellationTokenSource? _hoverCts;
    private bool _hoverBusy;
    private bool _initializingUi;
    private OverlayWindow? _hoverOverlay;
    private readonly List<HoverOverlayState> _hoverOverlays = [];
    private readonly List<OverlayWindow> _temporaryOverlayWindows = [];
    private string? _lastHoverText;
    private Rect _lastHoverBounds;
    private DateTimeOffset _lastHoverAt;
    private System.Drawing.Point? _lastHoverPoint;
    private string? _pendingHoverText;
    private Rect _pendingHoverBounds;
    private int _pendingHoverStableCount;
    private bool _hoverSuspended;
    private CancellationTokenSource? _ocrCts;
    private bool _fullOcrRunning;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.LoadSettings();
        _localizer = new LocalizationService(_settings.UiLanguage);
        _translator = new OpenAiCompatibleTranslationService(_settings);
        _ocrService = new TesseractOcrService(_settings);
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _hoverTimer.Tick += HoverTimer_OnTick;
        _overlayFollowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _overlayFollowTimer.Tick += (_, _) => UpdateAnchoredOverlays();
        OverlayList.ItemsSource = _entries;
        HotkeyList.ItemsSource = _hotkeyEditors;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _initializingUi = true;
        EndpointBox.Text = _settings.TranslationEndpoint;
        ModelBox.Text = _settings.Model;
        ApiKeyBox.Password = _settings.ApiKey;
        SourceLanguageCombo.ItemsSource = GetSourceLanguageOptions();
        SourceLanguageCombo.SelectedValue = NormalizeSourceLanguage(_settings.SourceLanguage);
        TargetLanguageBox.Text = _settings.TargetLanguage;
        _settings.OcrLanguages = GetOcrLanguagesForSource(NormalizeSourceLanguage(_settings.SourceLanguage));
        TesseractPathBox.Text = _settings.TesseractPath;
        OcrLanguagesBox.Text = _settings.OcrLanguages;
        ClipboardToggle.IsChecked = _settings.ClipboardDoubleCopyEnabled;
        ClipboardDisplaySecondsBox.Text = ClampSeconds(_settings.ClipboardDisplaySeconds, 6).ToString("0.##");
        HoverToggle.IsChecked = _settings.HoverTranslateEnabled;
        HoverModeCombo.ItemsSource = GetHoverModeOptions();
        HoverModeCombo.SelectedValue = _settings.HoverMode;
        HoverDisplaySecondsBox.Text = GetHoverDisplaySeconds(_settings.HoverMode).ToString("0.##");
        HoverTooltipToggle.IsChecked = _settings.HoverTooltipTranslateEnabled;
        OcrDebugToggle.IsChecked = _settings.OcrDebugEnabled;
        UiLanguageCombo.ItemsSource = _localizer.GetLanguages();
        UiLanguageCombo.SelectedValue = LocalizationService.Normalize(_settings.UiLanguage);
        LoadHotkeyEditors();
        ApplyLocalization();

        _hotkeys = new HotkeyService(this);
        _hotkeys.ActionRequested += (_, action) => ExecuteHotkeyAction(action);
        ShowHotkeyRegistrationStatus(_hotkeys.Start(_settings), initialLoad: true);

        _clipboardWatcher = new ClipboardDoubleCopyWatcher(this);
        _clipboardWatcher.DoubleCopied += async (_, text) => await TranslateClipboardAsync(text);
        _clipboardWatcher.Start();

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Native Overlay Translator",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        UpdateNotifyIconMenu();
        _notifyIcon.DoubleClick += (_, _) => ShowPanel();

        RefreshWindows();
        ApplyHoverState();
        _overlayFollowTimer.Start();
        _initializingUi = false;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveCurrentOverlaySet();
        _hotkeys?.Dispose();
        _clipboardWatcher?.Dispose();
        _notifyIcon?.Dispose();
        _overlayFollowTimer.Stop();
        _hoverCts?.Dispose();
        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _hoverOverlay?.Close();
        foreach (var hover in _hoverOverlays.ToList())
        {
            hover.Window.Close();
        }

        foreach (var temporary in _temporaryOverlayWindows.ToList())
        {
            temporary.Close();
        }

        foreach (var window in _overlayWindows.ToList())
        {
            window.Close();
        }
    }

    private void RefreshWindows_OnClick(object sender, RoutedEventArgs e) => RefreshWindows();

    private void RefreshWindows()
    {
        var windows = _windowDiscovery.GetTopLevelWindows();
        WindowCombo.ItemsSource = windows;
        Diagnostics.Log($"RefreshWindows count={windows.Count} lastTarget='{_settings.LastTargetProcessPath}'");

        var foreground = _windowDiscovery.GetForegroundWindow();
        var selected = windows.FirstOrDefault(window =>
            !string.IsNullOrWhiteSpace(_settings.LastTargetProcessPath) &&
            string.Equals(window.ProcessPath, _settings.LastTargetProcessPath, StringComparison.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault(window => foreground is not null && window.Handle == foreground.Handle)
            ?? windows.FirstOrDefault();

        WindowCombo.SelectedItem = selected;
        SetTarget(selected);
    }

    private void WindowCombo_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SetTarget(WindowCombo.SelectedItem as TargetWindowInfo);
    }

    private void SetTarget(TargetWindowInfo? target)
    {
        SaveCurrentOverlaySet();
        ClearOverlayWindows();
        _entries.Clear();
        _selectedTarget = target;

        if (target is null)
        {
            Diagnostics.Log("SetTarget null");
            SelectedWindowText.Text = _localizer.T("NoTarget");
            return;
        }

        Diagnostics.Log($"SetTarget process='{target.ProcessName}' pid={target.ProcessId} handle={target.Handle} title='{target.Title}' path='{target.ProcessPath}'");
        _settings.LastTargetProcessPath = target.ProcessPath;
        _settingsStore.SaveSettings(_settings);
        SelectedWindowText.Text = $"{target.ProcessName} | pid {target.ProcessId}\n{target.Title}\n{target.ProcessPath}";

        var processKey = SettingsStore.BuildProcessKey(target);
        foreach (var entry in _settingsStore.LoadOverlays(processKey))
        {
            AddOverlay(entry, showWindow: true);
        }

        StatusText.Text = _localizer.Format("TargetBound", target);
    }

    private async void FullOcr_OnClick(object sender, RoutedEventArgs e) => await RunFullOcrAsync();

    private async void RegionOcr_OnClick(object sender, RoutedEventArgs e) => await RunRegionOcrAsync();

    private async void ScreenshotTranslate_OnClick(object sender, RoutedEventArgs e) => await RunScreenshotTranslateAsync();

    private async void ManualOverlay_OnClick(object sender, RoutedEventArgs e) => await CreateManualOverlayAsync();

    private void TestImage_OnClick(object sender, RoutedEventArgs e)
    {
        var imagePath = Path.Combine(AppContext.BaseDirectory, "1.png");
        if (!File.Exists(imagePath))
        {
            imagePath = Path.Combine(Environment.CurrentDirectory, "1.png");
        }

        if (!File.Exists(imagePath))
        {
            imagePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "1.png");
        }

        imagePath = Path.GetFullPath(imagePath);
        if (!File.Exists(imagePath))
        {
            MessageBox.Show("1.png was not found in the project folder.", "Image test", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new ImageTranslationWindow(
            imagePath,
            new TesseractOcrService(_settings),
            _translator,
            _memoryStore,
            _settings)
        {
            Owner = this
        };
        window.Show();
    }

    private Task RunFullOcrAsync()
    {
        if (_fullOcrRunning)
        {
            _ocrCts?.Cancel();
            StatusText.Text = "Stopping full OCR...";
            return Task.CompletedTask;
        }

        return RunOcrAndOverlayAsync(_localizer.T("FullOcr"), null);
    }

    private async Task RunRegionOcrAsync()
    {
        var region = SelectRegion();
        if (region is not null)
        {
            await RunOcrAndOverlayAsync(_localizer.T("RegionOcr"), region);
        }
    }

    private async Task RunScreenshotTranslateAsync()
    {
        var region = SelectRegion();
        if (region is not null)
        {
            await OpenScreenshotImageTranslationAsync(region.Value);
        }
    }

    private async Task OpenScreenshotImageTranslationAsync(Rect region)
    {
        var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        var imagePath = ScreenCaptureService.CaptureRegionToPng(region, screenshotDir, "screenshot");
        var imageLength = File.Exists(imagePath) ? new FileInfo(imagePath).Length : -1;
        Diagnostics.Log($"ScreenshotTranslate region={FormatRect(region)} path='{imagePath}' bytes={imageLength}");
        var window = new ImageTranslationWindow(
            imagePath,
            new TesseractOcrService(_settings),
            _translator,
            _memoryStore,
            _settings)
        {
            Owner = this
        };
        window.Show();
        StatusText.Text = $"Screenshot captured: {imagePath}";
        await Task.CompletedTask;
    }

    private async Task CreateManualOverlayAsync()
    {
        var source = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "Manual overlay";
        if (string.IsNullOrWhiteSpace(source))
        {
            source = "Manual overlay";
        }

        var translated = await TranslateWithMemoryAsync(source, CancellationToken.None);
        var point = Forms.Cursor.Position;
        AddOverlay(BuildEntry(source, translated, new Rect(point.X, point.Y, 360, 64)), showWindow: true);
        SaveCurrentOverlaySet();
    }

    private async void ExecuteHotkeyAction(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.TogglePanel:
                TogglePanel();
                break;
            case HotkeyAction.FullOcr:
                await RunFullOcrAsync();
                break;
            case HotkeyAction.RegionOcr:
                await RunRegionOcrAsync();
                break;
            case HotkeyAction.ScreenshotTranslate:
                await RunScreenshotTranslateAsync();
                break;
            case HotkeyAction.ManualOverlay:
                await CreateManualOverlayAsync();
                break;
            case HotkeyAction.EditOverlays:
                SetOverlayEditMode(true);
                break;
            case HotkeyAction.LockOverlays:
                SetOverlayEditMode(false);
                break;
            case HotkeyAction.ClearOverlays:
                ClearCurrentOverlays();
                break;
            case HotkeyAction.ToggleHoverTranslate:
                SetHoverTranslateEnabled(!_settings.HoverTranslateEnabled);
                break;
            case HotkeyAction.ToggleClipboardDoubleCopy:
                SetClipboardDoubleCopyEnabled(!_settings.ClipboardDoubleCopyEnabled);
                break;
        }
    }

    private async Task RunOcrAndOverlayAsync(string mode, Rect? region)
    {
        var isFullOcr = region is null;
        if (_ocrCts is not null)
        {
            _ocrCts.Cancel();
            _ocrCts.Dispose();
        }

        _ocrCts = new CancellationTokenSource();
        var localOcrCts = _ocrCts;
        var token = localOcrCts.Token;
        if (isFullOcr)
        {
            _fullOcrRunning = true;
        }

        try
        {
            _hoverSuspended = true;
            CleanupAllHoverOverlays();
            Diagnostics.Log($"RunOcr start mode='{mode}' region={FormatRect(region)} target='{_selectedTarget?.ProcessName}' handle={_selectedTarget?.Handle}");
            StatusText.Text = _localizer.Format("Recognizing", mode);
            var lines = await _ocrService.CaptureAsync(_selectedTarget, region, token);
            Diagnostics.Log($"RunOcr raw mode='{mode}' lines={lines.Count}");
            var filtered = lines
                .Where(line => line.Bounds.Width > 8 && line.Bounds.Height > 7 && line.Confidence >= 0.10)
                .Where(line => !IsAlreadyTranslatedOcrLine(line))
                .ToList();
            Diagnostics.Log($"RunOcr filtered mode='{mode}' lines={filtered.Count}");
            var sourceLanguage = NormalizeSourceLanguage(_settings.SourceLanguage);
            if (sourceLanguage != "auto")
            {
                filtered = filtered
                    .Where(line => TextTranslationFilter.ShouldTranslate(line.Text, _settings.SourceLanguage, _settings.TargetLanguage))
                    .ToList();
            }

            var failed = 0;
            foreach (var line in filtered)
            {
                token.ThrowIfCancellationRequested();
                Diagnostics.Log($"RunOcr line text='{line.Text}' conf={line.Confidence:0.00} bounds={FormatRect(line.Bounds)}");
                var entry = await BuildTranslatedOrDebugEntryAsync(line, token);
                if (entry is null)
                {
                    failed++;
                    continue;
                }

                AddOverlay(entry, showWindow: true);
            }

            SaveCurrentOverlaySet();
            StatusText.Text = failed == 0
                ? _localizer.Format("Completed", mode, filtered.Count)
                : $"{_localizer.Format("Completed", mode, filtered.Count - failed)} ({failed} translation failed/skipped)";
        }
        catch (OperationCanceledException)
        {
            Diagnostics.Log($"RunOcr canceled mode='{mode}'");
            StatusText.Text = $"{mode} stopped.";
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"RunOcr failed mode='{mode}' error='{ex}'");
            StatusText.Text = _localizer.Format("Failed", mode, ex.Message);
            MessageBox.Show(ex.Message, _localizer.Format("Failed", mode, ""), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(_ocrCts, localOcrCts))
            {
                localOcrCts.Dispose();
                _ocrCts = null;
            }

            if (isFullOcr)
            {
                _fullOcrRunning = false;
            }

            _hoverSuspended = false;
            ResetPendingHover();
        }
    }

    private async Task TranslateClipboardAsync(string text)
    {
        if (!_settings.ClipboardDoubleCopyEnabled)
        {
            return;
        }

        StatusText.Text = _localizer.T("DoubleCopyDetected");
        var translated = await TranslateWithMemoryAsync(text, CancellationToken.None);
        var point = Forms.Cursor.Position;
        var entry = BuildEntry(text, translated, BuildClipboardBounds(point.X, point.Y, translated));
        entry.FontSize = 20;
        ShowTemporaryOverlay(entry, TimeSpan.FromSeconds(ClampSeconds(_settings.ClipboardDisplaySeconds, 6)));
        StatusText.Text = _localizer.T("ClipboardCompleted");
    }

    private bool IsAlreadyTranslatedOcrLine(OcrTextLine line)
    {
        return _entries.Any(entry =>
            !entry.SourceBounds.IsEmpty &&
            RectOverlapRatio(entry.SourceBounds, line.Bounds) >= 0.55);
    }

    private static double RectOverlapRatio(Rect a, Rect b)
    {
        a.Intersect(b);
        if (a.IsEmpty || b.Width <= 0 || b.Height <= 0)
        {
            return 0;
        }

        return (a.Width * a.Height) / (b.Width * b.Height);
    }

    private void ShowTemporaryOverlay(OverlayEntry entry, TimeSpan duration)
    {
        entry.IsLocked = true;
        var overlay = new OverlayWindow(entry);
        overlay.SetEditMode(false);
        overlay.Closed += (_, _) => _temporaryOverlayWindows.Remove(overlay);
        _temporaryOverlayWindows.Add(overlay);
        overlay.Show();

        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            overlay.Close();
        };
        timer.Start();
    }

    private static Rect BuildClipboardBounds(int mouseX, int mouseY, string translatedText)
    {
        var screen = Forms.SystemInformation.VirtualScreen;
        var normalizedLength = Math.Max(1, TextTranslationFilter.Normalize(translatedText).Length);
        var maxWidth = Math.Min(980, screen.Width - 48);
        var width = Math.Clamp(normalizedLength * 10 + 64, 340, maxWidth);
        var lineCount = Math.Max(1, Math.Ceiling((normalizedLength * 10.0) / Math.Max(1, width - 36)));
        var height = Math.Clamp(lineCount * 30 + 34, 72, Math.Min(420, screen.Height - 64));
        var x = Math.Clamp(mouseX + 18.0, screen.Left + 8, screen.Right - width - 8);
        var y = Math.Clamp(mouseY + 18.0, screen.Top + 8, screen.Bottom - height - 8);
        return new Rect(x, y, width, height);
    }

    private async void HoverTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_settings.HoverTranslateEnabled || _hoverBusy || _hoverSuspended)
        {
            return;
        }

        CleanupExpiredHoverOverlays();
        var point = Forms.Cursor.Position;
        if (HasActiveHoverOverlayNear(point))
        {
            return;
        }

        if (ShouldSkipStableHoverCapture(point))
        {
            return;
        }

        _hoverBusy = true;
        try
        {
            _hoverCts?.Dispose();
            _hoverCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var token = _hoverCts.Token;
            var region = GetHoverCaptureRegion(point.X, point.Y, _settings.HoverMode);
            Diagnostics.Log($"Hover capture point=({point.X},{point.Y}) mode={_settings.HoverMode} region={FormatRect(region)}");
            HideHoverOverlaysForCapture(region);
            await Task.Delay(35, token);

            var lines = await CaptureHoverTextAsync(region, token);
            Diagnostics.Log($"Hover raw lines={lines.Count}");
            RestoreHoverOverlaysAfterCapture();
            var line = PickHoverLine(lines, point.X, point.Y, _settings.SourceLanguage, _settings.TargetLanguage, _settings.HoverMode);
            if (line is null || token.IsCancellationRequested)
            {
                Diagnostics.Log("Hover picked null");
                return;
            }

            Diagnostics.Log($"Hover picked text='{line.Text}' conf={line.Confidence:0.00} bounds={FormatRect(line.Bounds)}");
            if (line.Confidence < 0.10 ||
                string.IsNullOrWhiteSpace(line.Text) ||
                !TextTranslationFilter.ShouldTranslate(line.Text, _settings.SourceLanguage, _settings.TargetLanguage))
            {
                return;
            }

            var normalizedText = TextTranslationFilter.Normalize(line.Text);
            if (!UpdateHoverStability(normalizedText, line.Bounds, _settings.HoverMode))
            {
                return;
            }

            if (AreClose(_lastHoverBounds, line.Bounds) &&
                DateTimeOffset.Now - _lastHoverAt < TimeSpan.FromSeconds(30))
            {
                Diagnostics.Log($"Hover skipped repeated bounds text='{normalizedText}' lastText='{_lastHoverText}'");
                return;
            }

            _lastHoverText = normalizedText;
            _lastHoverBounds = line.Bounds;
            _lastHoverAt = DateTimeOffset.Now;

            var translated = _settings.OcrDebugEnabled ? BuildOcrDebugText(line) : await TranslateWithMemoryAsync(line.Text, CancellationToken.None);
            if (IsTranslationFailure(translated))
            {
                StatusText.Text = translated;
                return;
            }

            var displayMode = _settings.HoverMode;
            if (_settings.HoverTooltipTranslateEnabled && !_settings.OcrDebugEnabled)
            {
                var withTooltip = await AppendTooltipTranslationAsync(translated, line, point.X, point.Y, CancellationToken.None);
                if (!string.Equals(withTooltip, translated, StringComparison.Ordinal))
                {
                    translated = withTooltip;
                    displayMode = HoverMode.Sentence;
                }
            }

            var entry = BuildHoverEntry(line.Text, translated, point.X, point.Y, displayMode);
            entry.IsLocked = true;
            Diagnostics.Log($"Hover show text='{translated}' bounds={FormatRect(entry.Bounds)} duration={GetHoverDisplayDurationForSettings(displayMode).TotalSeconds:0.##}");
            ShowHoverOverlay(entry, GetHoverDisplayDurationForSettings(displayMode));
            _lastHoverPoint = point;
        }
        catch (OperationCanceledException)
        {
            ResetPendingHover();
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Hover failed error='{ex}'");
            StatusText.Text = $"Hover failed: {ex.Message}";
        }
        finally
        {
            RestoreHoverOverlaysAfterCapture();
            _hoverBusy = false;
        }
    }

    private async Task<IReadOnlyList<OcrTextLine>> CaptureHoverTextAsync(Rect region, CancellationToken cancellationToken)
    {
        if (_settings.HoverMode == HoverMode.Word)
        {
            return await _ocrService.CaptureWordsAsync(_selectedTarget, region, cancellationToken);
        }

        return await _ocrService.CaptureAsync(_selectedTarget, region, cancellationToken);
    }

    private static TimeSpan GetHoverDisplayDuration(HoverMode mode)
    {
        return TimeSpan.FromSeconds(mode switch
        {
            HoverMode.Word => 0.5,
            HoverMode.Phrase => 2,
            HoverMode.Sentence => 3,
            _ => 2
        });
    }

    private double GetHoverDisplaySeconds(HoverMode mode)
    {
        return mode switch
        {
            HoverMode.Word => ClampHoverSeconds(_settings.HoverWordDisplaySeconds, 0.2),
            HoverMode.Phrase => ClampHoverSeconds(_settings.HoverPhraseDisplaySeconds, 0.2),
            HoverMode.Sentence => ClampHoverSeconds(_settings.HoverSentenceDisplaySeconds, 0.2),
            _ => 2
        };
    }

    private TimeSpan GetHoverDisplayDurationForSettings(HoverMode mode)
    {
        return TimeSpan.FromSeconds(GetHoverDisplaySeconds(mode));
    }

    private OverlayEntry BuildHoverEntry(string source, string translated, int mouseX, int mouseY, HoverMode mode)
    {
        var bounds = AvoidHoverOverlap(BuildHoverPopupBounds(mouseX, mouseY, translated, mode));
        return new OverlayEntry
        {
            ProcessName = _selectedTarget?.ProcessName ?? "global",
            ProcessPath = _selectedTarget?.ProcessPath ?? "",
            WindowTitle = _selectedTarget?.Title ?? "",
            SourceText = source,
            TranslatedText = translated,
            Bounds = bounds,
            SourceBounds = bounds,
            IsTargetAnchored = false,
            FontSize = mode == HoverMode.Word ? 16 : mode == HoverMode.Phrase ? 15 : 14,
            IsLocked = false
        };
    }

    private async Task<string> AppendTooltipTranslationAsync(string currentTranslation, OcrTextLine baseLine, int mouseX, int mouseY, CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        CleanupExpiredHoverOverlays();
        var tooltipRegion = GetTooltipCaptureRegion(mouseX, mouseY);
        HideHoverOverlaysForCapture(tooltipRegion);
        try
        {
            var lines = await _ocrService.CaptureAsync(_selectedTarget, tooltipRegion, cancellationToken);
            var tooltipText = BuildTooltipSourceText(lines, baseLine.Text);
            if (string.IsNullOrWhiteSpace(tooltipText))
            {
                return currentTranslation;
            }

            var translatedTooltip = await TranslateWithMemoryAsync(tooltipText, cancellationToken);
            if (IsTranslationFailure(translatedTooltip) || string.IsNullOrWhiteSpace(translatedTooltip))
            {
                return currentTranslation;
            }

            return $"{currentTranslation}{Environment.NewLine}{translatedTooltip}";
        }
        finally
        {
            RestoreHoverOverlaysAfterCapture();
        }
    }

    private static Rect GetTooltipCaptureRegion(int mouseX, int mouseY)
    {
        var screen = Forms.SystemInformation.VirtualScreen;
        var x = Math.Clamp(mouseX - 40, screen.Left, screen.Right - 1);
        var y = Math.Clamp(mouseY - 30, screen.Top, screen.Bottom - 1);
        var width = Math.Min(760, screen.Right - x);
        var height = Math.Min(420, screen.Bottom - y);
        return new Rect(x, y, Math.Max(80, width), Math.Max(80, height));
    }

    private static string BuildTooltipSourceText(IReadOnlyList<OcrTextLine> lines, string baseText)
    {
        var normalizedBase = TextTranslationFilter.Normalize(baseText);
        var selected = lines
            .Where(line => line.Bounds.Width > 20 && line.Bounds.Height > 8 && line.Confidence >= 0.10)
            .Select(line => TextTranslationFilter.Normalize(line.Text))
            .Where(text => text.Length >= 4)
            .Where(text => !string.Equals(text, normalizedBase, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return selected.Count == 0 ? "" : string.Join(Environment.NewLine, selected);
    }

    private void ShowHoverOverlay(OverlayEntry entry, TimeSpan duration)
    {
        CleanupExpiredHoverOverlays();
        foreach (var state in _hoverOverlays.Where(state => state.Window.Entry.Bounds.IntersectsWith(entry.Bounds)).ToList())
        {
            state.Window.Close();
            _hoverOverlays.Remove(state);
        }

        var overlay = new OverlayWindow(entry);
        overlay.SetEditMode(false);
        overlay.Show();
        _hoverOverlays.Add(new HoverOverlayState(overlay, DateTimeOffset.Now + duration));
        Diagnostics.Log($"Hover overlay created bounds={FormatRect(entry.Bounds)} visible={overlay.IsVisible}");
    }

    private void CleanupExpiredHoverOverlays()
    {
        var now = DateTimeOffset.Now;
        foreach (var state in _hoverOverlays.Where(state => state.ExpiresAt <= now || (!state.Window.IsVisible && !state.HiddenForCapture)).ToList())
        {
            state.Window.Close();
            _hoverOverlays.Remove(state);
        }
    }

    private void CleanupAllHoverOverlays()
    {
        foreach (var state in _hoverOverlays.ToList())
        {
            state.Window.Close();
        }

        _hoverOverlays.Clear();
    }

    private bool ShouldSkipStableHoverCapture(System.Drawing.Point point)
    {
        if (_lastHoverPoint is not { } previous)
        {
            return false;
        }

        var toleranceX = _settings.HoverMode == HoverMode.Word ? 8 : 18;
        var toleranceY = _settings.HoverMode == HoverMode.Word ? 8 : 14;
        var sameSpot = Math.Abs(point.X - previous.X) <= toleranceX && Math.Abs(point.Y - previous.Y) <= toleranceY;
        if (!sameSpot)
        {
            return false;
        }

        var quietPeriod = GetHoverDisplayDurationForSettings(_settings.HoverMode) + TimeSpan.FromMilliseconds(250);
        return DateTimeOffset.Now - _lastHoverAt < quietPeriod;
    }

    private bool HasActiveHoverOverlayNear(System.Drawing.Point point)
    {
        var now = DateTimeOffset.Now;
        return _hoverOverlays.Any(state =>
            state.ExpiresAt > now &&
            !state.HiddenForCapture &&
            IsPointNearHoverOverlay(point, state.Window.Entry.Bounds));
    }

    private static bool IsPointNearHoverOverlay(System.Drawing.Point point, Rect bounds)
    {
        var guard = new Rect(
            bounds.X - 120,
            bounds.Y - 160,
            bounds.Width + 180,
            bounds.Height + 220);
        return point.X >= guard.Left &&
               point.X <= guard.Right &&
               point.Y >= guard.Top &&
               point.Y <= guard.Bottom;
    }

    private void HideHoverOverlaysForCapture(Rect captureRegion)
    {
        foreach (var state in _hoverOverlays)
        {
            if (state.Window.IsVisible && state.Window.Entry.Bounds.IntersectsWith(captureRegion))
            {
                state.Window.Hide();
                state.HiddenForCapture = true;
            }
        }
    }

    private void RestoreHoverOverlaysAfterCapture()
    {
        CleanupExpiredHoverOverlays();
        foreach (var state in _hoverOverlays.Where(state => state.HiddenForCapture))
        {
            state.Window.Show();
            state.HiddenForCapture = false;
        }
    }

    private Rect AvoidHoverOverlap(Rect bounds)
    {
        var adjusted = bounds;
        var screen = Forms.SystemInformation.VirtualScreen;
        foreach (var existing in _hoverOverlays.Select(state => state.Window.Entry.Bounds))
        {
            if (!adjusted.IntersectsWith(existing))
            {
                continue;
            }

            adjusted.Y = existing.Bottom + 8;
            if (adjusted.Bottom > screen.Bottom - 8)
            {
                adjusted.Y = Math.Max(screen.Top + 8, existing.Top - adjusted.Height - 8);
            }
        }

        adjusted.X = Math.Clamp(adjusted.X, screen.Left + 8, screen.Right - adjusted.Width - 8);
        adjusted.Y = Math.Clamp(adjusted.Y, screen.Top + 8, screen.Bottom - adjusted.Height - 8);
        return adjusted;
    }

    private OverlayEntry BuildEntry(string source, string translated, Rect bounds)
    {
        var entry = new OverlayEntry
        {
            ProcessName = _selectedTarget?.ProcessName ?? "global",
            ProcessPath = _selectedTarget?.ProcessPath ?? "",
            WindowTitle = _selectedTarget?.Title ?? "",
            SourceText = source,
            TranslatedText = translated,
            Bounds = bounds,
            SourceBounds = bounds,
            IsLocked = false
        };

        ApplyTargetAnchor(entry);
        return entry;
    }

    private async Task<OverlayEntry?> BuildTranslatedOrDebugEntryAsync(OcrTextLine line, CancellationToken cancellationToken)
    {
        var text = _settings.OcrDebugEnabled
            ? BuildOcrDebugText(line)
            : await TranslateWithMemoryAsync(line.Text, cancellationToken);

        if (!_settings.OcrDebugEnabled && IsTranslationFailure(text))
        {
            StatusText.Text = text;
            return null;
        }

        var entry = BuildEntry(line.Text, text, EstimateOverlayBounds(line.Bounds, text));
        entry.SourceBounds = line.Bounds;
        entry.FontSize = EstimateOverlayFontSize(line.Bounds);
        entry.FontSizeIsPhysicalPixels = true;
        if (_settings.OcrDebugEnabled)
        {
            entry.BackgroundColor = "#CC102A43";
            entry.ForegroundColor = "#FFFFFFFF";
            entry.BorderColor = "#FF00A8FF";
            entry.IsLocked = false;
        }

        return entry;
    }

    private static Rect EstimateOverlayBounds(Rect sourceBounds, string translatedText)
    {
        var normalizedLength = Math.Max(1, TextTranslationFilter.Normalize(translatedText).Length);
        if (!IsCompactUiText(sourceBounds))
        {
            var rawTextWidth = normalizedLength * Math.Max(6, sourceBounds.Height * 0.40);
            var expandedWidth = Math.Clamp(
                rawTextWidth,
                Math.Max(48, sourceBounds.Width + 4),
                Math.Max(sourceBounds.Width + 4, Math.Min(620, sourceBounds.Width * 1.25 + 72)));
            var expandedLines = Math.Max(1, Math.Ceiling(rawTextWidth / Math.Max(1, expandedWidth - 8)));
            var expandedHeight = Math.Clamp(
                expandedLines * Math.Max(18, sourceBounds.Height + 6),
                Math.Max(22, sourceBounds.Height + 8),
                Math.Max(sourceBounds.Height + 4, 180));

            return new Rect(sourceBounds.X - 1, sourceBounds.Y - 1, expandedWidth, expandedHeight);
        }

        var preferredWidth = sourceBounds.Width <= 80
            ? sourceBounds.Width * 1.25 + 12
            : sourceBounds.Width * 1.12 + 10;
        var textDrivenWidth = normalizedLength * Math.Max(4.6, sourceBounds.Height * 0.30);
        var width = Math.Clamp(
            Math.Max(preferredWidth, Math.Min(textDrivenWidth, sourceBounds.Width * 1.22 + 18)),
            Math.Max(28, sourceBounds.Width + 4),
            Math.Max(sourceBounds.Width + 4, Math.Min(210, sourceBounds.Width * 1.28 + 22)));
        var lines = Math.Max(1, Math.Ceiling((normalizedLength * Math.Max(4.6, sourceBounds.Height * 0.30)) / Math.Max(1, width - 8)));
        var needsTwoLines = lines > 1 && sourceBounds.Width < 80;
        var height = Math.Clamp(
            needsTwoLines ? sourceBounds.Height * 2.05 + 8 : sourceBounds.Height + 7,
            Math.Max(22, sourceBounds.Height + 8),
            Math.Max(sourceBounds.Height + 4, Math.Min(52, sourceBounds.Height * 2.15 + 8)));

        return new Rect(sourceBounds.X - 1, sourceBounds.Y - 1, width, height);
    }

    private static bool IsCompactUiText(Rect bounds)
    {
        return bounds.Height <= 26 && bounds.Width <= 260;
    }

    private static double EstimateOverlayFontSize(Rect sourceBounds)
    {
        if (IsCompactUiText(sourceBounds))
        {
            return Math.Clamp(sourceBounds.Height * 0.90, 12, 16);
        }

        return Math.Clamp(sourceBounds.Height * 0.88, 12, 34);
    }

    private static string BuildOcrDebugText(OcrTextLine line)
    {
        return $"OCR {line.Confidence:P0}: {line.Text}";
    }

    private void AddOverlay(OverlayEntry entry, bool showWindow)
    {
        Diagnostics.Log($"AddOverlay show={showWindow} source='{entry.SourceText}' translated='{entry.TranslatedText}' bounds={FormatRect(entry.Bounds)} anchored={entry.IsTargetAnchored} anchor={FormatRect(entry.AnchorBounds)}");
        _entries.Add(entry);

        if (!showWindow)
        {
            return;
        }

        var overlay = new OverlayWindow(entry);
        overlay.EntryChanged += (_, _) =>
        {
            UpdateAnchorFromScreen(entry);
            OverlayList.Items.Refresh();
            _memoryStore.Remember(_selectedTarget, entry.SourceText, entry.TranslatedText);
            SaveCurrentOverlaySet();
        };
        _overlayWindows.Add(overlay);
        overlay.Show();
        Diagnostics.Log($"Overlay shown visible={overlay.IsVisible} left={overlay.Left:0.##} top={overlay.Top:0.##} width={overlay.Width:0.##} height={overlay.Height:0.##}");
    }

    private void SaveCurrentOverlaySet()
    {
        var key = SettingsStore.BuildProcessKey(_selectedTarget);
        _settingsStore.SaveOverlays(key, _entries);
        foreach (var entry in _entries)
        {
            _memoryStore.Remember(_selectedTarget, entry.SourceText, entry.TranslatedText);
        }
    }

    private async Task<string> TranslateWithMemoryAsync(string sourceText, CancellationToken cancellationToken)
    {
        var remembered = _memoryStore.TryGet(_selectedTarget, sourceText);
        if (!string.IsNullOrWhiteSpace(remembered))
        {
            return remembered;
        }

        var textForTranslation = PrepareTextForTranslation(sourceText);
        var translated = await _translator.TranslateAsync(textForTranslation, _settings.SourceLanguage, _settings.TargetLanguage, cancellationToken);
        if (!IsTranslationFailure(translated))
        {
            _memoryStore.Remember(_selectedTarget, sourceText, translated);
        }

        return translated;
    }

    private static string PrepareTextForTranslation(string sourceText)
    {
        var normalized = TextTranslationFilter.Normalize(sourceText);
        normalized = PascalCaseBoundaryRegex().Replace(normalized, "$1 $2");
        normalized = LetterDigitBoundaryRegex().Replace(normalized, "$1 $2");
        normalized = DigitLetterBoundaryRegex().Replace(normalized, "$1 $2");
        return normalized;
    }

    private static bool IsTranslationFailure(string text)
    {
        return text.StartsWith("[Translation failed:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[Translation canceled", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearOverlayWindows()
    {
        foreach (var window in _overlayWindows.ToList())
        {
            window.Close();
        }

        _overlayWindows.Clear();
    }

    private void ApplyTargetAnchor(OverlayEntry entry)
    {
        if (_selectedTarget is null || !NativeMethods.GetWindowRect(_selectedTarget.Handle, out var rect))
        {
            entry.IsTargetAnchored = false;
            return;
        }

        entry.IsTargetAnchored = true;
        entry.AnchorBounds = new Rect(
            entry.Bounds.X - rect.Left,
            entry.Bounds.Y - rect.Top,
            entry.Bounds.Width,
            entry.Bounds.Height);
    }

    private void UpdateAnchorFromScreen(OverlayEntry entry)
    {
        if (!entry.IsTargetAnchored || _selectedTarget is null || !NativeMethods.GetWindowRect(_selectedTarget.Handle, out var rect))
        {
            return;
        }

        entry.AnchorBounds = new Rect(
            entry.Bounds.X - rect.Left,
            entry.Bounds.Y - rect.Top,
            entry.Bounds.Width,
            entry.Bounds.Height);
    }

    private void UpdateAnchoredOverlays()
    {
        if (_selectedTarget is null || !NativeMethods.GetWindowRect(_selectedTarget.Handle, out var rect))
        {
            return;
        }

        foreach (var overlay in _overlayWindows)
        {
            var entry = overlay.Entry;
            if (!entry.IsTargetAnchored)
            {
                continue;
            }

            if (!overlay.IsVisible)
            {
                overlay.Show();
            }

            overlay.SetScreenBounds(new Rect(
                rect.Left + entry.AnchorBounds.X,
                rect.Top + entry.AnchorBounds.Y,
                entry.AnchorBounds.Width,
                entry.AnchorBounds.Height));
        }

        if (_hoverOverlay?.Entry is { IsTargetAnchored: true } hoverEntry)
        {
            _hoverOverlay.SetScreenBounds(new Rect(
                rect.Left + hoverEntry.AnchorBounds.X,
                rect.Top + hoverEntry.AnchorBounds.Y,
                hoverEntry.AnchorBounds.Width,
                hoverEntry.AnchorBounds.Height));
        }
    }

    private bool UpdateHoverStability(string text, Rect bounds, HoverMode mode)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ResetPendingHover();
            return false;
        }

        var sameText = string.Equals(_pendingHoverText, text, StringComparison.Ordinal);
        var samePosition = sameText && AreClose(_pendingHoverBounds, bounds);
        if (!samePosition)
        {
            _pendingHoverText = text;
            _pendingHoverBounds = bounds;
            _pendingHoverStableCount = 1;
            return false;
        }

        _pendingHoverStableCount++;
        _pendingHoverBounds = AverageBounds(_pendingHoverBounds, bounds);
        var requiredStableTicks = mode == HoverMode.Word ? 2 : 3;
        return _pendingHoverStableCount >= requiredStableTicks;
    }

    private void ResetPendingHover()
    {
        _pendingHoverText = null;
        _pendingHoverBounds = Rect.Empty;
        _pendingHoverStableCount = 0;
    }

    private static bool AreClose(Rect a, Rect b)
    {
        return Math.Abs(a.X - b.X) <= 8 &&
               Math.Abs(a.Y - b.Y) <= 8 &&
               Math.Abs(a.Width - b.Width) <= 16 &&
               Math.Abs(a.Height - b.Height) <= 10;
    }

    private static Rect AverageBounds(Rect a, Rect b)
    {
        return new Rect(
            (a.X + b.X) / 2,
            (a.Y + b.Y) / 2,
            (a.Width + b.Width) / 2,
            (a.Height + b.Height) / 2);
    }

    private static Rect ExpandHoverBounds(Rect bounds, string translatedText, HoverMode mode)
    {
        var normalized = TextTranslationFilter.Normalize(translatedText);
        var minWidth = Math.Max(bounds.Width + 8, 40);
        var modeMaxWidth = mode switch
        {
            HoverMode.Word => 240,
            HoverMode.Phrase => 420,
            HoverMode.Sentence => 680,
            _ => 420
        };
        var modeSoftWidth = mode switch
        {
            HoverMode.Word => 180,
            HoverMode.Phrase => 360,
            HoverMode.Sentence => 560,
            _ => 360
        };
        var maxWidth = Math.Max(minWidth, modeMaxWidth);
        var estimatedWidth = Math.Clamp(normalized.Length * Math.Max(7, bounds.Height * 0.52), minWidth, maxWidth);
        var estimatedLines = Math.Max(1, Math.Ceiling(estimatedWidth / modeSoftWidth));
        var width = Math.Min(estimatedWidth, Math.Max(minWidth, modeSoftWidth));
        var minHeight = Math.Max(bounds.Height + 8, 20);
        var maxHeight = Math.Max(minHeight, mode == HoverMode.Word ? 80 : 160);
        var height = Math.Clamp(estimatedLines * Math.Max(18, bounds.Height + 4), minHeight, maxHeight);
        return new Rect(
            bounds.X - 2,
            bounds.Y - 2,
            Math.Max(width, 40),
            Math.Max(height, 20));
    }

    private static Rect BuildHoverPopupBounds(int mouseX, int mouseY, string translatedText, HoverMode mode)
    {
        var screen = Forms.SystemInformation.VirtualScreen;
        var dpiScale = GetCurrentDpiScale();
        var normalized = TextTranslationFilter.Normalize(translatedText);
        var minWidthDip = mode switch
        {
            HoverMode.Word => 160,
            HoverMode.Phrase => 420,
            HoverMode.Sentence => 680,
            _ => 420
        };
        var preferredWidthDip = mode switch
        {
            HoverMode.Word => Math.Min(300, normalized.Length * 12 + 52),
            HoverMode.Phrase => Math.Min(760, normalized.Length * 10 + 96),
            HoverMode.Sentence => Math.Min(1180, normalized.Length * 9 + 140),
            _ => Math.Min(760, normalized.Length * 10 + 96)
        };
        var maxWidthDip = Math.Max(minWidthDip, (screen.Width - 64) / dpiScale);
        var widthDip = Math.Clamp(preferredWidthDip, minWidthDip, maxWidthDip);
        var charsPerLine = Math.Max(8, Math.Floor((widthDip - 28) / (mode == HoverMode.Sentence ? 8.5 : 9.5)));
        var lineCount = Math.Max(1, Math.Ceiling(normalized.Length / charsPerLine));
        var maxHeightDip = mode switch
        {
            HoverMode.Word => 86,
            HoverMode.Phrase => Math.Min(320, (screen.Height - 80) / dpiScale),
            HoverMode.Sentence => Math.Min(520, (screen.Height - 80) / dpiScale),
            _ => Math.Min(320, (screen.Height - 80) / dpiScale)
        };
        var explicitLineCount = Math.Max(0, translatedText.Count(c => c == '\n')) + 1;
        lineCount = Math.Max(lineCount, explicitLineCount);
        var lineHeightDip = mode == HoverMode.Word ? 28 : mode == HoverMode.Phrase ? 27 : 28;
        var heightDip = Math.Clamp(lineCount * lineHeightDip + 34, 52, Math.Max(52, maxHeightDip));
        var width = widthDip * dpiScale;
        var height = heightDip * dpiScale;

        var x = mouseX + 12.0;
        if (x + width > screen.Right - 12)
        {
            x = screen.Right - width - 12;
        }

        var y = mouseY + 24.0;
        if (y + height > screen.Bottom - 12)
        {
            y = mouseY - height - 18.0;
        }

        x = Math.Clamp(x, screen.Left + 8, screen.Right - width - 8);
        y = Math.Clamp(y, screen.Top + 8, screen.Bottom - height - 8);
        return new Rect(x, y, width, height);
    }

    private static OcrTextLine? PickHoverLine(IReadOnlyList<OcrTextLine> lines, double x, double y, string sourceLanguage, string targetLanguage, HoverMode mode)
    {
        var candidates = lines
            .Where(line => line.Bounds.Width > 8 && line.Bounds.Height > 7)
            .Where(line => TextTranslationFilter.ShouldTranslate(line.Text, sourceLanguage, targetLanguage))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var closest = candidates
            .OrderBy(line =>
            {
                if (line.Bounds.Contains(new System.Windows.Point(x, y)))
                {
                    return 0;
                }

                var cx = line.Bounds.X + line.Bounds.Width / 2;
                var cy = line.Bounds.Y + line.Bounds.Height / 2;
                return Math.Abs(cx - x) + Math.Abs(cy - y);
            })
            .FirstOrDefault();

        if (mode == HoverMode.Word)
        {
            if (closest is null)
            {
                return null;
            }

            if (!closest.Bounds.Contains(new System.Windows.Point(x, y)) &&
                DistanceFromRect(closest.Bounds, x, y) > GetHoverPickDistance(closest.Bounds, mode))
            {
                return null;
            }

            return closest;
        }

        if (closest is null)
        {
            return null;
        }

        if (!closest.Bounds.Contains(new System.Windows.Point(x, y)) &&
            DistanceFromRect(closest.Bounds, x, y) > GetHoverPickDistance(closest.Bounds, mode))
        {
            return null;
        }

        return closest;
    }

    private static double GetHoverPickDistance(Rect bounds, HoverMode mode)
    {
        return mode switch
        {
            HoverMode.Word => Math.Max(28, bounds.Height * 2.2),
            HoverMode.Phrase => Math.Max(42, bounds.Height * 2.8),
            HoverMode.Sentence => Math.Max(58, bounds.Height * 3.2),
            _ => Math.Max(42, bounds.Height * 2.8)
        };
    }

    private static double DistanceFromRect(Rect bounds, double x, double y)
    {
        var dx = x < bounds.Left ? bounds.Left - x : x > bounds.Right ? x - bounds.Right : 0;
        var dy = y < bounds.Top ? bounds.Top - y : y > bounds.Bottom ? y - bounds.Bottom : 0;
        return dx + dy;
    }

    private static string FormatRect(Rect? rect)
    {
        return rect is { } value
            ? $"[{value.X:0.##},{value.Y:0.##},{value.Width:0.##},{value.Height:0.##}]"
            : "<null>";
    }

    private static Rect GetHoverCaptureRegion(int x, int y, HoverMode mode)
    {
        var screen = Forms.SystemInformation.VirtualScreen;
        var rect = mode switch
        {
            HoverMode.Word => new Rect(x - 90, y - 28, 220, 64),
            HoverMode.Sentence => new Rect(x - 260, y - 58, 1180, 220),
            _ => new Rect(x - 80, y - 34, 520, 130)
        };
        var left = Math.Clamp(rect.Left, screen.Left, screen.Right - 1);
        var top = Math.Clamp(rect.Top, screen.Top, screen.Bottom - 1);
        var right = Math.Clamp(rect.Right, left + 1, screen.Right);
        var bottom = Math.Clamp(rect.Bottom, top + 1, screen.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static double GetCurrentDpiScale()
    {
        try
        {
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            var source = mainWindow is null ? null : PresentationSource.FromVisual(mainWindow);
            var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            return scale > 0 ? scale : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private bool IsSelectedTargetForeground()
    {
        if (_selectedTarget is null)
        {
            return true;
        }

        return NativeMethods.GetForegroundWindow() == _selectedTarget.Handle;
    }

    private bool IsMouseInsideSelectedTarget()
    {
        if (_selectedTarget is null)
        {
            return true;
        }

        if (!NativeMethods.GetWindowRect(_selectedTarget.Handle, out var rect))
        {
            return false;
        }

        var point = Forms.Cursor.Position;
        return point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;
    }

    private void EditOverlays_OnClick(object sender, RoutedEventArgs e) => SetOverlayEditMode(true);

    private void LockOverlays_OnClick(object sender, RoutedEventArgs e) => SetOverlayEditMode(false);

    private void SetOverlayEditMode(bool editMode)
    {
        foreach (var overlay in _overlayWindows)
        {
            overlay.SetEditMode(editMode);
        }

        if (!editMode)
        {
            SaveCurrentOverlaySet();
        }

        StatusText.Text = editMode
            ? _localizer.T("OverlayEditEnabled")
            : _localizer.T("OverlayLocked");
    }

    private void ClearOverlays_OnClick(object sender, RoutedEventArgs e) => ClearCurrentOverlays();

    private void ClearCurrentOverlays()
    {
        ClearOverlayWindows();
        _entries.Clear();
        SaveCurrentOverlaySet();
        StatusText.Text = _localizer.T("OverlaysCleared");
    }

    private void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.TranslationEndpoint = EndpointBox.Text.Trim();
        _settings.Model = ModelBox.Text.Trim();
        _settings.ApiKey = ApiKeyBox.Password;
        _settings.SourceLanguage = SourceLanguageCombo.SelectedValue as string ?? "auto";
        _settings.TargetLanguage = TargetLanguageBox.Text.Trim();
        _settings.TesseractPath = TesseractPathBox.Text.Trim();
        _settings.OcrLanguages = GetOcrLanguagesForSource(_settings.SourceLanguage);
        _settings.OcrPageSegmentationMode = GetPsmForSource(_settings.SourceLanguage);
        OcrLanguagesBox.Text = _settings.OcrLanguages;
        _settings.UiLanguage = UiLanguageCombo.SelectedValue as string ?? LocalizationService.English;
        _localizer.Language = _settings.UiLanguage;
        _settings.HoverTooltipTranslateEnabled = HoverTooltipToggle.IsChecked == true;
        SaveCurrentHoverDisplaySeconds();
        SaveClipboardDisplaySeconds();
        SaveHotkeyEditors();
        _settingsStore.SaveSettings(_settings);
        _translator = new OpenAiCompatibleTranslationService(_settings);
        _ocrService = new TesseractOcrService(_settings);
        ApplyLocalization();

        if (_hotkeys is not null)
        {
            ShowHotkeyRegistrationStatus(_hotkeys.Restart(_settings), initialLoad: false);
        }
        else
        {
            StatusText.Text = _localizer.T("SettingsSaved");
        }
    }

    private Rect? SelectRegion()
    {
        var wasVisible = IsVisible;
        _hoverSuspended = true;
        CleanupAllHoverOverlays();
        Hide();
        var selector = new RegionSelectionWindow { Owner = this };
        var result = selector.ShowDialog();
        if (wasVisible)
        {
            ShowPanel();
        }

        _hoverSuspended = false;
        ResetPendingHover();
        return result == true ? selector.SelectedRegion : null;
    }

    private void HoverToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        SetHoverTranslateEnabled(HoverToggle.IsChecked == true);
    }

    private void ClipboardToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        SetClipboardDoubleCopyEnabled(ClipboardToggle.IsChecked == true);
    }

    private void ClipboardDisplaySecondsBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded || _initializingUi)
        {
            return;
        }

        SaveClipboardDisplaySeconds();
    }

    private void ClipboardDisplaySecondsBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        SaveClipboardDisplaySeconds();
        ClipboardDisplaySecondsBox.Text = ClampSeconds(_settings.ClipboardDisplaySeconds, 6).ToString("0.##");
    }

    private void SaveClipboardDisplaySeconds()
    {
        if (!double.TryParse(ClipboardDisplaySecondsBox.Text.Trim(), out var seconds))
        {
            return;
        }

        _settings.ClipboardDisplaySeconds = ClampSeconds(seconds, 6);
        if (!_initializingUi)
        {
            _settingsStore.SaveSettings(_settings);
        }
    }

    private void HoverModeCombo_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || HoverModeCombo.SelectedValue is not HoverMode mode)
        {
            return;
        }

        _settings.HoverMode = mode;
        _settingsStore.SaveSettings(_settings);
        HoverDisplaySecondsBox.Text = GetHoverDisplaySeconds(mode).ToString("0.##");
        ResetPendingHover();
        _lastHoverText = null;
        _lastHoverBounds = Rect.Empty;
    }

    private void HoverDisplaySecondsBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded || _initializingUi)
        {
            return;
        }

        SaveCurrentHoverDisplaySeconds();
    }

    private void HoverDisplaySecondsBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        SaveCurrentHoverDisplaySeconds();
        HoverDisplaySecondsBox.Text = GetHoverDisplaySeconds(_settings.HoverMode).ToString("0.##");
    }

    private void SaveCurrentHoverDisplaySeconds()
    {
        if (!double.TryParse(HoverDisplaySecondsBox.Text.Trim(), out var seconds))
        {
            return;
        }

        seconds = ClampHoverSeconds(seconds, GetDefaultHoverSeconds(_settings.HoverMode));
        switch (_settings.HoverMode)
        {
            case HoverMode.Word:
                _settings.HoverWordDisplaySeconds = seconds;
                break;
            case HoverMode.Phrase:
                _settings.HoverPhraseDisplaySeconds = seconds;
                break;
            case HoverMode.Sentence:
                _settings.HoverSentenceDisplaySeconds = seconds;
                break;
        }

        _settingsStore.SaveSettings(_settings);
    }

    private static double ClampHoverSeconds(double value, double fallback)
    {
        return ClampSeconds(value, fallback);
    }

    private static double ClampSeconds(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, 0.2, 60);
    }

    private static double GetDefaultHoverSeconds(HoverMode mode)
    {
        return mode switch
        {
            HoverMode.Word => 0.5,
            HoverMode.Phrase => 2,
            HoverMode.Sentence => 3,
            _ => 2
        };
    }

    private void HoverTooltipToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.HoverTooltipTranslateEnabled = HoverTooltipToggle.IsChecked == true;
        if (!_initializingUi)
        {
            _settingsStore.SaveSettings(_settings);
        }
    }

    private void OcrDebugToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.OcrDebugEnabled = OcrDebugToggle.IsChecked == true;
        if (!_initializingUi)
        {
            _settingsStore.SaveSettings(_settings);
        }
        StatusText.Text = _settings.OcrDebugEnabled
            ? "OCR debug enabled. Model translation is bypassed."
            : "OCR debug disabled.";
    }

    private void SetHoverTranslateEnabled(bool enabled)
    {
        _settings.HoverTranslateEnabled = enabled;
        HoverToggle.IsChecked = enabled;
        if (!_initializingUi)
        {
            _settingsStore.SaveSettings(_settings);
        }
        ApplyHoverState();
        StatusText.Text = enabled ? _localizer.T("HoverEnabled") : _localizer.T("HoverDisabled");
    }

    private void SetClipboardDoubleCopyEnabled(bool enabled)
    {
        _settings.ClipboardDoubleCopyEnabled = enabled;
        ClipboardToggle.IsChecked = enabled;
        if (!_initializingUi)
        {
            _settingsStore.SaveSettings(_settings);
        }
        StatusText.Text = enabled ? _localizer.T("ClipboardEnabled") : _localizer.T("ClipboardDisabled");
    }

    private void ApplyHoverState()
    {
        if (_settings.HoverTranslateEnabled)
        {
            _hoverTimer.Start();
        }
        else
        {
            _hoverTimer.Stop();
            _hoverOverlay?.Close();
            _hoverOverlay = null;
            foreach (var hover in _hoverOverlays.ToList())
            {
                hover.Window.Close();
            }

            _hoverOverlays.Clear();
            _lastHoverPoint = null;
            _lastHoverText = null;
            _lastHoverBounds = Rect.Empty;
            ResetPendingHover();
        }
    }

    private void HidePanel_OnClick(object sender, RoutedEventArgs e) => Hide();

    private void TogglePanel()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowPanel();
        }
    }

    private void ShowPanel()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void LoadHotkeyEditors()
    {
        _hotkeyEditors.Clear();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            _settings.Hotkeys.TryGetValue(action.ToString(), out var gesture);
            _hotkeyEditors.Add(new HotkeyEditorItem
            {
                Action = action,
                DisplayName = GetHotkeyDisplayName(action),
                Gesture = gesture ?? ""
            });
        }
    }

    private void SaveHotkeyEditors()
    {
        foreach (var item in _hotkeyEditors)
        {
            _settings.Hotkeys[item.Action.ToString()] = item.Gesture.Trim();
        }
    }

    private void ShowHotkeyRegistrationStatus(IReadOnlyList<string> errors, bool initialLoad)
    {
        if (errors.Count == 0)
        {
            if (!initialLoad)
            {
                StatusText.Text = _localizer.T("SettingsHotkeysSaved");
            }

            return;
        }

        StatusText.Text = _localizer.Format("HotkeyIssue", string.Join("; ", errors));
    }

    private void UiLanguageCombo_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || UiLanguageCombo.SelectedValue is not string language)
        {
            return;
        }

        _settings.UiLanguage = language;
        _localizer.Language = language;
        ApplyLocalization();
        LoadHotkeyEditors();
    }

    private void ApplyLocalization()
    {
        RefreshWindowsButton.Content = _localizer.T("RefreshWindows");
        HidePanelButton.Content = _localizer.T("HidePanel");
        SubtitleText.Text = _localizer.T("Subtitle");
        UiLanguageLabel.Text = _localizer.T("UiLanguage");
        TargetWindowHeading.Text = _localizer.T("TargetWindow");
        FullOcrButton.Content = _localizer.T("FullOcr");
        RegionOcrButton.Content = _localizer.T("RegionOcr");
        ScreenshotTranslateButton.Content = _localizer.T("Screenshot");
        ManualOverlayButton.Content = _localizer.T("NewOverlay");
        TestImageButton.Content = _localizer.T("TestImage");
        HoverToggle.Content = _localizer.T("EnableHover");
        HoverModeLabel.Text = _localizer.T("HoverMode");
        HoverModeCombo.ItemsSource = GetHoverModeOptions();
        HoverModeCombo.SelectedValue = _settings.HoverMode;
        HoverDisplaySecondsLabel.Text = _localizer.T("HoverDisplaySeconds");
        HoverTooltipToggle.Content = _localizer.T("HoverTooltip");
        ClipboardToggle.Content = _localizer.T("EnableClipboard");
        ClipboardDisplaySecondsLabel.Text = _localizer.T("ClipboardDisplaySeconds");
        OcrDebugToggle.Content = _localizer.T("OcrDebug");
        TranslationApiHeading.Text = _localizer.T("TranslationApi");
        EndpointLabel.Text = _localizer.T("Endpoint");
        ModelLabel.Text = _localizer.T("Model");
        ApiKeyLabel.Text = _localizer.T("ApiKey");
        SourceLanguageLabel.Text = _localizer.T("SourceLanguage");
        TargetLanguageLabel.Text = _localizer.T("TargetLanguage");
        TesseractPathLabel.Text = _localizer.T("TesseractPath");
        OcrLanguagesLabel.Text = _localizer.T("OcrLanguages");
        HotkeysHeading.Text = _localizer.T("Hotkeys");
        HotkeyHelpText.Text = _localizer.T("HotkeyHelp");
        SaveSettingsButton.Content = _localizer.T("SaveSettings");
        OverlayHelpText.Text = _localizer.T("OverlayHelp");
        EditOverlaysButton.Content = _localizer.T("EditOverlays");
        LockOverlaysButton.Content = _localizer.T("LockOverlays");
        ClearOverlaysButton.Content = _localizer.T("ClearOverlays");
        TranslationsHeading.Text = _localizer.T("TranslationsOverlays");
        SourceColumn.Header = _localizer.T("ColumnSource");
        OriginalColumn.Header = _localizer.T("ColumnOriginal");
        TranslationColumn.Header = _localizer.T("ColumnTranslation");
        UpdatedColumn.Header = _localizer.T("ColumnUpdated");
        BuildInfoText.Text = _localizer.T("BuildInfo");
        if (StatusText.Text == "Ready." || StatusText.Text == "准备就绪。")
        {
            StatusText.Text = _localizer.T("Ready");
        }

        UiLanguageCombo.ItemsSource = _localizer.GetLanguages();
        UiLanguageCombo.SelectedValue = LocalizationService.Normalize(_settings.UiLanguage);
        UpdateNotifyIconMenu();
    }

    private void UpdateNotifyIconMenu()
    {
        if (_notifyIcon?.ContextMenuStrip is null)
        {
            return;
        }

        _notifyIcon.ContextMenuStrip.Items.Clear();
        _notifyIcon.ContextMenuStrip.Items.Add(_localizer.T("ShowPanel"), null, (_, _) => ShowPanel());
        _notifyIcon.ContextMenuStrip.Items.Add(_localizer.T("Exit"), null, (_, _) => System.Windows.Application.Current.Shutdown());
    }

    private string GetHotkeyDisplayName(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.TogglePanel => _localizer.T("ShowPanel"),
            HotkeyAction.FullOcr => _localizer.T("FullOcr"),
            HotkeyAction.RegionOcr => _localizer.T("RegionOcr"),
            HotkeyAction.ScreenshotTranslate => _localizer.T("Screenshot"),
            HotkeyAction.ManualOverlay => _localizer.T("NewOverlay"),
            HotkeyAction.EditOverlays => _localizer.T("EditOverlays"),
            HotkeyAction.LockOverlays => _localizer.T("LockOverlays"),
            HotkeyAction.ClearOverlays => _localizer.T("ClearOverlays"),
            HotkeyAction.ToggleHoverTranslate => _localizer.T("EnableHover"),
            HotkeyAction.ToggleClipboardDoubleCopy => _localizer.T("EnableClipboard"),
            _ => action.ToString()
        };
    }

    private void SourceLanguageCombo_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SourceLanguageCombo.SelectedValue is not string sourceLanguage)
        {
            return;
        }

        OcrLanguagesBox.Text = GetOcrLanguagesForSource(sourceLanguage);
    }

    private static IReadOnlyList<LanguageOption> GetSourceLanguageOptions()
    {
        return
        [
            new LanguageOption("auto", "Auto"),
            new LanguageOption("en", "English"),
            new LanguageOption("ja", "Japanese"),
            new LanguageOption("zh", "Chinese")
        ];
    }

    private static string NormalizeSourceLanguage(string? sourceLanguage)
    {
        return sourceLanguage?.ToLowerInvariant() switch
        {
            "english" or "eng" or "en" => "en",
            "japanese" or "jpn" or "ja" => "ja",
            "chinese" or "chi_sim" or "zh" => "zh",
            _ => "auto"
        };
    }

    private static string GetOcrLanguagesForSource(string sourceLanguage)
    {
        return NormalizeSourceLanguage(sourceLanguage) switch
        {
            "en" => "eng",
            "ja" => "jpn",
            "zh" => "chi_sim",
            _ => "jpn+eng+chi_sim"
        };
    }

    private static int GetPsmForSource(string sourceLanguage)
    {
        return NormalizeSourceLanguage(sourceLanguage) switch
        {
            "ja" => 6,
            _ => 11
        };
    }

    private IReadOnlyList<HoverModeOption> GetHoverModeOptions()
    {
        return
        [
            new HoverModeOption(HoverMode.Word, _localizer.T("HoverWord")),
            new HoverModeOption(HoverMode.Phrase, _localizer.T("HoverPhrase")),
            new HoverModeOption(HoverMode.Sentence, _localizer.T("HoverSentence"))
        ];
    }

    private sealed class HoverOverlayState(OverlayWindow window, DateTimeOffset expiresAt)
    {
        public OverlayWindow Window { get; } = window;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public bool HiddenForCapture { get; set; }
    }

    [GeneratedRegex(@"([a-z])([A-Z])")]
    private static partial Regex PascalCaseBoundaryRegex();

    [GeneratedRegex(@"([A-Za-z])(\d)")]
    private static partial Regex LetterDigitBoundaryRegex();

    [GeneratedRegex(@"(\d)([A-Za-z])")]
    private static partial Regex DigitLetterBoundaryRegex();
}
