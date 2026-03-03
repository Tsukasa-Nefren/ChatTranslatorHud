using Ptr.Shared.Hosting;
using Sharp.Shared.Objects;

namespace ChatTranslatorHud.Services;

public interface IPlayerTranslationService : IModule
{
    Task<string?> GetTranslatedTextForPlayerAsync(IGameClient client, string originalText);
    
    void SetPlayerLanguage(IGameClient client, string language);
    
    string? GetPlayerLanguage(IGameClient client);

    void RemovePlayer(IGameClient client);
}
