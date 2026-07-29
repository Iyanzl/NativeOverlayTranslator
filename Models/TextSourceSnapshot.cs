using System.Windows;

namespace NativeOverlayTranslator.Models;

public sealed record TextSourceSnapshot(
    string SourceKind,
    string StableId,
    string Text,
    Rect Bounds,
    double Confidence = 1,
    bool IsVisible = true);

public enum OverlayLifecycleChangeKind
{
    Added,
    Updated,
    Removed
}

public sealed record OverlayLifecycleChange(
    OverlayLifecycleChangeKind Kind,
    string TrackingId,
    TextSourceSnapshot? Previous,
    TextSourceSnapshot? Current);
