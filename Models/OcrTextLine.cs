using System.Windows;

namespace NativeOverlayTranslator.Models;

public sealed record OcrTextLine(string Text, Rect Bounds, double Confidence);
