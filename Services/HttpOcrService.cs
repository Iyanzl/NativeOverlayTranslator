using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class HttpOcrService(AppSettings settings, string name, Func<AppSettings, string> endpointSelector) : ITextCaptureService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public string Name { get; } = name;

    public async Task<IReadOnlyList<OcrTextLine>> CaptureAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken)
    {
        var captureBounds = ResolveCaptureBounds(target, region);
        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
        {
            return [];
        }

        var tempImage = ScreenCaptureService.CaptureRegionToPng(captureBounds, Path.GetTempPath(), $"not_{Name.ToLowerInvariant()}");
        try
        {
            return await RecognizeImageAsync(tempImage, captureBounds, cancellationToken);
        }
        finally
        {
            TryDelete(tempImage);
        }
    }

    public Task<IReadOnlyList<OcrTextLine>> CaptureWordsAsync(TargetWindowInfo? target, Rect? region, CancellationToken cancellationToken)
    {
        return CaptureAsync(target, region, cancellationToken);
    }

    public Task<IReadOnlyList<OcrTextLine>> CaptureImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file was not found.", imagePath);
        }

        return RecognizeImageAsync(imagePath, new Rect(0, 0, 0, 0), cancellationToken);
    }

    private async Task<IReadOnlyList<OcrTextLine>> RecognizeImageAsync(string imagePath, Rect offset, CancellationToken cancellationToken)
    {
        var endpointText = endpointSelector(settings);
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
        {
            return [new OcrTextLine($"Invalid {Name} endpoint: {endpointText}", offset, 1)];
        }

        try
        {
            var request = new OcrRequest(
                Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, cancellationToken)),
                NormalizeLanguage(settings.SourceLanguage),
                Path.GetFileName(imagePath));
            using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            using var response = await HttpClient.PostAsync(endpoint, content, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [new OcrTextLine($"{Name} failed ({(int)response.StatusCode}): {responseText}", offset, 1)];
            }

            return ParseResponse(responseText, offset);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return [new OcrTextLine($"{Name} unavailable: {ex.Message}", offset, 1)];
        }
    }

    private static IReadOnlyList<OcrTextLine> ParseResponse(string json, Rect offset)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var array = FindResultArray(root);
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new List<OcrTextLine>();
        foreach (var item in array.EnumerateArray())
        {
            if (!TryReadText(item, out var text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var bounds = TryReadBounds(item, out var rect) ? rect : Rect.Empty;
            var confidence = TryReadConfidence(item, out var conf) ? conf : 0.80;
            lines.Add(new OcrTextLine(
                text.Trim(),
                new Rect(offset.X + bounds.X, offset.Y + bounds.Y, bounds.Width, bounds.Height),
                confidence));
        }

        return lines
            .Where(line => line.Bounds.Width > 0 && line.Bounds.Height > 0)
            .OrderBy(line => line.Bounds.Top)
            .ThenBy(line => line.Bounds.Left)
            .ToList();
    }

    private static JsonElement FindResultArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        foreach (var name in new[] { "lines", "results", "data", "ocr", "items" })
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                return value;
            }
        }

        return default;
    }

    private static bool TryReadText(JsonElement item, out string text)
    {
        text = "";
        foreach (var name in new[] { "text", "transcription", "label", "value" })
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                text = value.GetString() ?? "";
                return true;
            }
        }

        return false;
    }

    private static bool TryReadConfidence(JsonElement item, out double confidence)
    {
        confidence = 0;
        foreach (var name in new[] { "confidence", "conf", "score", "probability" })
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out confidence))
            {
                if (confidence > 1)
                {
                    confidence /= 100.0;
                }

                return true;
            }
        }

        return false;
    }

    private static bool TryReadBounds(JsonElement item, out Rect bounds)
    {
        bounds = Rect.Empty;
        foreach (var name in new[] { "bounds", "rect", "bbox", "box" })
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty(name, out var value) &&
                TryReadRect(value, out bounds))
            {
                return true;
            }
        }

        foreach (var name in new[] { "points", "polygon" })
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty(name, out var value) &&
                TryReadPolygon(value, out bounds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadRect(JsonElement value, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = value.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.Number)
            .Select(element => element.GetDouble())
            .ToList();
        if (values.Count < 4)
        {
            return false;
        }

        bounds = new Rect(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool TryReadPolygon(JsonElement value, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var points = value.EnumerateArray()
            .Where(point => point.ValueKind == JsonValueKind.Array)
            .Select(point => point.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Number).Select(c => c.GetDouble()).ToList())
            .Where(coords => coords.Count >= 2)
            .Select(coords => new System.Windows.Point(coords[0], coords[1]))
            .ToList();
        if (points.Count == 0)
        {
            return false;
        }

        var left = points.Min(p => p.X);
        var top = points.Min(p => p.Y);
        var right = points.Max(p => p.X);
        var bottom = points.Max(p => p.Y);
        bounds = new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        return true;
    }

    private static Rect ResolveCaptureBounds(TargetWindowInfo? target, Rect? region)
    {
        if (region is { } explicitRegion)
        {
            return Normalize(explicitRegion);
        }

        if (target is not null && NativeMethods.GetWindowRect(target.Handle, out var rect))
        {
            return new Rect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        return new Rect(
            System.Windows.Forms.SystemInformation.VirtualScreen.Left,
            System.Windows.Forms.SystemInformation.VirtualScreen.Top,
            System.Windows.Forms.SystemInformation.VirtualScreen.Width,
            System.Windows.Forms.SystemInformation.VirtualScreen.Height);
    }

    private static Rect Normalize(Rect rect)
    {
        var x = Math.Min(rect.Left, rect.Right);
        var y = Math.Min(rect.Top, rect.Bottom);
        return new Rect(x, y, Math.Abs(rect.Width), Math.Abs(rect.Height));
    }

    private static string NormalizeLanguage(string sourceLanguage)
    {
        return sourceLanguage.ToLowerInvariant() switch
        {
            "ja" or "jpn" or "japan" or "japanese" => "japan",
            "zh" or "chi_sim" or "ch" or "chinese" => "ch",
            "en" or "eng" or "english" => "en",
            _ => "auto"
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for temporary screenshots.
        }
    }

    private sealed record OcrRequest(string Image_Base64, string Language, string FileName);
}
