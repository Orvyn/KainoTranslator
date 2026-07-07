# KainoTranslator

A lightweight Windows desktop app that OCRs a chosen rectangle of your screen and shows a
live-translated overlay on top of it — built for game subtitles, dialogue boxes, video
captions, or any on-screen foreign text.

- Pick a capture area with a hotkey, toggle live translation with a hotkey, open settings with a hotkey.
- Reads text with **Windows OCR** (recommended default, built into Windows, no setup), or the
  legacy **Tesseract** / **EasyOCR** engines if you prefer them.
- Translates with **DeepL**, **Google Translate**, **Yandex Translate**, or **Papago**.
- Strong support for English, Japanese, Chinese (Simplified & Traditional), Korean, and
  Russian, plus ~25 more languages (French, German, Spanish, Italian, Portuguese, Dutch,
  Polish, Turkish, Vietnamese, Thai, Indonesian, Arabic, Hindi, Ukrainian, and most other
  European languages — see `Models/LanguageCatalog.cs` for the full list).
- Optional proxy list to rotate requests through, mainly to keep Google's free endpoint from
  getting IP-rate-limited under heavy use.
- Click-through, borderless-friendly overlay designed to sit on top of games without stealing
  focus or input.

## Requirements

- Windows 10 (2018 Update / 1809) or Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build.
- Visual Studio 2022 (17.8+) is the easiest way to open/build/run it, but the .NET CLI works too.

## Building & running

```
cd KainoTranslator
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

Or open `ScreenTranslator.sln` in Visual Studio and press F5. The app has no main window — it
runs from the **system tray** (bottom-right of the taskbar). Right-click the tray icon for the
menu, or use the hotkeys below.

There's no window on launch by design; if you don't see the tray icon, check the taskbar's
"hidden icons" (^) arrow.

## Default hotkeys

| Action                              | Default combo   |
|--------------------------------------|-----------------|
| Select capture area                  | `Ctrl+Shift+A`  |
| Start / stop live translation        | `Ctrl+Shift+S`  |
| Translate an area once (one-off)     | `Ctrl+Shift+D`  |
| Open settings                        | `Ctrl+Shift+O`  |

All four are changeable in **Settings > Hotkeys** — click the box and press your desired
combination (must include at least one modifier key). If a hotkey fails to register (shown as
a tray balloon notification), another app is already using that combo — pick a different one.

The "translate an area once" hotkey lets you pick any area on screen and translate it a single
time (e.g. a bit of text that appeared somewhere else on screen) without touching your main
capture area or interrupting live translation if it's already running.

## First-time setup

1. Launch the app (tray icon appears).
2. Press `Ctrl+Shift+A` and drag a rectangle over the subtitle/dialogue area you want translated.
3. Press `Ctrl+Shift+O` to open Settings:
   - **General** — OCR engine (Windows OCR is on by default), source language, translator,
     target language.
   - **Translator API Keys** — add a key for whichever translator you picked (see below).
   - Save.
4. Press `Ctrl+Shift+S` to start live translation. The translated text appears in an overlay
   near the capture area.

## OCR engines

- **Windows OCR (recommended, default)** — no downloads needed, but the language you select
  must have its OCR component installed: **Settings > Time & Language > Language & region >
  Add a language**, then, for that language, enable **"Optical character recognition"** under
  its language options. Fast and generally the most accurate on clean UI/subtitle text.
- **Tesseract (legacy)** — fully offline. Download `.traineddata` files for the languages you
  need from https://github.com/tesseract-ocr/tessdata_fast and place them in the folder set in
  **Settings > Advanced > Tesseract data folder** (defaults to `.\tessdata` next to the exe).
- **EasyOCR (legacy)** — needs Python 3.9+ on PATH (or point at its exe in
  **Settings > Advanced**) with `pip install easyocr` run once. The app launches
  `Python\easyocr_worker.py`, which keeps EasyOCR's model loaded in memory and answers one
  recognition request per line of stdin — this avoids reloading the (slow) model on every
  frame. GPU acceleration is optional and requires a CUDA build of PyTorch.

## Translators & API keys

| Translator | What you need | Notes |
|---|---|---|
| **DeepL** | API key from deepl.com/pro-api (free tier exists) | Toggle "Use Pro endpoint" only if you have a paid Pro key — free keys must stay on the free endpoint. |
| **Google Translate** | Nothing required | Works immediately via Google's free web endpoint. Add a Cloud Translation API key for guaranteed uptime/quota instead of the free endpoint. |
| **Yandex Translate** | API key + Folder ID from Yandex Cloud | Both fields are required. |
| **Papago** | Client ID + Client Secret from the NAVER Cloud Platform console | Papago only supports a limited set of language pairs, mostly routed through Korean/English/Japanese/Chinese — pick a supported target language and it'll only show valid options. |

## Proxies

**Settings > Advanced > Proxy list** (at the bottom of the tab) accepts one proxy per line:
```
http://1.2.3.4:8080
http://user:pass@1.2.3.4:8080
1.2.3.4:8080
```
Leave it empty to connect directly (the default). This is most useful with Google's free
endpoint, which has no official rate-limit guarantee and can start rejecting requests under
sustained, fast polling — rotating through a few proxies (enabled by default when the list
isn't empty) spreads the load across different IPs.

## Using it with games

- **Run the game in Borderless or Windowed mode.** Most games let you switch with `Alt+Enter`
  or a display-mode setting. This is the single most important tip.
- **Exclusive fullscreen** mode can make both screen capture and the overlay behave oddly,
  because some exclusive-fullscreen games bypass the normal Windows compositor (DWM) — the
  captured image may come back black, or the overlay may not draw on top. Borderless/windowed
  modes render through DWM like any other window, so capture and overlay both work correctly.
  Recent Windows/DirectX versions increasingly avoid true exclusive fullscreen anyway, but if a
  specific game only offers "Fullscreen", try windowed/borderless if the overlay looks wrong.
- The overlay is **click-through by default** (toggle in **Settings > Overlay**), so it never
  blocks mouse clicks or keyboard input meant for the game underneath.
- The app is **per-monitor DPI aware**, so capture area and overlay placement stay accurate
  even on mixed-DPI multi-monitor setups or if you drag the game between monitors after picking
  the region (re-pick the region if you move to a monitor with a different resolution/scale).

## Overlay appearance

**Settings > Оформление** lets you set:
- Position (default: top-center of the screen), font size, and max width (default 1200px).
- Text color and background color (hex, e.g. `#FFFFFF`), with a live preview swatch, plus
  background opacity (default 80%).
