using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using Sharp.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.Types;
using Sharp.Shared.Objects;
using Sharp.Modules.MenuManager.Shared;
using ChatTranslatorHud;
using ChatTranslatorHud.Utils;

namespace ChatTranslatorHud.Services;

internal class HudDisplayService(
    ILogger<HudDisplayService> logger,
    InterfaceBridge bridge,
    ITranslationService translationService,
    IPlayerTranslationService playerTranslationService,
    IPlayerPreferenceService preferenceService,
    ISharedSystem sharedSystem) : IHudDisplayService
{
    private readonly ConcurrentQueue<ActiveHudMessage> _activeMessages = [];
    private readonly object _lockObject = new();
    private ActiveHudMessage? _currentCountdown;
    private bool _isRunning = false;
    private Guid? _timerId;
    
    private IModSharpModuleInterface<IMenuManager>? _menuManager;
    
    private readonly List<ActiveHudMessage> _validMessagesBuffer = new(8);
    private readonly List<ActiveHudMessage> _displayableMessagesBuffer = new(8);
    private readonly Dictionary<string, string> _languageTextsBuffer = new(8);
    private readonly List<string> _messageTextsBuffer = new(8);

    public void AddMessage(string text, string? originalText = null, int durationSeconds = 3)
    {
        var message = new ActiveHudMessage
        {
            Type = Utils.MessageType.Static,
            ExpiryTime = DateTimeOffset.UtcNow.AddSeconds(durationSeconds),
            StaticText = text,
            OriginalText = originalText ?? text
        };
        _activeMessages.Enqueue(message);
    }

    public void AddCountdown(string prefix, int seconds, string suffix = "", bool isMmss = false, string unit = "", string? originalText = null,
        IReadOnlyDictionary<string, (string Prefix, string Suffix)>? perLangPrefixSuffix = null)
    {
        lock (_lockObject)
        {
            _currentCountdown = new ActiveHudMessage
            {
                Type = Utils.MessageType.Countdown,
                ExpiryTime = DateTimeOffset.UtcNow.AddSeconds(seconds),
                TplPrefix = prefix,
                TplSuffix = suffix,
                TplIsMmss = isMmss,
                TplUnit = unit,
                OriginalText = originalText,
                PerLangPrefixSuffix = perLangPrefixSuffix
            };
        }
    }

    public void OnInit()
    {
    }

    public void OnAllModulesLoaded()
    {
        try
        {
            _menuManager = sharedSystem.GetSharpModuleManager()
                .GetRequiredSharpModuleInterface<IMenuManager>(IMenuManager.Identity);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MenuManager not found. Menu-aware HUD pausing disabled.");
        }
    }

    public void Start()
    {
        _timerId = null;
        _isRunning = true;
        
        _timerId = bridge.ModSharp.PushTimer(() =>
        {
            UpdateHudDisplay();
            return TimerAction.Continue;
        }, 0.1, GameTimerFlags.Repeatable | GameTimerFlags.StopOnMapEnd);
    }

    public void Stop()
    {
        _isRunning = false;
        _timerId = null;
        
        lock (_lockObject)
        {
            while (_activeMessages.TryDequeue(out _)) { }
            _currentCountdown = null;
        }
    }

    private void UpdateHudDisplay()
    {
        if (!_isRunning) return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            
            _validMessagesBuffer.Clear();
            _displayableMessagesBuffer.Clear();

            lock (_lockObject)
            {
                while (_activeMessages.TryDequeue(out var message))
                {
                    if (now < message.ExpiryTime)
                    {
                        _validMessagesBuffer.Add(message);
                        if (message.ShouldDisplay())
                        {
                            _displayableMessagesBuffer.Add(message);
                        }
                    }
                }

                foreach (var message in _validMessagesBuffer)
                {
                    _activeMessages.Enqueue(message);
                }

                if (_currentCountdown is not null && 
                    _currentCountdown.Type == Utils.MessageType.Countdown &&
                    now < _currentCountdown.ExpiryTime)
                {
                    if (_currentCountdown.ShouldDisplay())
                    {
                        _displayableMessagesBuffer.Add(_currentCountdown);
                    }
                }
                else if (_currentCountdown is not null && 
                         _currentCountdown.Type == Utils.MessageType.Countdown &&
                         now >= _currentCountdown.ExpiryTime)
                {
                    _currentCountdown = null;
                }
            }

            if (_displayableMessagesBuffer.Count == 0) return;

            DisplayPerPlayerHud(_displayableMessagesBuffer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during HUD display update");
        }
    }

    private void DisplayPerPlayerHud(List<ActiveHudMessage> messages)
    {
        var clients = bridge.ClientManager.GetGameClients(inGame: true);
        
        _languageTextsBuffer.Clear();
        
        foreach (var client in clients)
        {
            if (!client.IsValidPlayer() || !preferenceService.IsHudEnabled(client))
                continue;
            
            if (_menuManager?.Instance?.IsInMenu(client) == true)
                continue;
            
            var lang = playerTranslationService.GetPlayerLanguage(client) ?? "EN";
            
            if (!_languageTextsBuffer.TryGetValue(lang, out var combinedString))
            {
                _messageTextsBuffer.Clear();
                
                foreach (var message in messages)
                {
                    string text;
                    if (message.Type == Utils.MessageType.Static)
                    {
                        if (message.OriginalText is not null && 
                            translationService.TryGetTranslation(message.OriginalText, lang, out var translated) &&
                            !string.IsNullOrWhiteSpace(translated))
                        {
                            text = translated;
                        }
                        else
                        {
                            text = message.GetCurrentText();
                        }
                    }
                    else
                    {
                        text = message.GetCurrentText(lang);
                    }
                    
                    if (!string.IsNullOrEmpty(text))
                    {
                        _messageTextsBuffer.Add(text);
                    }
                }
                
                combinedString = _messageTextsBuffer.Count > 0 ? string.Join("\n", _messageTextsBuffer) : "";
                _languageTextsBuffer[lang] = combinedString;
            }
            
            if (!string.IsNullOrEmpty(combinedString))
            {
                var filter = new RecipientFilter(client);
                bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Center, combinedString, filter);
            }
        }
    }

    public void OnShutdown()
    {
        Stop();
    }
}


