using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using Sharp.Shared.Definition;
using Sharp.Shared.Objects;
using Sharp.Shared.Enums;
using Sharp.Shared.Types;
using ChatTranslatorHud.Utils;

#pragma warning disable CS9113

namespace ChatTranslatorHud.Services;

internal class PlayerTranslationService(
    ILogger<PlayerTranslationService> _logger,
    ITranslationService translationService,
    InterfaceBridge bridge,
    ChatTranslatorConfig _config) : IPlayerTranslationService
{
    private readonly ConcurrentDictionary<ulong, string> _playerLanguages = new();
    
    private static readonly Dictionary<string, string> SteamToDeepL = new(StringComparer.OrdinalIgnoreCase)
    {
        { "english", "EN" },
        { "korean", "KO" },
        { "koreana", "KO" },
        { "schinese", "ZH" },
        { "tchinese", "ZH" },
        { "japanese", "JA" },
        { "russian", "RU" },
        { "german", "DE" },
        { "french", "FR" },
        { "spanish", "ES" },
        { "latam", "ES" },
        { "portuguese", "PT-PT" },
        { "brazilian", "PT-BR" },
        { "italian", "IT" },
        { "polish", "PL" },
        { "turkish", "TR" },
        { "dutch", "NL" },
        { "danish", "DA" },
        { "finnish", "FI" },
        { "norwegian", "NB" },
        { "swedish", "SV" },
        { "czech", "CS" },
        { "hungarian", "HU" },
        { "romanian", "RO" },
        { "bulgarian", "BG" },
        { "greek", "EL" },
        { "ukrainian", "UK" },
        { "indonesian", "ID" },
        { "thai", "TH" },
        { "vietnamese", "VI" }
    };


    public void OnInit()
    {
    }

    public void OnShutdown()
    {
        _playerLanguages.Clear();
    }

    public void SetPlayerLanguage(IGameClient client, string steamLanguage)
    {
        if (!client.IsValidPlayer()) return;
        if (SteamToDeepL.TryGetValue(steamLanguage, out var deepLLang))
            _playerLanguages[(ulong)client.SteamId] = deepLLang;
    }

    public string? GetPlayerLanguage(IGameClient client)
    {
        if (!client.IsValidPlayer()) return null;
        return _playerLanguages.GetValueOrDefault((ulong)client.SteamId, "EN");
    }

    public async Task<string?> GetTranslatedTextForPlayerAsync(IGameClient client, string originalText)
    {
        var targetLang = GetPlayerLanguage(client);
        return await translationService.TranslateAsync(originalText, targetLang);
    }

    public async Task SendTranslatedMessageToAllAsync(string originalText)
    {
        var languageGroups = new Dictionary<string, List<IGameClient>>();
        foreach (var client in bridge.ClientManager.GetGameClients(inGame: true))
        {
            if (!client.IsValidPlayer()) continue;
            var lang = GetPlayerLanguage(client);
            if (!string.IsNullOrEmpty(lang))
                languageGroups.AddToLanguageGroup(lang, client);
        }
        
        if (languageGroups.Count == 0)
            return;
        
        var translations = await translationService.TranslateToMultipleLanguagesAsync(
            originalText, 
            languageGroups.Keys
        );
        
        bridge.ModSharp.PushTimer(() =>
        {
            foreach (var (lang, playerClients) in languageGroups)
            {
                var translatedText = translations.GetValueOrDefault(lang);
                if (string.IsNullOrWhiteSpace(translatedText))
                    continue;
                
                foreach (var client in playerClients)
                {
                    if (!client.IsValid || !client.IsInGame) continue;
                    bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, $" {ChatColor.LightRed}[Translated]{ChatColor.Green} {translatedText}", new RecipientFilter(client));
                }
            }
        }, 0.001);
    }
}
