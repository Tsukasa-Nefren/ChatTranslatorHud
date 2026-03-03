using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using Sharp.Shared;
using Sharp.Shared.Objects;
using Sharp.Modules.ClientPreferences.Shared;
using ChatTranslatorHud.Utils;

namespace ChatTranslatorHud.Services;

internal class PlayerPreferenceService(
    ILogger<PlayerPreferenceService> logger,
    ISharedSystem sharedSystem) : IPlayerPreferenceService
{
    private IModSharpModuleInterface<IClientPreference>? _clientPrefs;
    private IDisposable? _loadCallback;
    
    private const string CookieHudEnabled = "ChatTranslatorHud_HudEnabled";
    private const string CookieOriginalMsgEnabled = "ChatTranslatorHud_OriginalMsgEnabled";
    
    private readonly ConcurrentDictionary<ulong, bool> _hudEnabledCache = [];
    private readonly ConcurrentDictionary<ulong, bool> _originalMessageEnabledCache = [];

    public void OnInit()
    {
    }

    public void OnAllModulesLoaded()
    {
        try
        {
            _clientPrefs = sharedSystem.GetSharpModuleManager()
                .GetOptionalSharpModuleInterface<IClientPreference>(IClientPreference.Identity);
            
            if (_clientPrefs?.Instance is { } cp)
            {
                _loadCallback = cp.ListenOnLoad(OnCookieLoad);
                logger.LogInformation("ClientPreferences connected successfully");
            }
            else
            {
                logger.LogWarning("ClientPreferences module not found. Using in-memory storage only.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to ClientPreferences. Using in-memory storage only.");
        }
    }

    public void OnShutdown()
    {
        _loadCallback?.Dispose();
        _hudEnabledCache.Clear();
        _originalMessageEnabledCache.Clear();
    }

    private void OnCookieLoad(IGameClient client)
    {
        if (_clientPrefs?.Instance is not { } cp)
            return;
        
        var steamId = (ulong)client.SteamId;
        
        if (cp.GetCookie(client.SteamId, CookieHudEnabled) is { } hudCookie)
        {
            _hudEnabledCache[steamId] = hudCookie.GetString() == "1";
        }
        else
        {
            _hudEnabledCache[steamId] = true;
            cp.SetCookie(client.SteamId, CookieHudEnabled, "1");
        }
        
        if (cp.GetCookie(client.SteamId, CookieOriginalMsgEnabled) is { } msgCookie)
        {
            _originalMessageEnabledCache[steamId] = msgCookie.GetString() == "1";
        }
        else
        {
            _originalMessageEnabledCache[steamId] = true;
            cp.SetCookie(client.SteamId, CookieOriginalMsgEnabled, "1");
        }
    }

    private bool GetPreference(IGameClient client, ConcurrentDictionary<ulong, bool> cache)
    {
        if (!client.IsValidPlayer())
            return true;
        return cache.GetValueOrDefault((ulong)client.SteamId, true);
    }

    private void SetPreference(IGameClient client, string cookieName, bool value, ConcurrentDictionary<ulong, bool> cache)
    {
        if (!client.IsValidPlayer())
            return;
        cache[(ulong)client.SteamId] = value;
        if (_clientPrefs?.Instance is { } cp && cp.IsLoaded(client.SteamId))
            cp.SetCookie(client.SteamId, cookieName, value ? "1" : "0");
    }

    private bool TogglePreference(IGameClient client, string cookieName, ConcurrentDictionary<ulong, bool> cache, Func<IGameClient, bool> getter)
    {
        if (!client.IsValidPlayer())
            return true;
        var newValue = !getter(client);
        SetPreference(client, cookieName, newValue, cache);
        return newValue;
    }

    public bool IsHudEnabled(IGameClient client) => GetPreference(client, _hudEnabledCache);
    public bool ToggleHud(IGameClient client) => TogglePreference(client, CookieHudEnabled, _hudEnabledCache, IsHudEnabled);
    public void SetHudEnabled(IGameClient client, bool enabled) => SetPreference(client, CookieHudEnabled, enabled, _hudEnabledCache);

    public bool IsOriginalMessageEnabled(IGameClient client) => GetPreference(client, _originalMessageEnabledCache);
    public bool ToggleOriginalMessage(IGameClient client) => TogglePreference(client, CookieOriginalMsgEnabled, _originalMessageEnabledCache, IsOriginalMessageEnabled);
    public void SetOriginalMessageEnabled(IGameClient client, bool enabled) => SetPreference(client, CookieOriginalMsgEnabled, enabled, _originalMessageEnabledCache);

    public void RemovePlayer(IGameClient client)
    {
        var steamId = (ulong)client.SteamId;
        _hudEnabledCache.TryRemove(steamId, out _);
        _originalMessageEnabledCache.TryRemove(steamId, out _);
    }
}
