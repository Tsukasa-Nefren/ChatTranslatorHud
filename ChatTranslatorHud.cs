using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Ptr.Shared.Hosting;
using Sharp.Shared;
using ChatTranslatorHud.Services;
using ChatTranslatorHud.Listeners;

namespace ChatTranslatorHud;

internal class ChatTranslatorHud : IModSharpModule
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger<ChatTranslatorHud> _logger;
    private readonly IServiceProvider _provider;

    public ChatTranslatorHud(
        ISharedSystem sharedSystem,
        string dllPath,
        string sharpPath,
        Version version,
        IConfiguration configuration,
        bool hotReload)
    {
        var bridge = new InterfaceBridge(sharedSystem, dllPath, sharpPath, version, configuration, hotReload);
        var services = new ServiceCollection();
        
        services.AddSingleton(sharedSystem);
        services.AddSingleton(bridge);
        services.AddSingleton<IModSharpModule>(this);
        services.AddSingleton(sharedSystem.GetLoggerFactory());
        services.AddLogging(x => x.ClearProviders());
        services.AddHttpClient();
        
        _logger = sharedSystem.GetLoggerFactory().CreateLogger<ChatTranslatorHud>();
        
        var config = LoadConfig(bridge.SharpPath);
        services.AddSingleton(config);
        
        services.AddSingleton<ITranslationService, TranslationService>();
        services.AddSingleton<IPlayerPreferenceService, PlayerPreferenceService>();
        services.AddSingleton<IHudDisplayService, HudDisplayService>();
        services.AddSingleton<IPlayerTranslationService, PlayerTranslationService>();
        
        services.AddSingleton<IGameEventListener, GameEventListener>();
        services.AddSingleton<IClientLanguageListener, ClientLanguageListener>();
        services.AddSingleton<ICommandListener, CommandListener>();
        
        _provider = services.BuildServiceProvider();
        _bridge = bridge;
    }

    private ChatTranslatorConfig LoadConfig(string sharpPath)
    {
        var configPath = Path.Combine(sharpPath, "configs", "chattranslatorhud", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<ChatTranslatorConfig>(json);
                if (config != null) return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load config from {ConfigPath}, using defaults", configPath);
            }
        }
        else
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new ChatTranslatorConfig(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
                _logger.LogInformation("Created default config at {ConfigPath}", configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create default config at {ConfigPath}", configPath);
            }
        }

        return new ChatTranslatorConfig();
    }

    private void OnModuleError(Exception e, string context) => 
        _logger.LogError(e, "An error occurred when {Context}", context);

    public bool Init()
    {
        _provider.CallInit<IModule>(e => OnModuleError(e, "initializing modules"));
        _logger.LogInformation("ChatTranslatorHud initialized");
        return true;
    }

    public void PostInit() => _provider.CallPostInit<IModule>(e => OnModuleError(e, "calling PostInit"));
    public void OnLibraryConnected(string name) => _provider.CallLibraryConnected<IModule>(name, e => OnModuleError(e, $"calling OnLibraryConnected for {name}"));
    public void OnAllModulesLoaded() => _provider.CallAllModulesLoaded<IModule>(e => OnModuleError(e, "calling OnAllModulesLoaded"));
    public void OnLibraryDisconnect(string name) => _provider.CallLibraryDisconnect<IModule>(name, e => OnModuleError(e, $"calling OnLibraryDisconnect for {name}"));
    public void Shutdown()
    {
        _provider.CallShutdown<IModule>(e => OnModuleError(e, "shutting down modules"));
        (_provider as IDisposable)?.Dispose();
    }

    public string DisplayName => "Chat Translator HUD";
    public string DisplayAuthor => "Tsukasa";
}
