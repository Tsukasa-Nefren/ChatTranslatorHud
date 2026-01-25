using Ptr.Shared.Hosting;

namespace ChatTranslatorHud.Services;

public interface ITranslationService : IModule
{
    ValueTask<string?> TranslateAsync(string text, string? targetLanguage = null);
    
    bool TryGetTranslation(string originalText, string targetLanguage, out string? translatedText);
    
    ValueTask<Dictionary<string, string?>> TranslateToMultipleLanguagesAsync(string text, IEnumerable<string> targetLanguages);
    
    void SetCurrentMap(string mapName);
    void FlushCache();
}
