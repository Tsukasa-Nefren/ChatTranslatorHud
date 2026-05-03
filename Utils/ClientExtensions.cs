using Sharp.Shared.Objects;

namespace ChatTranslatorHud.Utils;

public static class ClientExtensions
{
    extension(IGameClient client)
    {
        public bool IsValidPlayer()
            => client.IsValid && !client.IsFakeClient;
    }
}
