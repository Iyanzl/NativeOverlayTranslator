using System.IO;
using System.Text.Json;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class TranslationMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;

    public TranslationMemoryStore()
    {
        _root = SettingsStore.ResolveWritableRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NativeOverlayTranslator",
            "memory");
    }

    public string? TryGet(TargetWindowInfo? target, string sourceText)
    {
        var memory = Load(target);
        var key = Normalize(sourceText);
        if (!memory.TryGetValue(key, out var translated))
        {
            return null;
        }

        if (!IsTranslationFailure(translated))
        {
            return translated;
        }

        memory.Remove(key);
        Save(target, memory);
        return null;
    }

    public void Remember(TargetWindowInfo? target, string sourceText, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(sourceText) ||
            string.IsNullOrWhiteSpace(translatedText) ||
            IsTranslationFailure(translatedText))
        {
            return;
        }

        var memory = Load(target);
        memory[Normalize(sourceText)] = translatedText.Trim();
        Save(target, memory);
    }

    private Dictionary<string, string> Load(TargetWindowInfo? target)
    {
        var path = GetPath(target);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void Save(TargetWindowInfo? target, Dictionary<string, string> memory)
    {
        File.WriteAllText(GetPath(target), JsonSerializer.Serialize(memory, JsonOptions));
    }

    private string GetPath(TargetWindowInfo? target)
    {
        return Path.Combine(_root, $"{SettingsStore.BuildProcessKey(target)}.json");
    }

    private static string Normalize(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsTranslationFailure(string text)
    {
        return text.StartsWith("[Translation failed:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[Translation canceled", StringComparison.OrdinalIgnoreCase);
    }
}
