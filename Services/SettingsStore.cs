using System.IO;
using System.Text.Json;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string _root;

    public SettingsStore()
    {
        _root = ResolveWritableRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NativeOverlayTranslator");
    }

    public AppSettings LoadSettings()
    {
        var path = Path.Combine(_root, "settings.json");
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            settings.EnsureDefaults();
            return settings;
        }
        catch
        {
            var settings = new AppSettings();
            settings.EnsureDefaults();
            return settings;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        WriteAllTextSafe("settings.json", JsonSerializer.Serialize(settings, JsonOptions));
    }

    public IReadOnlyList<OverlayEntry> LoadOverlays(string processKey)
    {
        var path = GetOverlayPath(processKey);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<OverlayEntry>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveOverlays(string processKey, IEnumerable<OverlayEntry> entries)
    {
        WriteAllTextSafe($"overlays_{processKey}.json", JsonSerializer.Serialize(entries, JsonOptions));
    }

    public static string BuildProcessKey(TargetWindowInfo? target)
    {
        if (target is null)
        {
            return "global";
        }

        var raw = string.IsNullOrWhiteSpace(target.ProcessPath)
            ? $"{target.ProcessName}_{target.ProcessId}"
            : target.ProcessPath;

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            raw = raw.Replace(c, '_');
        }

        return raw.Replace(':', '_').Replace('\\', '_').Replace('/', '_');
    }

    private string GetOverlayPath(string processKey) => Path.Combine(_root, $"overlays_{processKey}.json");

    private void WriteAllTextSafe(string fileName, string contents)
    {
        var path = Path.Combine(_root, fileName);
        try
        {
            File.WriteAllText(path, contents);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            SwitchToFallbackRoot();
        }
        catch (IOException)
        {
            SwitchToFallbackRoot();
        }

        File.WriteAllText(Path.Combine(_root, fileName), contents);
    }

    private void SwitchToFallbackRoot()
    {
        var fallback = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(fallback);
        _root = fallback;
    }

    internal static string ResolveWritableRoot(params string[] preferredParts)
    {
        var preferred = Path.Combine(preferredParts);
        if (TryCreateDirectory(preferred))
        {
            return preferred;
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "data");
        if (TryCreateDirectory(fallback))
        {
            return fallback;
        }

        fallback = Path.Combine(Environment.CurrentDirectory, "data");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
