# ChatTranslatorHud

A ModSharp plugin for Counter-Strike 2 servers. Translates console messages via DeepL API and displays them on HUD and chat based on each player's language.

## Features

- Automatic console message translation (DeepL API)
- Auto-detect player language (based on Steam client language)
- Display translated messages on center HUD
- Translation caching (per-map storage)
- Automatic countdown message detection and display
- Per-player HUD toggle (on/off)
- Original message display toggle (on/off)
- Multi-language UI support

## Requirements

- .NET 10.0
- ModSharp 2.x
- Ptr.Shared

### Dependencies

- MenuManager
- LocalizerManager
- ClientPreferences

## Installation

1. Download the latest release from [Releases](../../releases)
2. Extract the zip file to your server's root folder (the `sharp` folder will merge automatically)
3. Start the server and edit the generated config file

The release archive structure:
```
sharp/
├── modules/
│   └── ChatTranslatorHud/
│       └── ChatTranslatorHud.dll
└── locales/
    └── ChatTranslatorHud.json
```

## Configuration

Config file location: `sharp/configs/chattranslatorhud/config.json`

```json
{
  "DeepLApiKey": "",
  "EnableTranslation": true,
  "CacheTranslations": true,
  "DeepLApiUrl": "https://api-free.deepl.com/v2/translate"
}
```

| Option | Description | Default |
|--------|-------------|---------|
| DeepLApiKey | DeepL API key (required) | - |
| EnableTranslation | Enable translation feature | true |
| CacheTranslations | Enable translation caching | true |
| DeepLApiUrl | DeepL API URL | api-free.deepl.com |

## Commands

| Command | Description |
|---------|-------------|
| `thud` | Open translation HUD settings menu |

## Supported Languages

Supports all languages available in DeepL:
- Korean, English, Japanese, Chinese
- German, French, Spanish, Portuguese
- Russian, Italian, Polish, Turkish
- Dutch, Danish, Finnish, Swedish
- Czech, Hungarian, Romanian, Bulgarian
- Greek, Ukrainian, Indonesian, Thai, Vietnamese

## License

MIT License
