# Changelog

## [1.1.2]

### Updated

- **ModSharp dependencies pinned to 2.1.123** (LocalizerManager, MenuManager, ClientPreferences, Sharp.Shared) and **Ptr.Shared to 1.1.29**.
- **LocalizerManager API migration**
  - `ILocalizer` / `TryGetLocalizer` → `ILocale` / `For(client)` to match the latest LocalizerManager surface.

### Added

- **ISO 8601 date pre-substitution**
  - `yy.mm.dd` is rewritten to `20yy-mm-dd` before being sent to DeepL, eliminating European `dd.mm.yy` misinterpretation that the context hint alone could not reliably prevent.
- **Cache write coalescing**
  - Per-map translation cache writes are now debounced (1s) and serialized through a single-writer semaphore, eliminating redundant I/O under high translation load.
- **Per-language countdown rendering**
  - Countdown HUD now shows language-specific prefix/suffix per player.
- **Console message dedup**
  - Identical `ConsoleSay` messages within a 100ms window are coalesced to avoid duplicate translation calls and HUD entries.

### Fixed

- **Atomic cache file write**
  - Per-map translation cache is now written via `temp file → rename` to prevent JSON corruption on server crash or power loss. Stale `.tmp` leftovers from prior abnormal shutdowns are cleaned up on startup.
- **DeepL API resilience**
  - Added a circuit breaker (opens for 1 minute after 5 consecutive failures) to stop hammering the API during outages or quota exhaustion.
  - HTTP 429 / 503 responses now retry with exponential backoff (500ms → 1000ms → 2000ms) instead of failing immediately.
- **Map change race condition**
  - Translations completing after a map change no longer pollute the new map's cache; the result is still returned to the caller for HUD display.

## [1.1.1]

- **Fix missing .deps.json in release archive**

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

