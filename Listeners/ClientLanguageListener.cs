using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using ChatTranslatorHud.Services;
using ChatTranslatorHud.Utils;

#pragma warning disable CS9113

namespace ChatTranslatorHud.Listeners;

internal interface IClientLanguageListener : IModule;

internal class ClientLanguageListener(
    ILogger<ClientLanguageListener> _logger,
    IPlayerTranslationService playerTranslationService,
    InterfaceBridge bridge) : IClientLanguageListener, IClientListener
{

    #region IModule

    public void OnInit()
    {
        bridge.ClientManager.InstallClientListener(this);
    }

    public void OnShutdown()
    {
        bridge.ClientManager.RemoveClientListener(this);
    }

    #endregion

    #region IClientListener

    public int ListenerVersion => IClientListener.ApiVersion;
    public int ListenerPriority => 0;

    public void OnClientPutInServer(IGameClient client)
    {
        if (!client.IsValidPlayer()) return;
        bridge.ModSharp.PushTimer(() => QueryPlayerLanguage(client), 1.0, GameTimerFlags.StopOnMapEnd);
    }

    public void OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
    {
    }

    private void QueryPlayerLanguage(IGameClient client)
    {
        if (!client.IsValidPlayer() || !client.IsInGame) return;
        bridge.ClientManager.QueryConVar(client, "cl_language", OnLanguageQueryResult);
    }

    private void OnLanguageQueryResult(IGameClient client, QueryConVarValueStatus status, string name, string value)
    {
        if (status != QueryConVarValueStatus.ValueIntact)
            return;
        
        if (!client.IsValid || !client.SteamId.IsValidUserId())
            return;
        
        playerTranslationService.SetPlayerLanguage(client, value);
    }

    #endregion
}