internal class ActiveHudMessage
{
    private const int CountdownDisplayThreshold = 5;
    
    public Utils.MessageType Type { get; init; }
    public DateTimeOffset ExpiryTime { get; init; }
    public string? StaticText { get; init; }
    public string? TplPrefix { get; init; }
    public string? TplSuffix { get; init; }
    public bool TplIsMmss { get; init; }
    public string? TplUnit { get; init; }
    public string? OriginalText { get; init; }
    public IReadOnlyDictionary<string, (string Prefix, string Suffix)>? PerLangPrefixSuffix { get; init; }

    public bool ShouldDisplay()
    {
        if (Type == Utils.MessageType.Static)
            return true;
        
        var remaining = ExpiryTime - DateTimeOffset.UtcNow;
        var secs = (int)Math.Ceiling(remaining.TotalSeconds);
        return secs <= CountdownDisplayThreshold && secs > 0;
    }

    public string GetCurrentText(string? lang = null)
    {
        if (Type == Utils.MessageType.Static)
        {
            return StaticText ?? "";
        }

        var remaining = ExpiryTime - DateTimeOffset.UtcNow;
        var secs = (int)Math.Ceiling(remaining.TotalSeconds);
        if (secs < 0) secs = 0;

        var langKey = lang?.ToUpperInvariant() ?? "";
        if (PerLangPrefixSuffix is not null && langKey.Length > 0 && PerLangPrefixSuffix.TryGetValue(langKey, out var ps))
        {
            return $"{ps.Prefix}{secs}{ps.Suffix}";
        }

        if (TplIsMmss)
        {
            var m = secs / 60;
            var s = secs % 60;
            return $"{TplPrefix}{m:00}:{s:00}{TplSuffix}";
        }

        var unit = TplUnit ?? "";
        var mid = string.IsNullOrEmpty(unit) ? $"{secs}" : $"{secs} {unit}";
        return $"{TplPrefix}{mid}{TplSuffix}";
    }
}
