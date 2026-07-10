using System.Text.RegularExpressions;
using System.Windows;
using NativeOverlayTranslator.Models;

namespace NativeOverlayTranslator.Services;

public static partial class HoverTextSelector
{
    private const int MaxPhraseWords = 5;
    private const int MaxPhraseCharacters = 40;

    public static OcrTextLine SelectHoverText(OcrTextLine line, double mouseX, HoverMode mode)
    {
        var text = TextTranslationFilter.Normalize(line.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return EmptyLine();
        }

        return mode switch
        {
            HoverMode.Word => SelectWord(line, text, mouseX),
            HoverMode.Phrase => SelectPhrase(line, text, mouseX),
            HoverMode.Sentence => new OcrTextLine(text, line.Bounds, line.Confidence),
            _ => new OcrTextLine(text, line.Bounds, line.Confidence)
        };
    }

    private static OcrTextLine SelectWord(OcrTextLine line, string text, double mouseX)
    {
        var matches = WordTokenRegex().Matches(text);
        if (matches.Count <= 1)
        {
            return IsUsefulWord(text) ? new OcrTextLine(text, line.Bounds, line.Confidence) : new OcrTextLine("", Rect.Empty, 0);
        }

        var match = FindClosestMatch(line.Bounds, text, matches, mouseX, 6);
        if (match is null)
        {
            return EmptyLine();
        }

        var token = match.Value.Trim();
        return IsUsefulWord(token)
            ? CreateSelection(line, text, match.Index, match.Length, token)
            : EmptyLine();
    }

    private static OcrTextLine SelectPhrase(OcrTextLine line, string text, double mouseX)
    {
        var phrases = PhraseRegex().Matches(text);
        var phrase = FindClosestMatch(line.Bounds, text, phrases, mouseX, 10);
        if (phrase is null)
        {
            return EmptyLine();
        }

        var span = TrimSpan(text, phrase.Index, phrase.Length);
        if (span.Length == 0)
        {
            return EmptyLine();
        }

        var phraseText = text.Substring(span.Start, span.Length);
        var words = WordTokenRegex().Matches(phraseText);
        if (words.Count <= MaxPhraseWords && phraseText.Length <= MaxPhraseCharacters)
        {
            return CreateSelection(line, text, span.Start, span.Length, phraseText);
        }

        if (words.Count <= 1)
        {
            return SelectCharacterWindow(line, text, span, mouseX);
        }

        var absoluteMatches = words
            .Cast<Match>()
            .Select(match => new TextSpan(span.Start + match.Index, match.Length))
            .ToList();
        var hoveredIndex = FindClosestSpanIndex(line.Bounds, text, absoluteMatches, mouseX);
        var startIndex = Math.Clamp(hoveredIndex - MaxPhraseWords / 2, 0, Math.Max(0, absoluteMatches.Count - MaxPhraseWords));
        var endIndex = Math.Min(absoluteMatches.Count - 1, startIndex + MaxPhraseWords - 1);
        var start = absoluteMatches[startIndex].Start;
        var end = absoluteMatches[endIndex].End;
        var selected = text[start..end];
        return CreateSelection(line, text, start, end - start, selected);
    }

    private static OcrTextLine SelectCharacterWindow(OcrTextLine line, string text, TextSpan span, double mouseX)
    {
        const int maxCharacters = 12;
        var ratio = line.Bounds.Width <= 0 ? 0.5 : Math.Clamp((mouseX - line.Bounds.Left) / line.Bounds.Width, 0, 1);
        var pointerIndex = Math.Clamp((int)Math.Round(ratio * text.Length), span.Start, span.End - 1);
        var start = Math.Clamp(pointerIndex - maxCharacters / 2, span.Start, Math.Max(span.Start, span.End - maxCharacters));
        var length = Math.Min(maxCharacters, span.End - start);
        return CreateSelection(line, text, start, length, text.Substring(start, length));
    }

    private static Match? FindClosestMatch(Rect bounds, string text, MatchCollection matches, double mouseX, double guard)
    {
        Match? closest = null;
        var closestDistance = double.MaxValue;
        foreach (Match match in matches)
        {
            var matchBounds = CalculateBounds(bounds, text, match.Index, match.Length);
            var distance = mouseX < matchBounds.Left
                ? matchBounds.Left - mouseX
                : mouseX > matchBounds.Right
                    ? mouseX - matchBounds.Right
                    : 0;
            if (distance < closestDistance)
            {
                closest = match;
                closestDistance = distance;
            }
        }

        return closestDistance <= guard ? closest : null;
    }

    private static int FindClosestSpanIndex(Rect bounds, string text, IReadOnlyList<TextSpan> spans, double mouseX)
    {
        return spans
            .Select((span, index) => new { index, bounds = CalculateBounds(bounds, text, span.Start, span.Length) })
            .OrderBy(item => mouseX < item.bounds.Left
                ? item.bounds.Left - mouseX
                : mouseX > item.bounds.Right
                    ? mouseX - item.bounds.Right
                    : 0)
            .First().index;
    }

    private static OcrTextLine CreateSelection(OcrTextLine line, string text, int start, int length, string selectedText)
    {
        return new OcrTextLine(selectedText.Trim(), CalculateBounds(line.Bounds, text, start, length), line.Confidence);
    }

    private static Rect CalculateBounds(Rect sourceBounds, string text, int start, int length)
    {
        var totalWeight = text.Sum(GetCharWeight);
        if (totalWeight <= 0 || sourceBounds.Width <= 0)
        {
            return sourceBounds;
        }

        var startWeight = text.Take(start).Sum(GetCharWeight);
        var selectionWeight = text.Skip(start).Take(length).Sum(GetCharWeight);
        var left = sourceBounds.Left + sourceBounds.Width * startWeight / totalWeight;
        var width = Math.Max(4, sourceBounds.Width * selectionWeight / totalWeight);
        return new Rect(left, sourceBounds.Top, width, sourceBounds.Height);
    }

    private static TextSpan TrimSpan(string text, int start, int length)
    {
        var end = start + length;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return new TextSpan(start, end - start);
    }

    private static OcrTextLine EmptyLine()
    {
        return new OcrTextLine("", Rect.Empty, 0);
    }

    private static bool IsUsefulWord(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 2)
        {
            return true;
        }

        return string.Equals(trimmed, "A", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "I", StringComparison.OrdinalIgnoreCase);
    }

    private static double GetCharWeight(char c)
    {
        if (char.IsWhiteSpace(c))
        {
            return 0.55;
        }

        if (char.IsPunctuation(c) || char.IsSymbol(c))
        {
            return 0.45;
        }

        return c is 'i' or 'l' or 'I' ? 0.55 : 1.0;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’_-][\p{L}\p{N}]+)*", RegexOptions.Compiled)]
    private static partial Regex WordTokenRegex();

    [GeneratedRegex(@"[^:：;；,.!?。！？|/\\\r\n]+", RegexOptions.Compiled)]
    private static partial Regex PhraseRegex();

    private readonly record struct TextSpan(int Start, int Length)
    {
        public int End => Start + Length;
    }
}
