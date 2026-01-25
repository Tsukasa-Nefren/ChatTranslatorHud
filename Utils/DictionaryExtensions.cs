using Sharp.Shared.Objects;

namespace ChatTranslatorHud.Utils;

public static class DictionaryExtensions
{
    public static void AddToLanguageGroup(this Dictionary<string, List<IGameClient>> groups, string lang, IGameClient client)
    {
        if (!groups.TryGetValue(lang, out var list))
            groups[lang] = list = [];
        list.Add(client);
    }
}
