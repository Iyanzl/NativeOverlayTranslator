using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NativeOverlayTranslator.Models;
using NativeOverlayTranslator.Services;

namespace NativeOverlayTranslator;

public partial class OverlayWindow : Window
{
    private bool _isUpdating;
    private bool _isProgrammaticMove;
    public OverlayEntry Entry { get; }

    public event EventHandler? EntryChanged;

    public OverlayWindow(OverlayEntry entry)
    {
        Entry = entry;
        InitializeComponent();
        _isUpdating = true;
        TranslationBox.Text = entry.TranslatedText;
        TranslationBox.FontSize = GetDisplayFontSize(entry.FontSize);
        var displayBounds = ToDipRect(entry.Bounds);
        TranslationBox.Width = Math.Max(displayBounds.Width - 4, 12);
        TranslationBox.Height = Math.Max(displayBounds.Height - 2, 10);
        _isUpdating = false;
        ApplyEntryStyle();
        Left = displayBounds.X;
        Top = displayBounds.Y;
        Width = Math.Max(displayBounds.Width, 18);
        Height = Math.Max(displayBounds.Height, 14);
        FitTextToBounds();
        Loaded += OnLoaded;
        LocationChanged += (_, _) => PersistBounds();
        SizeChanged += (_, _) =>
        {
            FitTextToBounds();
            PersistBounds();
        };
    }

    public void SetEditMode(bool enabled)
    {
        Entry.IsLocked = !enabled;
        TranslationBox.IsReadOnly = !enabled;
        ResizeMode = enabled ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        Frame.BorderBrush = enabled
            ? System.Windows.Media.Brushes.DeepSkyBlue
            : ParseBrush(Entry.BorderColor);
        UpdateClickThrough(!enabled);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log($"OverlayWindow loaded left={Left:0.##} top={Top:0.##} width={Width:0.##} height={Height:0.##} text='{Entry.TranslatedText}'");
        SetEditMode(!Entry.IsLocked);
    }

    private void TranslationBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        Entry.TranslatedText = TranslationBox.Text;
        Entry.UpdatedAt = DateTimeOffset.Now;
        EntryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TranslationBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Entry.IsLocked)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void PersistBounds()
    {
        if (_isProgrammaticMove)
        {
            return;
        }

        Entry.Bounds = ToPhysicalRect(new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height));
        Entry.UpdatedAt = DateTimeOffset.Now;
        EntryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetScreenBounds(Rect bounds)
    {
        var displayBounds = ToDipRect(bounds);
        _isProgrammaticMove = true;
        Left = displayBounds.X;
        Top = displayBounds.Y;
        Width = Math.Max(displayBounds.Width, 24);
        Height = Math.Max(displayBounds.Height, 18);
        TranslationBox.Width = Math.Max(displayBounds.Width - 4, 12);
        TranslationBox.Height = Math.Max(displayBounds.Height - 2, 10);
        Entry.Bounds = bounds;
        FitTextToBounds();
        _isProgrammaticMove = false;
    }

    private void UpdateClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
        if (enabled)
        {
            style |= NativeMethods.WS_EX_TRANSPARENT;
        }
        else
        {
            style &= ~NativeMethods.WS_EX_TRANSPARENT;
        }

        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style);
    }

    private void ApplyEntryStyle()
    {
        Frame.Background = ParseBrush(Entry.BackgroundColor);
        Frame.BorderBrush = ParseBrush(Entry.BorderColor);
        TranslationBox.Foreground = ParseBrush(Entry.ForegroundColor);
    }

    private void FitTextToBounds()
    {
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        TranslationBox.Width = Math.Max(width - 4, 12);
        TranslationBox.Height = Math.Max(height - 2, 10);
        var text = TextTranslationFilter.Normalize(TranslationBox.Text);
        var textLength = Math.Max(1, text.Length);
        var targetFont = Entry.FontSize > 0
            ? GetDisplayFontSize(Entry.FontSize)
            : Math.Clamp(height * 0.78, 9, 16);
        targetFont = Math.Clamp(targetFont, 10, 28);
        var minFont = Math.Clamp(targetFont * 0.72, 10, Math.Min(targetFont, 13));
        var oneLineWidth = EstimateTextWidth(textLength, targetFont);
        var availableWidth = Math.Max(8, width - 8);
        var fontSize = targetFont;

        if (oneLineWidth > availableWidth)
        {
            fontSize = Math.Max(minFont, targetFont * availableWidth / oneLineWidth);
        }

        var estimatedLines = 1;
        if (fontSize <= minFont + 0.1 && EstimateTextWidth(textLength, fontSize) > availableWidth)
        {
            estimatedLines = 2;
            var twoLineWidth = EstimateTextWidth(Math.Ceiling(textLength / 2.0), fontSize);
            if (twoLineWidth > availableWidth)
            {
                fontSize = Math.Max(minFont, fontSize * availableWidth / twoLineWidth);
            }
        }

        var lineHeight = Math.Max(9, (height - 2) / estimatedLines * 0.88);
        var lineHeightDriven = Math.Clamp(lineHeight, Math.Min(10, targetFont), targetFont);
        fontSize = Math.Min(fontSize, lineHeightDriven);
        TranslationBox.TextAlignment = estimatedLines > 1 ? TextAlignment.Center : TextAlignment.Left;
        TranslationBox.FontSize = fontSize;
        Diagnostics.Log($"Overlay fit width={width:0.##} height={height:0.##} sourceFont={Entry.FontSize:0.##} dipFont={fontSize:0.##} lines={estimatedLines} text='{Entry.TranslatedText}'");
    }

    private static double EstimateTextWidth(double length, double fontSize)
    {
        return length * fontSize * 0.58;
    }

    private Rect ToDipRect(Rect physicalRect)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            physicalRect.X / dpi.DpiScaleX,
            physicalRect.Y / dpi.DpiScaleY,
            physicalRect.Width / dpi.DpiScaleX,
            physicalRect.Height / dpi.DpiScaleY);
    }

    private Rect ToPhysicalRect(Rect dipRect)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            dipRect.X * dpi.DpiScaleX,
            dipRect.Y * dpi.DpiScaleY,
            dipRect.Width * dpi.DpiScaleX,
            dipRect.Height * dpi.DpiScaleY);
    }

    private double GetDisplayFontSize(double fontSize)
    {
        return Entry.FontSizeIsPhysicalPixels ? ToDipFontSize(fontSize) : fontSize;
    }

    private double ToDipFontSize(double physicalFontSize)
    {
        if (physicalFontSize <= 0)
        {
            return physicalFontSize;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return physicalFontSize / Math.Max(0.01, dpi.DpiScaleY);
    }

    private static SolidColorBrush ParseBrush(string color)
    {
        try
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        }
        catch
        {
            return new SolidColorBrush(System.Windows.Media.Colors.White);
        }
    }
}
