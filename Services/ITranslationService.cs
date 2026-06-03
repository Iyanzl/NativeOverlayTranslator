namespace NativeOverlayTranslator.Services;

public interface ITranslationService
{
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken);
}
