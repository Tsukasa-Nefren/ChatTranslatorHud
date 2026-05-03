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

    // Save coalescing: 매 번역마다 SaveCacheAsync 가 호출되면 전체 파일을 덮어쓰는 게 낭비라서
    // 1초 debounce + single-writer lock 으로 묶음. dirty 플래그만 set 하고 실제 write 는 1초 후.
    // shutdown 때 FlushCache() 는 동기식이라 dirty 플래그와 무관하게 즉시 write.
    private volatile bool _cacheDirty;
    private long _lastSaveScheduledAtTicks; // DateTimeOffset.UtcNow.Ticks
    private readonly object _saveScheduleLock = new();
    private readonly SemaphoreSlim _saveWriterSem = new(1, 1);

    /// <summary>
    /// YY.MM.DD 형식 날짜 매치 + 캡처 그룹. CJK 에서는 "2025.05.12" = 2025년 5월 12일 (year-month-day) 관습.
    /// DeepL 에 context hint 로 전달해봤지만 유럽식 DD.MM.YY 로 해석되는 케이스가 관측됨 (예: 25.05.12 → 2012년 5월 25일).
    /// 그래서 DeepL 으로 보내기 전에 아예 unambiguous ISO 8601 (20YY-MM-DD) 로 pre-substitute 해서 혼동 원천 차단.
    /// </summary>
    // lookaround 로 앞뒤에 숫자/점이 없는 경우만 매치 → "25.05.2012" 같은 DD.MM.YYYY 의 부분 매치 차단.
    private static readonly Regex YyMmDdDatePattern = new(@"(?<![\d.])(\d{2})\.(\d{2})\.(\d{2})(?![\d.])", RegexOptions.Compiled);
    private const string DateContextYyMmDd = "Dates in this text use yyyy-mm-dd ISO 8601 format.";

    private const int RoundContextBufferMax = 30;
    private const int RoundContextSentencesForTranslation = 10;
    private readonly List<string> _roundContextBuffer = [];
    private readonly object _roundContextLock = new();

    // Circuit breaker: 연속 실패가 임계 도달하면 일정 시간 API 호출 자체를 skip.
    // DeepL 장애나 quota 초과 시 무의미한 호출 폭증을 막아 서버 영향 최소화.
    // 성공 한 번에 _consecutiveFailures = 0 으로 즉시 리셋.
    private const int FailureThresholdForOpen = 5;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromMinutes(1);
    private long _circuitOpenUntilTicks; // 0 = closed; future ticks = open until that time
    private int _consecutiveFailures;

    public void OnInit()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }

        // 이전 비정상 종료로 남은 .tmp 파일 정리 (atomic write 의 임시 파일 잔재)
        try
        {
            foreach (var stale in Directory.EnumerateFiles(_cacheDirectory, "*.tmp"))
            {
                try { File.Delete(stale); } catch (Exception ex) { logger.LogDebug(ex, "Failed to delete stale temp file: {File}", stale); }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to enumerate cache directory for cleanup");
        }
    }

    // 부분 쓰기로 인한 JSON 손상 방지: temp 파일에 쓴 후 원자적 rename.
    // File.Move(overwrite:true) 는 동일 볼륨에서 atomic rename 보장 (.NET 5+).
    private static void AtomicWriteAllText(string path, string contents)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task AtomicWriteAllTextAsync(string path, string contents)
    {
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
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

        // Circuit breaker: open 상태면 API 호출 skip (캐시 hit 은 위에서 이미 처리됨)
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        if (Interlocked.Read(ref _circuitOpenUntilTicks) > nowTicks)
        {
            return null;
        }

        // 번역 시작 시점의 맵 스냅샷 — 비동기 진행 중 맵이 바뀌면 새 맵 캐시 오염 방지로 저장 생략
        var startMap = Volatile.Read(ref _currentMapName);

        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                // CJK (KO/ZH/JA) 관습상 "25.05.12" = 2025년 5월 12일 (year-month-day) 인데
                // DeepL 은 유럽식 DD.MM.YY (2012년 5월 25일) 로 해석하는 경우가 있음.
                // context hint 만으로는 불안정해서 요청 전에 unambiguous ISO 형식으로 치환.
                //   25.05.12 → 2025-05-12  (두자리 연도는 2000+ 으로 가정)
                string textToSend = text;
                if (config.UseDateContext && YyMmDdDatePattern.IsMatch(text))
                {
                    textToSend = YyMmDdDatePattern.Replace(text, m =>
                    {
                        // 2-digit year → assume 2000s (2025 not 1925)
                        // 2080년대 이후 이 코드 동작 중이면 임계값 조정 필요
                        return $"20{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
                    });
                }

                var requestBody = new Dictionary<string, object>
                {
                    { "text", new[] { textToSend } },
                    { "target_lang", targetLanguage }
                };
                var ctx = context;
                if (config.UseDateContext && string.IsNullOrWhiteSpace(ctx) && textToSend != text)
                    ctx = DateContextYyMmDd;  // pre-substitute 했으면 hint 도 같이 (belt-and-suspenders)
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
                    var status = (int)response.StatusCode;

                    // 429 Too Many Requests / 503 Service Unavailable 은 일시적 장애 → exponential backoff 재시도
                    if ((status == 429 || status == 503) && attempt < maxRetries - 1)
                    {
                        var backoffMs = 500 * (int)Math.Pow(2, attempt); // 500, 1000, 2000ms
                        logger.LogWarning("DeepL rate-limited ({Status}), retrying after {Ms}ms ({Attempt}/{Max})",
                            status, backoffMs, attempt + 1, maxRetries);
                        await Task.Delay(backoffMs);
                        continue;
                    }

                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("DeepL API request failed: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    RecordFailure();
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<DeepLResponse>(responseContent);

                // 성공 응답 도달 — circuit 회로 즉시 닫음
                Interlocked.Exchange(ref _consecutiveFailures, 0);

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
                        // 번역 중 맵이 바뀌었으면 새 맵 캐시에 이전 맵의 번역이 섞이는 것을 방지
                        if (Volatile.Read(ref _currentMapName) == startMap)
                        {
                            CacheTranslation(text, targetLanguage, translatedText);
                            SaveCacheAsync();
                        }
                        else
                        {
                            logger.LogDebug("Translation completed after map change, skipping cache save");
                        }
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
                RecordFailure();
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during translation: {Text}", text);
                RecordFailure();
                return null;
            }
        }

        return null;
    }

    /// <summary>연속 실패 카운터 증가; 임계 도달 시 circuit open 으로 일정 시간 API 호출 차단.</summary>
    private void RecordFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        if (failures >= FailureThresholdForOpen)
        {
            var until = DateTimeOffset.UtcNow.Add(CircuitOpenDuration).Ticks;
            Interlocked.Exchange(ref _circuitOpenUntilTicks, until);
            logger.LogWarning("DeepL circuit opened for {Duration}s after {Failures} consecutive failures",
                CircuitOpenDuration.TotalSeconds, failures);
        }
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

        if (Volatile.Read(ref _currentMapName) != mapName)
        {
            FlushCache();
            _memoryCache.Clear();
            Volatile.Write(ref _currentMapName, mapName);
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
            AtomicWriteAllText(cacheFile, json);

            var totalTranslations = cache.Values.Sum(v => v.Count);
            logger.LogInformation("Translation cache: Saved {Count} items ({Translations} translations) for {Map}", 
                cache.Count, totalTranslations, _currentMapName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush cache for map: {MapName}", _currentMapName);
        }
    }

    /// <summary>
    /// 새 번역 생길 때마다 호출. 매번 파일 쓰지 않고 1초 debounce 로 묶어서 single-writer 가 처리.
    /// 동시에 여러 번 호출돼도 타이머는 1개만 활성 (최신 "곧 save 필요" 표시만 갱신).
    /// </summary>
    private void SaveCacheAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentMapName) || !config.CacheTranslations || _memoryCache.IsEmpty)
            return;

        _cacheDirty = true;

        // 1초 이내에 이미 스케줄됐으면 skip — 기존 타이머가 곧 처리할 것
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        lock (_saveScheduleLock)
        {
            if (nowTicks - _lastSaveScheduledAtTicks < TimeSpan.TicksPerSecond)
                return;
            _lastSaveScheduledAtTicks = nowTicks;
        }

        // 1초 후 단일 write 실행
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            await WriteIfDirtyAsync();
        });
    }

    /// <summary>
    /// single-writer 로 dirty 상태일 때만 파일 쓰기. 여러 caller 가 동시에 와도 세마포어로 직렬화.
    /// 한 번 write 가 끝나면 _cacheDirty 클리어 — 그 사이 추가된 번역이 있으면 다음 SaveCacheAsync 가 다시 세팅.
    /// </summary>
    private async Task WriteIfDirtyAsync()
    {
        if (!_cacheDirty) return;
        if (!await _saveWriterSem.WaitAsync(0)) return; // 이미 write 중이면 양보 (기존 write 가 최신 상태 반영)
        try
        {
            _cacheDirty = false; // write 직전에 클리어 — 도중에 추가되는 엔트리는 다음 cycle 에서 처리
            if (string.IsNullOrWhiteSpace(_currentMapName)) return;
            var mapName = _currentMapName;
            var cache = BuildCacheForSave();
            if (cache.Count == 0) return;

            var cacheFile = GetCacheFilePath(mapName);
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            await AtomicWriteAllTextAsync(cacheFile, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save cache for map: {MapName}", _currentMapName);
            _cacheDirty = true; // 실패 시 dirty 복구 — 다음 번역 때 재시도 유발
        }
        finally
        {
            _saveWriterSem.Release();
        }
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

                SaveCacheAsync();
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
