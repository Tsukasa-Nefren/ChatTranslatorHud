using Ptr.Shared.Hosting;
using Sharp.Shared.Objects;

namespace ChatTranslatorHud.Services;

public interface IPlayerPreferenceService : IModule
{
    bool IsHudEnabled(IGameClient client);
    
    bool ToggleHud(IGameClient client);
    
    void SetHudEnabled(IGameClient client, bool enabled);
    
    bool IsOriginalMessageEnabled(IGameClient client);
    
    bool ToggleOriginalMessage(IGameClient client);
    
    void SetOriginalMessageEnabled(IGameClient client, bool enabled);
}
