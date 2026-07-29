using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using NativeOverlayTranslator.Models;
using NativeOverlayTranslator.Services;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace NativeOverlayTranslator;

public partial class ImageTranslationWindow : Window
{
    private const double ChromeHeightEstimate = 96;
    private const double WindowPaddingEstimate = 36;
    private readonly string _imagePath;
    private readonly ITextCaptureService _ocrService;
    private readonly ITranslationService _translationService;
    private readonly TranslationMemoryStore _memoryStore;
    private readonly AppSettings _settings;
    private readonly ImageOverlayStyleStabilizer _styleStabilizer = new();
    private Bitmap? _bitmap;

    public ImageTranslationWindow(
        string imagePath,
        ITextCaptureService ocrService,
        ITranslationService translationService,
        TranslationMemoryStore memoryStore,
        AppSettings settings)
    {
        _imagePath = imagePath;
        _ocrService = ocrService;
        _translationService = translationService;
        _memoryStore = memoryStore;
        _settings = settings;
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => ApplyFitScaleToViewport();
        Closed += (_, _) => _bitmap?.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadImage();
        await TranslateImageAsync();
    }

    private async void Retranslate_OnClick(object sender, RoutedEventArgs e)
    {
        await TranslateImageAsync();
    }

    private void LoadImage()
    {
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.UriSource = new Uri(_imagePath, UriKind.Absolute);
        bitmapImage.EndInit();

        SourceImage.Source = bitmapImage;
        SourceImage.Width = bitmapImage.PixelWidth;
        SourceImage.Height = bitmapImage.PixelHeight;
        SourceImage.Stretch = Stretch.Fill;
        SourceImage.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        SourceImage.VerticalAlignment = VerticalAlignment.Top;
        OverlayCanvas.Width = bitmapImage.PixelWidth;
        OverlayCanvas.Height = bitmapImage.PixelHeight;
        ImageHost.Width = bitmapImage.PixelWidth;
        ImageHost.Height = bitmapImage.PixelHeight;

        _bitmap?.Dispose();
        _bitmap = new Bitmap(_imagePath);
        _styleStabilizer.Clear();
        ConfigureInitialWindow(bitmapImage.PixelWidth, bitmapImage.PixelHeight);
        ApplyFitScaleToViewport();
    }

