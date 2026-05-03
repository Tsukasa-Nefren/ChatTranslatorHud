using Sharp.Shared.Objects;

namespace ChatTranslatorHud.Utils;

public static class DictionaryExtensions
{
    extension(Dictionary<string, List<IGameClient>> groups)
    {
        public void AddToLanguageGroup(string lang, IGameClient client)
        {
            if (!groups.TryGetValue(lang, out var list))
                groups[lang] = list = [];
            list.Add(client);
        }
    }
}
