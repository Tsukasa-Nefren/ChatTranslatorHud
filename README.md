# ChatTranslatorHud

A ModSharp plugin for Counter-Strike 2 servers. Seamlessly translates console messages via the DeepL API and displays them on the HUD and chat, tailored to each player's native language.

## Features

- **Smart Translation:** Automatic console message translation using DeepL.
- **Context-Aware:** Enhances translation quality using round-based context and optional date-format hints (`yy.mm.dd`).
- **Player-Centric:** Auto-detects player language via their Steam client settings.
- **Optimized Performance:** Per-map translation caching. Failed API translations are smartly ignored and not cached.
- **Dynamic Countdowns:** Automatically detects countdowns and displays them on the HUD in the player's specific language.
- **Customizable Experience:** Players can individually toggle the HUD display and original message visibility on/off.
- **Localized UI:** Comprehensive multi-language support for all plugin menus.

## Requirements & Dependencies

**Core**
- .NET 10.0
- ModSharp 2.x
- Ptr.Shared

**Plugin Dependencies**
- MenuManager
- LocalizerManager
- ClientPreferences

## Installation

1. Download the latest release from the [Releases](https://github.com/Tsukasa-Nefren/ChatTranslatorHud/releases) page.
2. Extract the `.zip` file into your server's root directory (the `sharp` folder will merge automatically).
3. Start the server once to generate the configuration file.
4. Open the generated config file and add your DeepL API Key.

**Directory Structure:**
```text
sharp/
├── modules/
│   └── ChatTranslatorHud/
│       └── ChatTranslatorHud.dll
└── locales/
    ├── ChatTranslatorHud.json
    ├── ChatTranslatorHud.deps.json
    └── basemenu.json
```

## Configuration

**Path:** `sharp/configs/chattranslatorhud/config.json`

```json
{
  "DeepLApiKey": "YOUR_API_KEY_HERE",
  "EnableTranslation": true,
  "CacheTranslations": true,
  "DeepLApiUrl": "https://api-free.deepl.com/v2/translate",
  "UseRoundContext": true,
  "UseDateContext": true
}
```

| Option | Description | Default |
|--------|-------------|---------|
| `DeepLApiKey` | Your DeepL API key *(Required)* | `""` |
| `EnableTranslation` | Master switch to enable/disable translation | `true` |
| `CacheTranslations` | Enable per-map translation caching | `true` |
| `DeepLApiUrl` | DeepL API endpoint URL (Free or Pro) | `https://api-free.deepl.com/v2/translate` |
| `UseRoundContext` | Use previous messages as DeepL context per round | `true` |
| `UseDateContext` | Auto-add context hints for `yy.mm.dd` date patterns | `true` |

## Commands

| Command | Description |
|---------|-------------|
| `thud` | Opens the translation HUD settings menu for the player |

## Supported Languages

Supports all languages provided by the DeepL API, including but not limited to:
> **Asian:** Korean, Japanese, Chinese, Indonesian, Thai, Vietnamese  
> **European:** English, German, French, Spanish, Portuguese, Italian, Dutch, Polish, Russian, Turkish, Danish, Finnish, Swedish, Czech, Hungarian, Romanian, Bulgarian, Greek, Ukrainian

## License


This project is licensed under the MIT License.
