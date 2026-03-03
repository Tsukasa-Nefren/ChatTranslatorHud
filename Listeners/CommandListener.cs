using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using Sharp.Shared;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using ChatTranslatorHud.Services;
using ChatTranslatorHud.Utils;

namespace ChatTranslatorHud.Listeners;

internal interface ICommandListener : IModule;

internal class CommandListener(
    ILogger<CommandListener> logger,
    IPlayerPreferenceService preferenceService,
    InterfaceBridge bridge,
    ISharedSystem sharedSystem) : ICommandListener
{
    private IModSharpModuleInterface<IMenuManager>? _menuManager;
    private IModSharpModuleInterface<ILocalizerManager>? _localizerManager;

    private const string KeyMenuTitle = "chattranslatorhud.menu.title";
    private const string KeyMenuDesc = "chattranslatorhud.menu.description";
    private const string KeyHudDisplay = "chattranslatorhud.menu.hud_display";
    private const string KeyOriginalMessage = "chattranslatorhud.menu.original_message";
    private const string KeyOn = "chattranslatorhud.common.on";
    private const string KeyOff = "chattranslatorhud.common.off";
    private const string KeyHudEnabled = "chattranslatorhud.chat.hud_enabled";
    private const string KeyHudDisabled = "chattranslatorhud.chat.hud_disabled";
    private const string KeyOriginalEnabled = "chattranslatorhud.chat.original_enabled";
    private const string KeyOriginalDisabled = "chattranslatorhud.chat.original_disabled";

    public void OnInit()
    {
        bridge.ClientManager.InstallCommandCallback("thud", OnThudCommand);
    }

    public void OnAllModulesLoaded()
    {
        var moduleManager = sharedSystem.GetSharpModuleManager();
        
        try
        {
            _menuManager = moduleManager.GetRequiredSharpModuleInterface<IMenuManager>(IMenuManager.Identity);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MenuManager not found. Menu feature disabled.");
        }
        
        try
        {
            _localizerManager = moduleManager.GetRequiredSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity);
            _localizerManager.Instance?.LoadLocaleFile("ChatTranslatorHud");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LocalizerManager not found. Using default language.");
        }
    }

    public void OnShutdown()
    {
        bridge.ClientManager.RemoveCommandCallback("thud", OnThudCommand);
    }

    private ILocalizer? GetLocalizer(IGameClient client)
    {
        if (_localizerManager?.Instance is not { } lm)
            return null;
        
        lm.TryGetLocalizer(client, out var localizer);
        return localizer;
    }

    private string Localize(IGameClient client, string key, params object?[] args)
    {
        var localizer = GetLocalizer(client);
        if (localizer is null)
            return key;
        
        return args.Length > 0 ? localizer.Format(key, args) : (localizer.TryGet(key) ?? key);
    }

    private ECommandAction OnThudCommand(IGameClient client, StringCommand command)
    {
        if (!client.IsValidPlayer())
            return ECommandAction.Skipped;

        if (_menuManager?.Instance is not { } menuManager)
        {
            var enabled = preferenceService.ToggleHud(client);
            var message = enabled 
                ? $" {ChatColor.Green}[ChatTranslatorHud]{ChatColor.White} {Localize(client, KeyHudEnabled)}" 
                : $" {ChatColor.Red}[ChatTranslatorHud]{ChatColor.White} {Localize(client, KeyHudDisabled)}";
            
            var filter = new RecipientFilter(client);
            bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, message, filter);
            return ECommandAction.Skipped;
        }

        var menu = CreateSettingsMenu(client);
        menuManager.DisplayMenu(client, menu);
        
        return ECommandAction.Skipped;
    }

    private Menu CreateSettingsMenu(IGameClient client)
    {
        return Menu.Create()
            .Title(c => Localize(c, KeyMenuTitle))
            .DisabledItem(c => Localize(c, KeyMenuDesc))
            .Item((IGameClient c, ref MenuItemContext ctx) =>
            {
                var status = preferenceService.IsHudEnabled(c)
                    ? Localize(c, KeyOn)
                    : Localize(c, KeyOff);
                ctx.Title = $"{Localize(c, KeyHudDisplay)}: {status}";
                ctx.Action = ctrl =>
                {
                    var newState = preferenceService.ToggleHud(ctrl.Client);
                    var filter = new RecipientFilter(ctrl.Client);
                    var message = newState
                        ? $" {ChatColor.Green}[ChatTranslatorHud]{ChatColor.White} {Localize(ctrl.Client, KeyHudEnabled)}"
                        : $" {ChatColor.Red}[ChatTranslatorHud]{ChatColor.White} {Localize(ctrl.Client, KeyHudDisabled)}";
                    bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, message, filter);
                    ctrl.Refresh();
                };
            })
            .Item((IGameClient c, ref MenuItemContext ctx) =>
            {
                var status = preferenceService.IsOriginalMessageEnabled(c)
                    ? Localize(c, KeyOn)
                    : Localize(c, KeyOff);
                ctx.Title = $"{Localize(c, KeyOriginalMessage)}: {status}";
                ctx.Action = ctrl =>
                {
                    var newState = preferenceService.ToggleOriginalMessage(ctrl.Client);
                    var filter = new RecipientFilter(ctrl.Client);
                    var message = newState
                        ? $" {ChatColor.Green}[ChatTranslatorHud]{ChatColor.White} {Localize(ctrl.Client, KeyOriginalEnabled)}"
                        : $" {ChatColor.Red}[ChatTranslatorHud]{ChatColor.White} {Localize(ctrl.Client, KeyOriginalDisabled)}";
                    bridge.ModSharp.PrintChannelFilter(HudPrintChannel.Chat, message, filter);
                    ctrl.Refresh();
                };
            })
            .Build();
    }
}