    private void ConfigureInitialWindow(int imageWidth, int imageHeight)
    {
        var dpiScale = GetDpiScale();
        var workArea = SystemParameters.WorkArea;
        var maxWindowWidth = Math.Max(240, workArea.Width * 0.94);
        var maxWindowHeight = Math.Max(180, workArea.Height * 0.90);
        var maxImageWidth = Math.Max(160, maxWindowWidth - WindowPaddingEstimate);
        var maxImageHeight = Math.Max(160, maxWindowHeight - ChromeHeightEstimate);
        var nativeDisplayWidth = imageWidth / dpiScale;
        var nativeDisplayHeight = imageHeight / dpiScale;
        var fitScale = Math.Min(1.0, Math.Min(maxImageWidth / Math.Max(1, nativeDisplayWidth), maxImageHeight / Math.Max(1, nativeDisplayHeight)));
        var displayWidth = nativeDisplayWidth * fitScale;
        var displayHeight = nativeDisplayHeight * fitScale;

        Width = Math.Clamp(displayWidth + WindowPaddingEstimate, 160, maxWindowWidth);
        Height = Math.Clamp(displayHeight + ChromeHeightEstimate, 120, maxWindowHeight);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void ApplyFitScaleToViewport()
    {
        if (_bitmap is null || ImageScroller.ActualWidth <= 0 || ImageScroller.ActualHeight <= 0)
        {
            return;
        }

        var dpiScale = GetDpiScale();
        var nativeScale = 1.0 / dpiScale;
        var viewportWidth = Math.Max(1, ImageScroller.ActualWidth - 18);
        var viewportHeight = Math.Max(1, ImageScroller.ActualHeight - 18);
        var scale = Math.Min(nativeScale, Math.Min(viewportWidth / Math.Max(1, _bitmap.Width), viewportHeight / Math.Max(1, _bitmap.Height)));
        ImageScale.ScaleX = scale;
        ImageScale.ScaleY = scale;
    }

    private double GetDpiScale()
    {
        try
        {
            var source = PresentationSource.FromVisual(this);
            var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            return scale > 0 ? scale : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private async Task TranslateImageAsync()
    {
        OverlayCanvas.Children.Clear();
        StatusText.Text = "OCR image...";
        Diagnostics.Log($"ImageTranslate start path='{_imagePath}'");
        var lines = await _ocrService.CaptureImageAsync(_imagePath, CancellationToken.None);
        Diagnostics.Log($"ImageTranslate raw lines={lines.Count}");
        if (OcrFailureDetector.IsFailureResult(lines))
        {
            var message = OcrFailureDetector.BuildFailureMessage(_ocrService.Name, lines);
            Diagnostics.Log($"ImageTranslate selected engine failed engine='{_ocrService.Name}' message='{message}'");
            StatusText.Text = message;
            System.Windows.MessageBox.Show(message, "Image OCR failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var usefulLines = lines
            .Where(line => line.Bounds.Width > 8 && line.Bounds.Height > 7 && line.Confidence >= 0.10)
            .ToList();
        Diagnostics.Log($"ImageTranslate useful lines={usefulLines.Count}");

        StatusText.Text = $"Translating {usefulLines.Count} line(s)...";
        var completed = 0;
        foreach (var line in usefulLines)
        {
            var translation = _memoryStore.TryGet(null, line.Text);
            if (_settings.OcrDebugEnabled)
            {
                translation = $"OCR {line.Confidence:P0}: {line.Text}";
            }
            else if (string.IsNullOrWhiteSpace(translation))
            {
                translation = await _translationService.TranslateAsync(line.Text, _settings.SourceLanguage, _settings.TargetLanguage, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(translation))
                {
                    translation = line.Text;
                }

                if (!IsTranslationFailure(translation))
                {
                    _memoryStore.Remember(null, line.Text, translation);
                }
            }

            if (!_settings.OcrDebugEnabled && IsTranslationFailure(translation))
            {
                Diagnostics.Log($"ImageTranslate skipped failed text='{line.Text}' translation='{translation}'");
                StatusText.Text = $"Skipped failed translation: {line.Text}";
                continue;
            }

            Diagnostics.Log($"ImageTranslate overlay source='{line.Text}' translated='{translation}' bounds={line.Bounds.X:0.##},{line.Bounds.Y:0.##},{line.Bounds.Width:0.##},{line.Bounds.Height:0.##}");
            AddInlineOverlay(line, translation);
            completed++;
            StatusText.Text = $"Translated {completed}/{usefulLines.Count}: {line.Text}";
        }

        StatusText.Text = $"Done. {usefulLines.Count} overlay(s) are attached to the image.";
    }

    private void AddInlineOverlay(OcrTextLine line, string translation)
    {
        var sampledStyle = _bitmap is null
            ? new ImageOverlayStyle(Color.FromArgb(230, 20, 20, 20), Color.White, FontWeights.Normal)
            : ImageOverlayStyleSampler.Sample(_bitmap, line.Bounds);
        var style = _styleStabilizer.Resolve(line.Text, line.Bounds, sampledStyle);
        var overlayWidth = EstimateOverlayWidth(line.Bounds, translation);
        var overlayHeight = EstimateOverlayHeight(line.Bounds, translation);
        var box = new System.Windows.Controls.TextBox
        {
            Text = translation,
            Tag = line.Text,
            TextAlignment = ShouldUseTwoLineLayout(line.Bounds, translation, overlayWidth) ? TextAlignment.Center : TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0),
            Background = ToBrush(style.Background),
            Foreground = ToBrush(style.Foreground),
            FontSize = EstimateFittedFontSize(line.Bounds, translation, overlayWidth, overlayHeight),
            FontWeight = style.FontWeight,
            MinWidth = Math.Max(24, line.Bounds.Width),
            Width = overlayWidth,
            Height = overlayHeight,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        box.TextChanged += (_, _) => _memoryStore.Remember(null, line.Text, box.Text);
        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Alt)
            {
                box.CaptureMouse();
                box.Tag = new DragState(e.GetPosition(OverlayCanvas), Canvas.GetLeft(box), Canvas.GetTop(box), line.Text);
                e.Handled = true;
            }
        };
        box.PreviewMouseMove += (_, e) =>
        {
            if (box.Tag is not DragState drag || !box.IsMouseCaptured)
            {
                return;
            }

            var current = e.GetPosition(OverlayCanvas);
            Canvas.SetLeft(box, drag.Left + current.X - drag.Start.X);
            Canvas.SetTop(box, drag.Top + current.Y - drag.Start.Y);
        };
        box.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (box.Tag is DragState dragState)
            {
                box.Tag = dragState.SourceText;
            }

            box.ReleaseMouseCapture();
        };

        Canvas.SetLeft(box, Math.Max(0, line.Bounds.X));
        Canvas.SetTop(box, Math.Max(0, line.Bounds.Y));
        OverlayCanvas.Children.Add(box);
    }

    private static double EstimateFontSize(Rect bounds)
    {
        if (IsCompactUiText(bounds))
        {
            return Math.Clamp(bounds.Height * 0.90, 12, 16);
        }

        return Math.Clamp(bounds.Height * 0.88, 12, 34);
    }

    private static double EstimateFittedFontSize(Rect bounds, string text, double width, double height)
    {
        var normalizedLength = Math.Max(1, TextTranslationFilter.Normalize(text).Length);
        var targetFont = EstimateFontSize(bounds);
        var minFont = Math.Clamp(targetFont * 0.72, 10, Math.Min(targetFont, 13));
        var oneLineWidth = normalizedLength * targetFont * 0.58;
        var availableWidth = Math.Max(8, width - 8);
        var font = oneLineWidth > availableWidth
            ? Math.Max(minFont, targetFont * availableWidth / oneLineWidth)
            : targetFont;

        var estimatedLines = ShouldUseTwoLineLayout(bounds, text, width) ? 2 : 1;
        var heightFont = Math.Clamp((height - 2) / estimatedLines * 0.88, Math.Min(10, targetFont), targetFont);
        return Math.Min(font, heightFont);
    }

    private static bool ShouldUseTwoLineLayout(Rect bounds, string text, double width)
    {
        var normalizedLength = Math.Max(1, TextTranslationFilter.Normalize(text).Length);
        var targetFont = EstimateFontSize(bounds);
        var minFont = Math.Clamp(targetFont * 0.72, 10, Math.Min(targetFont, 13));
        return normalizedLength * minFont * 0.58 > Math.Max(8, width - 8);
    }

    private static double EstimateOverlayWidth(Rect bounds, string text)
    {
        var minWidth = Math.Max(18, bounds.Width);
        if (IsCompactUiText(bounds))
        {
            return Math.Min(210, Math.Max(minWidth, bounds.Width + 4));
        }

        return Math.Max(minWidth, bounds.Width + 4);
    }

    private static double EstimateOverlayHeight(Rect bounds, string text)
    {
        var width = EstimateOverlayWidth(bounds, text);
        var normalizedLength = Math.Max(1, TextTranslationFilter.Normalize(text).Length);
        var minHeight = Math.Max(14, bounds.Height + 2);
        if (IsCompactUiText(bounds))
        {
            var estimatedLines = Math.Max(1, Math.Ceiling((normalizedLength * Math.Max(4.6, bounds.Height * 0.30)) / Math.Max(1, width - 8)));
            var needsTwoLines = estimatedLines > 1 && bounds.Width < 80;
            return Math.Clamp(
                needsTwoLines ? bounds.Height * 1.25 + 4 : bounds.Height + 3,
                minHeight,
                Math.Max(minHeight, Math.Min(44, bounds.Height * 1.35 + 6)));
        }

        var rawTextWidth = normalizedLength * Math.Max(6, bounds.Height * 0.40);
        var estimatedLineCount = Math.Max(1, Math.Ceiling(rawTextWidth / Math.Max(1, width - 4)));
        var maxHeight = Math.Max(minHeight, Math.Min(120, bounds.Height * 1.45 + 8));
        return Math.Clamp(estimatedLineCount * Math.Max(16, bounds.Height + 2), minHeight, maxHeight);
    }

    private static bool IsCompactUiText(Rect bounds)
    {
        return bounds.Height <= 26 && bounds.Width <= 260;
    }

    private static SolidColorBrush ToBrush(Color color)
    {
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    private string SaveTranslatedImage()
    {
        var previousScaleX = ImageScale.ScaleX;
        var previousScaleY = ImageScale.ScaleY;
        try
        {
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
        var width = Math.Max(1, _bitmap?.Width ?? (int)Math.Ceiling(ImageHost.ActualWidth));
        var height = Math.Max(1, _bitmap?.Height ?? (int)Math.Ceiling(ImageHost.ActualHeight));
        var exportVisual = BuildExportVisual(width, height);
        var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderBitmap.Render(exportVisual);

        var outputPath = Path.Combine(Path.GetDirectoryName(_imagePath) ?? AppContext.BaseDirectory, "翻译.png");
        using var stream = File.Create(outputPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
        encoder.Save(stream);
            return outputPath;
        }
        finally
        {
            ImageScale.ScaleX = previousScaleX;
            ImageScale.ScaleY = previousScaleY;
        }
    }

    private Grid BuildExportVisual(int width, int height)
    {
        var image = new System.Windows.Controls.Image
        {
            Source = SourceImage.Source,
            Stretch = Stretch.Fill,
            Width = width,
            Height = height
        };

        var canvas = new Canvas
        {
            Width = width,
            Height = height
        };

        foreach (var sourceBox in OverlayCanvas.Children.OfType<System.Windows.Controls.TextBox>())
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = sourceBox.Text,
                TextAlignment = sourceBox.TextAlignment,
                TextWrapping = sourceBox.TextWrapping,
                AcceptsReturn = sourceBox.AcceptsReturn,
                BorderThickness = sourceBox.BorderThickness,
                Padding = sourceBox.Padding,
                Background = sourceBox.Background,
                Foreground = sourceBox.Foreground,
                FontSize = sourceBox.FontSize,
                FontWeight = sourceBox.FontWeight,
                Width = sourceBox.Width,
                Height = sourceBox.Height,
                VerticalContentAlignment = sourceBox.VerticalContentAlignment
            };
            Canvas.SetLeft(box, Canvas.GetLeft(sourceBox));
            Canvas.SetTop(box, Canvas.GetTop(sourceBox));
            canvas.Children.Add(box);
        }

        var visual = new Grid
        {
            Width = width,
            Height = height,
            Background = System.Windows.Media.Brushes.White
        };
        visual.Children.Add(image);
        visual.Children.Add(canvas);
        visual.Measure(new System.Windows.Size(width, height));
        visual.Arrange(new Rect(0, 0, width, height));
        visual.UpdateLayout();
        return visual;
    }

    private sealed record DragState(Point Start, double Left, double Top, string SourceText);

    private static bool IsTranslationFailure(string text)
    {
        return text.StartsWith("[Translation failed:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[Translation canceled", StringComparison.OrdinalIgnoreCase);
    }
}
