using Microsoft.Extensions.Configuration;
using Sharp.Shared;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;

namespace ChatTranslatorHud;

internal class InterfaceBridge
{
    private readonly ISharedSystem _sharedSystem;

    public InterfaceBridge(ISharedSystem sharedSystem, string dllPath, string sharpPath, Version version, IConfiguration coreConfiguration, bool hotReload)
    {
        _sharedSystem = sharedSystem;
        DllPath = dllPath;
        SharpPath = sharpPath;
        Version = version;
        CoreConfiguration = coreConfiguration;
        IsHotReload = hotReload;
    }

    public IModSharp ModSharp => _sharedSystem.GetModSharp();
    public INetworkServer Server => ModSharp.GetIServer();
    public IClientManager ClientManager => _sharedSystem.GetClientManager();
    public IConVarManager ConVarManager => _sharedSystem.GetConVarManager();
    public IEventManager EventManager => _sharedSystem.GetEventManager();
    public IFileManager FileManager => _sharedSystem.GetFileManager();
    public IGlobalVars GlobalVars => ModSharp.GetGlobals();

    public string DllPath { get; init; }
    public string SharpPath { get; init; }
    public Version? Version { get; init; }
    public IConfiguration? CoreConfiguration { get; init; }
    public bool IsHotReload { get; init; }
}
