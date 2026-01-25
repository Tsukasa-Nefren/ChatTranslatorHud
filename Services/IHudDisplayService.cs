using Ptr.Shared.Hosting;

namespace ChatTranslatorHud.Services;

public interface IHudDisplayService : IModule
{
    void AddMessage(string text, string? originalText = null, int durationSeconds = 3);
    
    void AddCountdown(string prefix, int seconds, string suffix = "", bool isMmss = false, string unit = "", string? originalText = null);
    
    void Start();
    void Stop();
}
