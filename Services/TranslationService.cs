using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using ChatTranslatorHud;

namespace ChatTranslatorHud.Services;

internal class TranslationService(
    ILogger<TranslationService> logger,
    ChatTranslatorConfig config,
    IHttpClientFactory httpClientFactory,
    InterfaceBridge bridge) : ITranslationService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _memoryCache = [];
    private readonly string _cacheDirectory = Path.Combine(bridge.DllPath, "translation_cache");
    private string _currentMapName = "";

    /// <summary>Matches date-like patterns (e.g. 25.05.12) so we can ask DeepL to interpret as yy.mm.dd.</summary>
    private static readonly Regex YyMmDdDatePattern = new(@"\b\d{2}\.\d{2}\.\d{2}\b", RegexOptions.Compiled);
    private const string DateContextYyMmDd = "Dates in this text use yy.mm.dd format (year, month, day).";

    private const int RoundContextBufferMax = 30;
    private const int RoundContextSentencesForTranslation = 10;
    private readonly List<string> _roundContextBuffer = [];
    private readonly object _roundContextLock = new();

    public void OnInit()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public void ClearRoundContext()
    {
        lock (_roundContextLock)
        {
            _roundContextBuffer.Clear();
        }
    }

    /// <summary>Returns up to 3 previous sentences (excluding date-pattern lines) for DeepL context. Chronological order.</summary>
    public string? GetRoundContextForTranslation()
    {
        if (!config.UseRoundContext) return null;
        lock (_roundContextLock)
        {
            if (_roundContextBuffer.Count == 0) return null;
            var eligible = new List<string>(_roundContextBuffer.Count);
            for (var i = _roundContextBuffer.Count - 1; i >= 0 && eligible.Count < RoundContextSentencesForTranslation; i--)
            {
                var s = _roundContextBuffer[i];
                if (!string.IsNullOrWhiteSpace(s) && !YyMmDdDatePattern.IsMatch(s))
                    eligible.Add(s);
            }
            if (eligible.Count == 0) return null;
            eligible.Reverse();
            return string.Join("\n", eligible);
        }
    }

    public void PushRoundMessage(string message)
    {
        if (!config.UseRoundContext || string.IsNullOrWhiteSpace(message)) return;
        lock (_roundContextLock)
        {
            _roundContextBuffer.Add(message);
            if (_roundContextBuffer.Count > RoundContextBufferMax)
                _roundContextBuffer.RemoveAt(0);
        }
    }

    public async ValueTask<string?> TranslateAsync(string text, string? targetLanguage = null, string? context = null)
    {
        if (string.IsNullOrEmpty(targetLanguage))
            return null; // 언어 지정 필수
        targetLanguage = targetLanguage.ToUpperInvariant();
        
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (string.IsNullOrWhiteSpace(config.DeepLApiKey))
        {
            logger.LogWarning("DeepL API key is not set");
            return null;
        }

        if (config.CacheTranslations && TryGetTranslation(text, targetLanguage, out var cached))
        {
            return cached;
        }

        const int maxRetries = 2;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var requestBody = new Dictionary<string, object>
                {
                    { "text", new[] { text } },
                    { "target_lang", targetLanguage }
                };
                var ctx = context;
                if (config.UseDateContext && string.IsNullOrWhiteSpace(ctx) && YyMmDdDatePattern.IsMatch(text))
                    ctx = DateContextYyMmDd;
                if (!string.IsNullOrWhiteSpace(ctx))
                    requestBody["context"] = ctx;

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, config.DeepLApiUrl)
                {
                    Content = content
                };

                request.Headers.Add("Authorization", $"DeepL-Auth-Key {config.DeepLApiKey}");

                var response = await httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("DeepL API request failed: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<DeepLResponse>(responseContent);

                if (result?.Translations is not null && result.Translations.Length > 0)
                {
                    var translation = result.Translations[0];
                    var detectedLanguage = translation.DetectedSourceLanguage?.ToUpperInvariant();
                    
                    if (detectedLanguage == targetLanguage)
                    {
                        return null;
                    }
                    
                    var translatedText = translation.Text;
                    
                    if (config.CacheTranslations && !string.IsNullOrWhiteSpace(translatedText))
                    {
                        CacheTranslation(text, targetLanguage, translatedText);
                        _ = SaveCacheAsync();
                    }
                    
                    return translatedText;
                }

                return null;
            }
            catch (TaskCanceledException)
            {
                if (attempt < maxRetries - 1)
                {
                    logger.LogWarning("Translation timeout, retrying... ({Attempt}/{Max})", attempt + 1, maxRetries);
                    await Task.Delay(500);
                    continue;
                }
                logger.LogWarning("Translation timeout after {Max} attempts: {Text}", maxRetries, text);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during translation: {Text}", text);
                return null;
            }
        }

        return null;
    }

    public async ValueTask<Dictionary<string, string?>> TranslateToMultipleLanguagesAsync(string text, IEnumerable<string> targetLanguages, string? context = null)
    {
        var languageList = targetLanguages.Select(l => l.ToUpperInvariant()).Distinct().ToList();
        var results = languageList.ToDictionary(l => l, l => TryGetTranslation(text, l, out var cached) ? cached : null);
        var toTranslate = results.Where(kvp => kvp.Value is null).Select(kvp => kvp.Key).ToList();
        
        if (toTranslate.Count > 0)
        {
            var translations = await Task.WhenAll(toTranslate.Select(async lang => (lang, await TranslateAsync(text, lang, context))));
            foreach (var (lang, result) in translations)
                results[lang] = result;
        }
        
        return results;
    }

    public bool TryGetTranslation(string originalText, string targetLanguage, out string? translatedText)
    {
        translatedText = null;
        
        if (!config.CacheTranslations || string.IsNullOrWhiteSpace(originalText))
            return false;

        targetLanguage = targetLanguage.ToUpperInvariant();
        
        if (_memoryCache.TryGetValue(originalText, out var langCache) && 
            langCache.TryGetValue(targetLanguage, out translatedText))
        {
            return true;
        }
        
        return false;
    }

    private void CacheTranslation(string originalText, string targetLanguage, string translatedText)
    {
        var langCache = _memoryCache.GetOrAdd(originalText, _ => new ConcurrentDictionary<string, string>());
        langCache[targetLanguage.ToUpperInvariant()] = translatedText;
    }

    public void SetCurrentMap(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            return;

        if (_currentMapName != mapName)
        {
            FlushCache();
            _memoryCache.Clear();
            _currentMapName = mapName;
            _ = LoadMapCacheAsync(mapName);
            logger.LogInformation("Translation cache: Map set to {MapName}", mapName);
        }
    }

    /// <summary>Builds a cache dictionary containing only entries with non-empty translations (excludes failed/empty).</summary>
    private Dictionary<string, Dictionary<string, string>> BuildCacheForSave()
    {
        var cache = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (original, langDict) in _memoryCache)
        {
            var valid = langDict
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (valid.Count > 0)
                cache[original] = valid;
        }
        return cache;
    }

    public void FlushCache()
    {
        if (string.IsNullOrWhiteSpace(_currentMapName) || !config.CacheTranslations || _memoryCache.IsEmpty)
            return;

        try
        {
            var cacheFile = GetCacheFilePath(_currentMapName);
            var cache = BuildCacheForSave();
            if (cache.Count == 0)
                return;

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(cacheFile, json);
            
            var totalTranslations = cache.Values.Sum(v => v.Count);
            logger.LogInformation("Translation cache: Saved {Count} items ({Translations} translations) for {Map}", 
                cache.Count, totalTranslations, _currentMapName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush cache for map: {MapName}", _currentMapName);
        }
    }

    private async Task SaveCacheAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentMapName) || !config.CacheTranslations || _memoryCache.IsEmpty)
            return;

        var mapName = _currentMapName;
        var cache = BuildCacheForSave();
        if (cache.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var cacheFile = GetCacheFilePath(mapName);
                var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(cacheFile, json);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to save cache asynchronously for map: {MapName}", mapName);
            }
        });
    }

    private async Task LoadMapCacheAsync(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName) || !config.CacheTranslations)
            return;

        var cacheFile = GetCacheFilePath(mapName);
        if (!File.Exists(cacheFile))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(cacheFile);
            
            try
            {
                var cache = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                if (cache is not null)
                {
                    foreach (var (original, translations) in cache)
                    {
                        var langCache = _memoryCache.GetOrAdd(original, _ => new ConcurrentDictionary<string, string>());
                        foreach (var (lang, translated) in translations)
                        {
                            langCache[lang] = translated;
                        }
                    }
                    
                    var totalTranslations = cache.Values.Sum(v => v.Count);
                    logger.LogInformation("Translation cache loaded: {MapName} ({Count} items, {Translations} translations)", 
                        mapName, cache.Count, totalTranslations);
                    return;
                }
            }
            catch (JsonException ex)
            {
                logger.LogDebug(ex, "Failed to deserialize cache as new format, trying old format");
            }
            
            var oldCache = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (oldCache is not null)
            {
                foreach (var (original, translated) in oldCache)
                {
                    var langCache = _memoryCache.GetOrAdd(original, _ => new ConcurrentDictionary<string, string>());
                    langCache["EN"] = translated;
                }
                
                logger.LogInformation("Translation cache migrated from old format: {MapName} ({Count} items)", 
                    mapName, oldCache.Count);
                
                _ = SaveCacheAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load map cache: {MapName}", mapName);
        }
    }

    private string GetCacheFilePath(string mapName)
    {
        var safeMapName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDirectory, $"{safeMapName}.json");
    }

    public void OnShutdown()
    {
        FlushCache();
    }

    private class DeepLResponse
    {
        [JsonPropertyName("translations")]
        public DeepLTranslation[]? Translations { get; set; }
    }

    private class DeepLTranslation
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        
        [JsonPropertyName("detected_source_language")]
        public string? DetectedSourceLanguage { get; set; }
    }
}
