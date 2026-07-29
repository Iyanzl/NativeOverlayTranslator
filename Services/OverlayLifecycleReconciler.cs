using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class OverlayLifecycleReconciler
{
    private readonly object _gate = new();
    private readonly int _missesBeforeRemoval;
    private readonly Dictionary<string, TrackedSource> _tracked = new(StringComparer.Ordinal);
    private long _nextAnonymousId;

    public OverlayLifecycleReconciler(int missesBeforeRemoval = 2)
    {
        _missesBeforeRemoval = Math.Max(1, missesBeforeRemoval);
    }

    public IReadOnlyList<OverlayLifecycleChange> Reconcile(IEnumerable<TextSourceSnapshot> snapshots)
    {
        lock (_gate)
        {
            var currentSnapshots = PrepareSnapshots(snapshots);
            var matchedTrackingIds = new HashSet<string>(StringComparer.Ordinal);
            var changes = new List<OverlayLifecycleChange>();

            foreach (var snapshot in currentSnapshots)
            {
                var tracked = FindStableMatch(snapshot, matchedTrackingIds)
                    ?? FindNearbyTextMatch(snapshot, matchedTrackingIds);
                if (tracked is null)
                {
                    var trackingId = CreateTrackingId(snapshot);
                    tracked = new TrackedSource(trackingId, snapshot);
                    _tracked.Add(trackingId, tracked);
                    matchedTrackingIds.Add(trackingId);
                    changes.Add(new OverlayLifecycleChange(
                        OverlayLifecycleChangeKind.Added,
                        trackingId,
                        Previous: null,
                        Current: snapshot));
                    continue;
                }

                matchedTrackingIds.Add(tracked.TrackingId);
                tracked.MissedReconciliations = 0;
                var previous = tracked.Current;
                tracked.Current = snapshot;
                if (HasMeaningfulChange(previous, snapshot))
                {
                    changes.Add(new OverlayLifecycleChange(
                        OverlayLifecycleChangeKind.Updated,
                        tracked.TrackingId,
                        previous,
                        snapshot));
                }
            }

            foreach (var tracked in _tracked.Values
                         .Where(item => !matchedTrackingIds.Contains(item.TrackingId))
                         .ToList())
            {
                tracked.MissedReconciliations++;
                if (tracked.MissedReconciliations < _missesBeforeRemoval)
                {
                    continue;
                }

                _tracked.Remove(tracked.TrackingId);
                changes.Add(new OverlayLifecycleChange(
                    OverlayLifecycleChangeKind.Removed,
                    tracked.TrackingId,
                    tracked.Current,
                    Current: null));
            }

            return changes;
        }
    }

    public IReadOnlyList<OverlayLifecycleChange> Reset()
    {
        lock (_gate)
        {
            var removed = _tracked.Values
                .Select(tracked => new OverlayLifecycleChange(
                    OverlayLifecycleChangeKind.Removed,
                    tracked.TrackingId,
                    tracked.Current,
                    Current: null))
                .ToList();
            _tracked.Clear();
            return removed;
        }
    }

    private static IReadOnlyList<TextSourceSnapshot> PrepareSnapshots(IEnumerable<TextSourceSnapshot> snapshots)
    {
        var prepared = snapshots
            .Where(IsUsable)
            .ToList();
        var stableKeys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TextSourceSnapshot>(prepared.Count);

        foreach (var snapshot in prepared
                     .OrderByDescending(item => item.Confidence)
                     .ThenBy(item => item.Bounds.Y)
                     .ThenBy(item => item.Bounds.X))
        {
            if (string.IsNullOrWhiteSpace(snapshot.StableId))
            {
                result.Add(snapshot);
                continue;
            }

            var stableKey = BuildStableKey(snapshot);
            if (stableKeys.Add(stableKey))
            {
                result.Add(snapshot);
            }
        }

        return result;
    }

    private TrackedSource? FindStableMatch(TextSourceSnapshot snapshot, HashSet<string> matchedTrackingIds)
    {
        if (string.IsNullOrWhiteSpace(snapshot.StableId))
        {
            return null;
        }

        var stableKey = BuildStableKey(snapshot);
        return _tracked.Values.FirstOrDefault(tracked =>
            !matchedTrackingIds.Contains(tracked.TrackingId) &&
            string.Equals(BuildStableKey(tracked.Current), stableKey, StringComparison.Ordinal));
    }

    private TrackedSource? FindNearbyTextMatch(TextSourceSnapshot snapshot, HashSet<string> matchedTrackingIds)
    {
        var normalizedText = NormalizeText(snapshot.Text);
        return _tracked.Values
            .Where(tracked => !matchedTrackingIds.Contains(tracked.TrackingId))
            .Where(tracked => string.Equals(tracked.Current.SourceKind, snapshot.SourceKind, StringComparison.OrdinalIgnoreCase))
            .Where(tracked => string.Equals(NormalizeText(tracked.Current.Text), normalizedText, StringComparison.OrdinalIgnoreCase))
            .Where(tracked => IsNearby(tracked.Current.Bounds, snapshot.Bounds))
            .OrderBy(tracked => CenterDistance(tracked.Current.Bounds, snapshot.Bounds))
            .ThenBy(tracked => tracked.TrackingId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private string CreateTrackingId(TextSourceSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.StableId))
        {
            var baseId = BuildStableKey(snapshot);
            if (!_tracked.ContainsKey(baseId))
            {
                return baseId;
            }
        }

        return $"{NormalizeKind(snapshot.SourceKind)}:anonymous:{++_nextAnonymousId}";
    }

    private static bool IsUsable(TextSourceSnapshot snapshot)
    {
        return snapshot.IsVisible &&
               !string.IsNullOrWhiteSpace(snapshot.SourceKind) &&
               !string.IsNullOrWhiteSpace(snapshot.Text) &&
               !snapshot.Bounds.IsEmpty &&
               snapshot.Bounds.Width > 0 &&
               snapshot.Bounds.Height > 0;
    }

    private static bool HasMeaningfulChange(TextSourceSnapshot previous, TextSourceSnapshot current)
    {
        return !string.Equals(NormalizeText(previous.Text), NormalizeText(current.Text), StringComparison.Ordinal) ||
               !BoundsAreClose(previous.Bounds, current.Bounds);
    }

    private static bool BoundsAreClose(Rect left, Rect right)
    {
        return Math.Abs(left.X - right.X) <= 1 &&
               Math.Abs(left.Y - right.Y) <= 1 &&
               Math.Abs(left.Width - right.Width) <= 1 &&
               Math.Abs(left.Height - right.Height) <= 1;
    }

    private static bool IsNearby(Rect left, Rect right)
    {
        if (left.IntersectsWith(right))
        {
            return true;
        }

        var threshold = Math.Max(48, Math.Max(
            Math.Max(left.Width, right.Width),
            Math.Max(left.Height, right.Height)) * 0.75);
        return CenterDistance(left, right) <= threshold;
    }

    private static double CenterDistance(Rect left, Rect right)
    {
        var dx = left.X + left.Width / 2 - (right.X + right.Width / 2);
        var dy = left.Y + left.Height / 2 - (right.Y + right.Height / 2);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string BuildStableKey(TextSourceSnapshot snapshot)
    {
        return $"{NormalizeKind(snapshot.SourceKind)}:{snapshot.StableId.Trim()}";
    }

    private static string NormalizeKind(string sourceKind)
    {
        return sourceKind.Trim().ToLowerInvariant();
    }

    private static string NormalizeText(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class TrackedSource(string trackingId, TextSourceSnapshot current)
    {
        public string TrackingId { get; } = trackingId;
        public TextSourceSnapshot Current { get; set; } = current;
        public int MissedReconciliations { get; set; }
    }
}
