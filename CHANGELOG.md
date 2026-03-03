# Changelog

## [1.1.0]

### Added

- **Context-aware translation (DeepL)**
  - Previous console messages are sent as context to improve translation quality (reset on round restart).
  - Automatic "date format" context for sentences with dates (e.g. yy.mm.dd).
- **Config toggles**: `UseRoundContext`, `UseDateContext` (default: true).
- **LICENSE** file (MIT).

### Changed

- **Countdowns**
  - Countdown messages are translated per player language; HUD shows language-specific prefix/suffix and a summary when the countdown ends.
- **Chat output**
  - Successful translations shown as `[Translated] {text}`; falls back to original when translation fails, is uncached, or player language is unset.
- **Cache & round context**
  - Failed API translations are no longer written to the cache file or added to round context.
- **Localization**
  - Expanded multilingual menu strings and simplified UI descriptions.

## [1.0.0]

### Added

- Initial release: DeepL console message translation, chat & HUD display, countdown detection, per-player language and settings.
