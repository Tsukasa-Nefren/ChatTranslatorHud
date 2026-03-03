using System.Text.Json.Serialization;

namespace ChatTranslatorHud;

public class ChatTranslatorConfig
{
    [JsonPropertyName("DeepLApiKey")]
    public string DeepLApiKey { get; set; } = "";

    [JsonPropertyName("EnableTranslation")]
    public bool EnableTranslation { get; set; } = true;

    [JsonPropertyName("CacheTranslations")]
    public bool CacheTranslations { get; set; } = true;

    [JsonPropertyName("DeepLApiUrl")]
    public string DeepLApiUrl { get; set; } = "https://api-free.deepl.com/v2/translate";

    [JsonPropertyName("UseRoundContext")]
    public bool UseRoundContext { get; set; } = true;

    [JsonPropertyName("UseDateContext")]
    public bool UseDateContext { get; set; } = true;
}
