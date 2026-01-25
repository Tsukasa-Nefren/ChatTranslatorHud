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
    private readonly List<IGameClient> _clientBuffer = new(64);

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
        _clientBuffer.Clear();
        
        try
        {
            foreach (var client in bridge.ClientManager.GetGameClients(inGame: true))
            {
                if (client.IsValidPlayer())
                    _clientBuffer.Add(client);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get game clients");
            return;
        }
        
        if (_clientBuffer.Count == 0)
            return;
        
        if (MessageParser.IsCountdownOnly(message))
        {
            var parseResult = MessageParser.TryParseMessage(message);
            if (parseResult.IsValid)
            {
                bridge.ModSharp.PushTimer(() =>
                {
                    foreach (var client in _clientBuffer)
                    {
                        if (!client.IsValid || !client.IsInGame) continue;
                        bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.White} {message}", new RecipientFilter(client));
                    }
                    
                    hudDisplayService.AddCountdown(
                        parseResult.Prefix,
                        parseResult.Seconds,
                        parseResult.Suffix,
                        parseResult.IsMmss,
                        parseResult.Unit,
                        message
                    );
                }, 0.001);
                return;
            }
        }
        
        var languageGroups = new Dictionary<string, List<IGameClient>>();
        foreach (var client in _clientBuffer)
        {
            var lang = playerTranslationService.GetPlayerLanguage(client);
            if (!string.IsNullOrEmpty(lang))
                languageGroups.AddToLanguageGroup(lang, client);
        }
        
        if (languageGroups.Count == 0)
            return;
        
        var translations = await translationService.TranslateToMultipleLanguagesAsync(
            message, 
            languageGroups.Keys
        );
        
        var defaultTranslation = translations.Values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? message;
        
        bridge.ModSharp.PushTimer(() =>
        {
            try
            {
                foreach (var (lang, playerClients) in languageGroups)
                {
                    var translatedText = translations.GetValueOrDefault(lang);
                    if (string.IsNullOrWhiteSpace(translatedText))
                        continue;
                    
                    foreach (var client in playerClients)
                    {
                        if (!client.IsValid || !client.IsInGame) continue;
                        var filter = new RecipientFilter(client);
                        if (preferenceService.IsOriginalMessageEnabled(client))
                            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}Console:{ChatColor.White} {message}", filter);
                        bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}[Translated]{ChatColor.Green} {translatedText}", filter);
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
