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

Full feature parity with the original app (the archived `legacy` WebView2 build), implemented in the rewrite's own design.

- **Springy notch**: collapses to a pill (LED + clock + battery + live chips), springs open on hover, hangs flush against the top edge in Notch style or floats in Island style (toggle in Settings).
- **Setup wizard**: a two-page first-run wizard (theme / type / surface, then display + widgets) that opens on a fresh install; **Rerun setup…** from Settings or the tray.
- **Theme system**: Dark / Light / System (follows your Windows app theme **live**) / Color, plus accent control — preset swatches, a custom hex picker, or **Follow Windows accent** (DWM color, live). Every surface re-skins instantly: island, settings, dialogs.
- **Appearance**: Glass (real backdrop blur with an on/off switch, **opacity** 10–100% and **blur** 0–40px sliders) or Solid, and the Sunset / Ocean / Aurora / Candy / Graphite background gradients. Glass falls back to a translucent panel when the backdrop can't be captured.
- **Font**: pick the typeface for the island and app text (Aptos by default, plus Segoe UI Variable, Segoe UI and Bahnschrift) — applied live to the island, settings and dialogs.
- **Size**: Compact / Standard / Wide presets and width (150–800) / height (30–200) sliders; sliders stage a draft applied with **Apply** (or discarded with **Reset**).
- **Display targeting**: Main monitor, a specific display (picked by name or on a **visual map** of your screen layout), **All monitors** (one island per screen), or **Follow active** (the island hops to the monitor under your active window).
- **Idle content**: Default (LED + clock + battery), Brand + clock, Clock only, custom text, or just the dot.
- **Drag to move**: grab the collapsed pill and drag it anywhere (toggle in Settings); the spot is remembered.
- **Right-click the expanded notch**: Hide Winotch, Restart Winotch, or Close Winotch. The tray adds Show/Hide and Restart.
- **Live call indicator**: while a known call app runs (Zoom, Teams, Discord, Slack, Skype, WhatsApp, Phone Link, …) the pill turns into a green call chip with the app name and a ticking timer.
- **Status dot**: grey when idle, **red** while the microphone is in use, **green** during a call, **blue** on a call with the mic hot — hover it for the current state. Driven by the real Windows privacy consent store.
- **Widget deck**: the expanded panel is a stack of widget cards — clock, system stats, now playing, notes, weather, app launcher, focus timer, clipboard. Use **+** in the panel header to add widgets; **drag any card** to reorder (or use the **↑ ↓** buttons), **⚙** to configure, **✕** to remove. Order and settings persist.
- **Media widget**: now playing title/artist with **album art**, transport controls and a timeline via Windows SMTC; media-change toasts. While playing, the collapsed pill shows the track (with art).
- **System stats widget**: live CPU, memory, battery and network rates (per-metric toggles via the ⚙ on the card).
- **Notes widget**: free-form scratchpad persisted in settings; tap the card to edit.
- **Weather widget**: current conditions, feels-like, today's high/low and city from Open-Meteo (no API key); set a city or use **Windows location** via the ⚙ on the card.
- **App launcher widget**: a grid of app shortcuts (chrome, edge, terminal, VS Code, notepad, calculator, paint, spotify, steam presets or any path); click to launch, ⚙ to manage.
- **Focus timer widget**: 25/5 and 50/10 presets, pause/resume/skip/stop, live countdown chip in the pill, completion toasts.
- **Clipboard widget**: in-memory last-10 text/files/images with thumbnails; copy-again, delete, clear. Never written to disk.
- **Status alerts**: charger connected (with an animated battery-fill flourish), charger disconnected, low battery (≤20%), Wi-Fi loss/reconnect, microphone/camera in use.
- **Settings window** (gear icon or tray): 24-hour clock, show date, notch/island style, theme, appearance, size, idle content, drag to move, display targeting, toast + feature toggles, start with Windows, rerun setup.
- **Tray menu**: settings, rerun setup, pause/resume the notch, hide/show, start with Windows, restart, exit.

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

- Fonts come from the OS (the Appearance font choice, Fluent icons, Segoe UI Emoji, Cascadia Mono for timers) — nothing is bundled. Choosing a family that isn't installed falls back to the OS default.
- Clipboard images are stored only as small PNG thumbnails in memory.
- Wi-Fi alerts read the SSID via `netsh`; mic/camera alerts read the Windows privacy consent store.
- Glass backdrop blur captures the composited screen behind the island and blurs it with Skia; because the overlay window is a full-screen transparent surface, the capture includes the island's own previous frame, which converges to a soft glow (the classic glass artifact) — content on top stays crisp. When the capture fails (locked session etc.) glass falls back to a plain translucent panel.
- Call detection is a known-app process scan (like the old app) — browser-based calls aren't detected, and the pill shows while a call app is running.
- UI rendering is **not** accessible to screen readers — the shell is a fully custom surface by design. The settings window is standard XAML.
- Architecture inspiration: [DynamicWin](https://github.com/FlorianButz/DynamicWin) (SkiaSharp island, spring motion) and [yappologistic/winotch](https://github.com/yappologistic/winotch) (feature set). Both predate this codebase; this project is an independent implementation under The Unlicense.

## License

[The Unlicense](LICENSE) — public domain. Use it however you like.