- Either a 1px outline **or** a drop shadow behind the text for readability on any background
  (not both at once) - default is the outline.
- Optional auto-hide: hide the overlay automatically after N seconds if the text stops updating.

## Performance notes

- The polling interval (default 0.2s / 200 ms, **Settings > Перевод**, shown in seconds) controls
  how often the capture area is re-checked.
- "Skip OCR/translation when the capture area hasn't visibly changed" (on by default) hashes a
  small downsampled version of each captured frame and skips OCR + the translator API call
  entirely when nothing changed — this is what keeps a fast polling interval cheap on both CPU
  and translation-API usage, since most subtitle/dialogue text sits still for a second or more.
- Repeated identical recognized text also skips a redundant translation call.
- Perceived delay is mostly the OCR + translation network round-trip; Windows OCR is local and
  typically the fastest of the three engines, so it's the best choice when both speed and
  simplicity matter.

## Project layout

```
KainoTranslator/
  App.xaml(.cs)              - tray icon, hotkey wiring, top-level coordination
  TranslationEngine.cs        - capture -> OCR -> translate -> overlay loop
  Capture/                    - region selection UI + screen capture + frame hashing
  Ocr/                        - IOcrEngine + Windows OCR / Tesseract / EasyOCR implementations
  Translation/                - ITranslator + DeepL / Google / Yandex / Papago implementations, proxy manager
  Overlay/                    - the always-on-top translated-text overlay window
  Hotkeys/                    - global hotkey registration (RegisterHotKey)
  SettingsUi/                 - the Settings window
  Models/                     - AppSettings (persisted to %AppData%\KainoTranslator\settings.json), LanguageCatalog
  Python/easyocr_worker.py    - EasyOCR worker script (only needed if you use the EasyOCR engine)
```

## Known limitations / troubleshooting

- Windows OCR requires the language's OCR feature to be installed via Windows Settings — the
  app will tell you exactly that if it's missing, rather than failing silently.
- The free Google endpoint and Papago's limited language-pair support are noted above; the app
  surfaces clear error messages (via tray balloon) rather than failing silently when a
  translator rejects a request.
- If a hotkey doesn't fire, another application likely already registered that exact
  combination system-wide; pick a different combo in Settings.
