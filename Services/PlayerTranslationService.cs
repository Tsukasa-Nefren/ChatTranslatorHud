using System.Collections.Concurrent;
using Ptr.Shared.Hosting;
using Sharp.Shared.Definition;
using Sharp.Shared.Objects;
using Sharp.Shared.Enums;
using Sharp.Shared.Types;
using ChatTranslatorHud.Utils;

namespace ChatTranslatorHud.Services;

internal class PlayerTranslationService(ITranslationService translationService) : IPlayerTranslationService
{
    private readonly ConcurrentDictionary<ulong, string> _playerLanguages = [];
    
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

    public void RemovePlayer(IGameClient client)
    {
        _playerLanguages.TryRemove((ulong)client.SteamId, out _);
    }

    public async Task<string?> GetTranslatedTextForPlayerAsync(IGameClient client, string originalText)
    {
        var targetLang = GetPlayerLanguage(client);
        return await translationService.TranslateAsync(originalText, targetLang);
    }
}
