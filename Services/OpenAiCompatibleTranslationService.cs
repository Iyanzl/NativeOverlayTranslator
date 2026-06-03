using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public sealed class OpenAiCompatibleTranslationService(AppSettings settings) : ITranslationService
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        if (string.IsNullOrWhiteSpace(settings.TranslationEndpoint))
        {
            return text;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.TranslationEndpoint);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var isShortUiText = normalized.Length <= 80 && !text.Contains('\n');
        var prompt = isShortUiText
            ? $"""
              Translate this software UI text into {targetLanguage}. Source language: {sourceLanguage}.
              Return only the translation. Correct obvious OCR spacing/noise first. Split stuck UI words such as GenericTemplate into Generic Template before translating.
              Text: {text}
              """
            : $"""
              Task: localize software UI text into {targetLanguage}.
              Source language: {sourceLanguage}.

              Rules:
              - Return only the translated UI text. Do not explain.
              - Preserve product names, file paths, hotkeys, gamepad buttons, numbers, version strings, and punctuation structure.
              - Keep common technical terms concise and natural for software settings panels.
              - If OCR produced obvious spelling noise, silently correct it before translating.
              - If OCR joined English UI words, split them before translating, for example GenericTemplate means Generic Template.
              - Preserve line breaks and item order.

              Text:
              {text}
              """;

        request.Content = JsonContent.Create(new
        {
            model = settings.Model,
            messages = new object[]
            {
                new { role = "system", content = "You are a precise software UI localizer. Output only the translated text." },
                new { role = "user", content = prompt }
            },
            temperature = 0.1,
            max_tokens = Math.Clamp(normalized.Length * 4 + 96, 96, 1536)
        });

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var translated = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();

            return string.IsNullOrWhiteSpace(translated) ? text : translated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return $"[Translation canceled] {text}";
        }
        catch (Exception ex)
        {
            return $"[Translation failed: {ex.Message}] {text}";
        }
    }
}
