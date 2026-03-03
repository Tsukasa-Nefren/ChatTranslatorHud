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
    #region IModule

    public void OnInit()
    {
        bridge.ModSharp.InstallGameListener(this);
    }

    public void OnShutdown()
    {
        hudDisplayService.Stop();
        bridge.ModSharp.RemoveGameListener(this);
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
                        foreach (var client in playerClients)
                        {
                            if (!client.IsValid || !client.IsInGame) continue;
                            var filter = new RecipientFilter(client);
                            if (isTranslated && preferenceService.IsOriginalMessageEnabled(client))
                                bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.White} {message}", filter);
                            if (isTranslated)
                                bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}[Translated]{ChatColor.Green} {translatedText}", filter);
                            if (!isTranslated)
                                bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.Green} {message}", filter);
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
                    hudDisplayService.AddMessage(countdownDefaultTranslation, message);
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
            return;
        
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
                    foreach (var client in playerClients)
                    {
                        if (!client.IsValid || !client.IsInGame) continue;
                        var filter = new RecipientFilter(client);
                        if (isTranslated && preferenceService.IsOriginalMessageEnabled(client))
                            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.White} {message}", filter);
                        if (isTranslated)
                            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}[Translated]{ChatColor.Green} {translatedText}", filter);
                        if (!isTranslated)
                            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.Green} {message}", filter);
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
