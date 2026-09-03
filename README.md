# Winotch

A dynamic-island style **notch** for Windows 10/11, written in **C# / WPF** with a custom **SkiaSharp** renderer. A slim pill hangs from the top-center of your monitor, shows the essentials, and springs open on hover into a panel with media controls, a focus timer, clipboard history, and status alerts.

> **Note:** this `main` branch is the new C# rewrite (v2.0.0+). The earlier WebView2/Tauri build is archived on the [`legacy`](https://github.com/xXNeilplayzXx/Winotch/tree/legacy) branch — its releases (v0.2.3 → v1.0.1) remain downloadable from the [Releases](https://github.com/xXNeilplayzXx/Winotch/releases) page.

## How it works

Unlike a classic XAML window that resizes to fit its content, Winotch draws one **full-screen transparent overlay window** per monitor and paints the island every frame with SkiaSharp:

- A **critically-damped spring** drives the island's width/height between collapsed-pill and expanded-panel targets, which is what makes the morph feel weightless.
- Everything is **clipped to the island's rounded silhouette**, so no content can ever bleed outside the capsule — no black boxes, no square corners during animation (the bugs that plagued the WebView2 build are structurally impossible here).
- Clicks **pass through** every pixel except the island itself (`WM_NCHITTEST` → `HTTRANSPARENT`), so the rest of the screen stays fully interactive.
- The window is `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`: it never steals focus and never shows in the taskbar or Alt-Tab.

The engine (`src/Winotch/Engine`) is deliberately small — a scene of widgets laid out by the shell each frame, fed by plain, unit-tested logic services (`FocusTimer`, `PriorityStatus`, `ClipboardHistory`, `SettingsService`, …).

## Features (current)

- **Springy notch**: collapses to a pill (clock + battery + playing/active-focus chips), springs open on hover, hangs flush against the top edge in Notch style or floats in Island style (toggle in Settings).
- **Widget deck**: the expanded panel is a stack of widget cards — clock, system stats, now playing, notes, weather, app launcher, focus timer, clipboard. Use **+** in the panel header to add widgets and the **↑ ↓** / **⚙** / **✕** buttons on each card to reorder, configure, or remove them. Order and settings persist.
- **Media widget**: now playing title/artist with transport controls and a timeline via Windows SMTC; media-change toasts.
- **System stats widget**: live CPU, memory, battery and network rates (per-metric toggles via the ⚙ on the card).
- **Notes widget**: free-form scratchpad persisted in settings; tap the card to edit.
- **Weather widget**: current conditions, feels-like, today's high/low and city from Open-Meteo (no API key); set the city via the ⚙ on the card.
- **App launcher widget**: a grid of app shortcuts (chrome, edge, terminal, VS Code, notepad, calculator, paint, spotify, steam presets or any path); click to launch, ⚙ to manage.
- **Focus timer widget**: 25/5 and 50/10 presets, pause/resume/skip/stop, live countdown chip in the pill, completion toasts.
- **Clipboard widget**: in-memory last-10 text/files/images with thumbnails; copy-again, delete, clear. Never written to disk.
- **Status alerts**: charger connected (with an animated battery-fill flourish), charger disconnected, low battery (≤20%), Wi-Fi loss/reconnect.
- **Settings window** (gear icon or tray): 24-hour clock, show date, notch/island style, toast + feature toggles, start with Windows.
- **Tray menu**: settings, pause/resume the notch, start with Windows, exit.

## Building

Requires the **.NET 8 SDK** on Windows (10 2004+ / 11).

```powershell
dotnet restore Winotch.slnx
dotnet build Winotch.slnx -c Release
dotnet test  Winotch.slnx -c Release
```

Run the app: `dotnet run --project src/Winotch` (or launch the built exe). Windows may show a SmartScreen prompt for an unsigned dev build — that's expected.

## Releasing

Tag a commit `vX.Y.Z` and push it; the [build-test-release](.github/workflows/build-release.yml) workflow builds, runs the tests, publishes a self-contained portable `Winotch.exe`, and attaches it (plus a zip) to the release.

## Notes

- Fonts come from the OS (Segoe UI Variable / Fluent icons / Segoe UI Emoji) — nothing is bundled.
- Clipboard images are stored only as small PNG thumbnails in memory.
- Wi-Fi alerts read the SSID via `netsh`; mic/camera-use alerts are not implemented yet.
- UI rendering is **not** accessible to screen readers — the shell is a fully custom surface by design. The settings window is standard XAML.
- Architecture inspiration: [DynamicWin](https://github.com/FlorianButz/DynamicWin) (SkiaSharp island, spring motion) and [yappologistic/winotch](https://github.com/yappologistic/winotch) (feature set). Both predate this codebase; this project is an independent implementation under The Unlicense.

## License

[The Unlicense](LICENSE) — public domain. Use it however you like.
