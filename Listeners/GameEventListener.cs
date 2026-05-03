using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using ChatTranslatorHud.Services;
using ChatTranslatorHud.Utils;
using ChatTranslatorHud;

namespace ChatTranslatorHud.Listeners;

internal interface IGameEventListener : IModule;

internal class GameEventListener(
    ILogger<GameEventListener> logger,
    ITranslationService translationService,
    IHudDisplayService hudDisplayService,
    IPlayerTranslationService playerTranslationService,
    IPlayerPreferenceService preferenceService,
    ChatTranslatorConfig config,
    InterfaceBridge bridge) : IGameListener, IGameEventListener
{
    // ConsoleSay dedup: 동일 메시지가 DedupWindowSeconds 내에 연속으로 오면 두 번째 이후 무시.
    // 맵의 여러 point_servercommand 가 같은 틱에 같은 say 를 발화하는 경우 방지 + 중복 DeepL 호출 차단.
    // countdown 은 초 단위로 서로 다른 내용 ("| 5 |", "| 4 |" ...) 이라 영향 없음.
    private readonly Dictionary<string, DateTime> _recentSayTimestamps = new(StringComparer.Ordinal);
    private readonly object _recentSayLock = new();
    private const double DedupWindowSeconds = 0.1;
    private const int DedupCleanupThreshold = 64;

    #region IModule

    public void OnInit()
    {
        bridge.ModSharp.InstallGameListener(this);
    }

    public void OnShutdown()
    {
        hudDisplayService.Stop();
        bridge.ModSharp.RemoveGameListener(this);
        lock (_recentSayLock) _recentSayTimestamps.Clear();
    }

    #endregion

    #region IGameListener

    public void OnServerActivate()
    {
        try
        {
            var mapName = bridge.ModSharp.GetMapName();
            if (!string.IsNullOrWhiteSpace(mapName))
            {
                translationService.SetCurrentMap(mapName);
            }
            
            hudDisplayService.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting current map");
        }
    }

    public void OnGameShutdown()
    {
        try
        {
            translationService.FlushCache();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error flushing cache");
        }
    }

    public void OnRoundRestart()
    {
        try
        {
            translationService.ClearRoundContext();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error clearing round context");
        }
    }

    public void OnRoundRestarted()
    {
    }

    public ECommandAction ConsoleSay(string message)
    {
        if (!config.EnableTranslation || string.IsNullOrWhiteSpace(message))
            return ECommandAction.Skipped;

        if (message.StartsWith("[Translated]"))
            return ECommandAction.Skipped;

        // 동일 메시지 rapid-duplicate dedup (100ms 창). 중복 HUD 표시 + 중복 DeepL 호출 차단.
        var now = DateTime.UtcNow;
        lock (_recentSayLock)
        {
            if (_recentSayTimestamps.TryGetValue(message, out var lastTime)
                && (now - lastTime).TotalSeconds < DedupWindowSeconds)
            {
                return ECommandAction.Stopped; // 원본 콘솔 say 는 그대로 차단 (이미 번역본이 표시 중)
            }
            _recentSayTimestamps[message] = now;

            // 주기적 cleanup: 10초 이전 엔트리 제거
            if (_recentSayTimestamps.Count > DedupCleanupThreshold)
            {
                var cutoff = now.AddSeconds(-10);
                List<string>? toRemove = null;
                foreach (var kv in _recentSayTimestamps)
                {
                    if (kv.Value < cutoff)
                        (toRemove ??= []).Add(kv.Key);
                }
                if (toRemove is not null)
                {
                    foreach (var k in toRemove) _recentSayTimestamps.Remove(k);
                }
            }
        }

        _ = ProcessConsoleSayAsync(message);

        return ECommandAction.Stopped;
    }

    private async Task ProcessConsoleSayAsync(string message)
    {
        try
        {
            await ProcessPerPlayerTranslationAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing console say message: {Message}", message);
            SendFallbackOriginal(message);
        }
    }

    private void SendFallbackOriginal(string message)
    {
        try
        {
            bridge.ModSharp.PushTimer(() =>
            {
                foreach (var client in bridge.ClientManager.GetGameClients(inGame: true))
                {
                    if (!client.IsValid || !client.IsInGame) continue;
                    var filter = new RecipientFilter(client);
                    bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.Green} {message}", filter);
                }
            }, 0.001);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send fallback original message");
        }
    }

    private async Task ProcessPerPlayerTranslationAsync(string message)
    {
        List<IGameClient> clientBuffer = new(64);
        try
        {
            foreach (var client in bridge.ClientManager.GetGameClients(inGame: true))
            {
                if (client.IsValidPlayer())
                    clientBuffer.Add(client);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get game clients");
            return;
        }
        
        if (clientBuffer.Count == 0)
            return;
        
        if (MessageParser.IsCountdownOnly(message))
        {
            var parseResult = MessageParser.TryParseMessage(message);
            if (parseResult.IsValid)
            {
                var countdownLanguageGroups = new Dictionary<string, List<IGameClient>>();
                foreach (var client in clientBuffer)
                {
                    if (!client.IsValid || !client.IsInGame) continue;
                    var lang = playerTranslationService.GetPlayerLanguage(client);
                    if (!string.IsNullOrEmpty(lang))
                        countdownLanguageGroups.AddToLanguageGroup(lang, client);
                }
                var countdownTranslations = countdownLanguageGroups.Count > 0
                    ? await translationService.TranslateToMultipleLanguagesAsync(message, countdownLanguageGroups.Keys, translationService.GetRoundContextForTranslation())
                    : new Dictionary<string, string?>();
                if (countdownTranslations.Values.Any(t => !string.IsNullOrWhiteSpace(t)))
                    translationService.PushRoundMessage(message);
                var countdownDefaultTranslation = countdownTranslations.Values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? message;

                var numStr = parseResult.Seconds.ToString();
                var perLangPrefixSuffix = new Dictionary<string, (string Prefix, string Suffix)>();
                foreach (var (lang, translatedText) in countdownTranslations)
                {
                    if (string.IsNullOrWhiteSpace(translatedText)) continue;
                    var idx = translatedText.IndexOf(numStr, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        perLangPrefixSuffix[lang] = (translatedText[..idx], translatedText[(idx + numStr.Length)..]);
                    }
                }

                bridge.ModSharp.PushTimer(() =>
                {
                    foreach (var (lang, playerClients) in countdownLanguageGroups)
                    {
                        var translatedText = countdownTranslations.GetValueOrDefault(lang);
                        var isTranslated = !string.IsNullOrWhiteSpace(translatedText);
                        // 번역 결과가 원본과 동일하면 [Translated] 줄은 redundant → Console 로 단일 표시.
                        // 예: "| 10 |" 는 DeepL 이 그대로 반환해서 두 줄 완전 동일 → dedup.
                        var isDistinct = isTranslated && !string.Equals(translatedText, message, StringComparison.Ordinal);
                        foreach (var client in playerClients)
                        {
                            if (!client.IsValid || !client.IsInGame) continue;
                            var filter = new RecipientFilter(client);
                            if (isDistinct)
                            {
                                if (preferenceService.IsOriginalMessageEnabled(client))
                                    bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.White} {message}", filter);
                                bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}[Translated]{ChatColor.Green} {translatedText}", filter);
                            }
                            else
                            {
                                bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.Green} {message}", filter);
                            }
                        }
                    }
                    foreach (var client in clientBuffer)
                    {
                        if (!client.IsValid || !client.IsInGame) continue;
                        var lang = playerTranslationService.GetPlayerLanguage(client);
                        if (string.IsNullOrEmpty(lang) || !countdownLanguageGroups.ContainsKey(lang))
                        {
                            var filter = new RecipientFilter(client);
                            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.Green} {message}", filter);
                        }
                    }

                    hudDisplayService.AddCountdown(
                        parseResult.Prefix,
                        parseResult.Seconds,
                        parseResult.Suffix,
                        parseResult.IsMmss,
                        parseResult.Unit,
                        message,
                        perLangPrefixSuffix.Count > 0 ? perLangPrefixSuffix : null
                    );
                    // AddMessage 는 호출 안함 — AddCountdown 이 이미 HUD 에 카운트다운을 표시하고 있어서
                    // 정적 번역 메시지를 같이 추가하면 "| 5 | | 3 |" 처럼 두 텍스트가 겹쳐 보임.
                    // countdownDefaultTranslation 은 채팅 메시지로 이미 위에서 출력됨.
                }, 0.001);
                return;
            }
        }
        
        var languageGroups = new Dictionary<string, List<IGameClient>>();
        foreach (var client in clientBuffer)
        {
            var lang = playerTranslationService.GetPlayerLanguage(client);
            if (!string.IsNullOrEmpty(lang))
                languageGroups.AddToLanguageGroup(lang, client);
        }
        
        if (languageGroups.Count == 0)
        {
            SendFallbackOriginal(message);
            return;
        }

        var roundContext = translationService.GetRoundContextForTranslation();
        var translations = await translationService.TranslateToMultipleLanguagesAsync(
            message,
            languageGroups.Keys,
            roundContext
        );
        if (translations.Values.Any(t => !string.IsNullOrWhiteSpace(t)))
            translationService.PushRoundMessage(message);

        var defaultTranslation = translations.Values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? message;
        
        bridge.ModSharp.PushTimer(() =>
        {
            try
            {
                foreach (var (lang, playerClients) in languageGroups)
                {
                    var translatedText = translations.GetValueOrDefault(lang);
                    var isTranslated = !string.IsNullOrWhiteSpace(translatedText);
                    // 번역 결과가 원본과 동일하면 [Translated] 줄 생략 (위 countdown 과 동일한 dedup)
                    var isDistinct = isTranslated && !string.Equals(translatedText, message, StringComparison.Ordinal);
                    foreach (var client in playerClients)
                    {
                        if (!client.IsValid || !client.IsInGame) continue;
                        var filter = new RecipientFilter(client);
                        if (isDistinct)
                        {
                            if (preferenceService.IsOriginalMessageEnabled(client))
                                bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.White} {message}", filter);
                            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}[Translated]{ChatColor.Green} {translatedText}", filter);
                        }
                        else
                        {
                            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.Green} {message}", filter);
                        }
                    }
                }
                
                hudDisplayService.AddMessage(defaultTranslation, message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in per-player translation: {Message}", message);
            }
        }, 0.001);
    }

    public int ListenerVersion => IGameListener.ApiVersion;
    public int ListenerPriority => 0;

    #endregion
}
