using Sharp.Shared.Objects;

namespace ChatTranslatorHud.Utils;

public static class ClientExtensions
{
    public static bool IsValidPlayer(this IGameClient client) 
        => client.IsValid && !client.IsFakeClient;
}
