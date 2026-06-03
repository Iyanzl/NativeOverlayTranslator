using System.Text.RegularExpressions;

namespace NativeOverlayTranslator.Services;

public static partial class TextTranslationFilter
{
    public static bool ShouldTranslate(string text, string sourceLanguage, string targetLanguage)
    {
        var normalized = Normalize(text);
        if (normalized.Length < 2)
        {
            return false;
        }

        if (!HasLetter(normalized) || LooksLikeNoise(normalized))
        {
            return false;
        }

        var source = NormalizeSourceLanguage(sourceLanguage);
        if (source == "zh")
        {
            return CjkRegex().IsMatch(normalized);
        }

        if (IsChineseTarget(targetLanguage) && source == "auto" && IsAlreadyChinese(normalized))
        {
            return false;
        }

        return source switch
        {
            "en" => HasEnglish(normalized),
            "ja" => HasJapaneseKana(normalized) || HasJapaneseKanjiCandidate(normalized),
            _ => HasEnglish(normalized) || HasJapaneseKana(normalized)
        };
    }

    public static string Normalize(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsChineseTarget(string targetLanguage)
    {
        return targetLanguage.Contains("Chinese", StringComparison.OrdinalIgnoreCase)
            || targetLanguage.Contains("zh", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSourceLanguage(string sourceLanguage)
    {
        return sourceLanguage.ToLowerInvariant() switch
        {
            "english" or "eng" or "en" => "en",
            "japanese" or "jpn" or "ja" => "ja",
            "chinese" or "chi_sim" or "zh" => "zh",
            _ => "auto"
        };
    }

    private static bool IsAlreadyChinese(string text)
    {
        var cjk = CjkRegex().Matches(text).Count;
        var kana = KanaRegex().Matches(text).Count;
        var latin = LatinRegex().Matches(text).Count;
        return cjk >= 2 && kana == 0 && latin == 0;
    }

    private static bool HasLetter(string text)
    {
        return LatinRegex().IsMatch(text) || CjkRegex().IsMatch(text) || KanaRegex().IsMatch(text);
    }

    private static bool HasEnglish(string text) => LatinRegex().IsMatch(text);

    private static bool HasJapaneseKana(string text) => KanaRegex().IsMatch(text);

    private static bool HasJapaneseKanjiCandidate(string text)
    {
        return CjkRegex().IsMatch(text) && !HasEnglish(text);
    }

    private static bool LooksLikeNoise(string text)
    {
        var letters = text.Count(char.IsLetter);
        var symbols = text.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
        return letters == 0 || symbols > letters * 2;
    }

    [GeneratedRegex(@"\p{IsBasicLatin}*[A-Za-z]\p{IsBasicLatin}*")]
    private static partial Regex LatinRegex();

    [GeneratedRegex(@"[\u3040-\u30ff]")]
    private static partial Regex KanaRegex();

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex CjkRegex();
}
