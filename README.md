> [!IMPORTANT]
> **This repository is the archived legacy build.** Winotch is being rewritten in **C# / WPF** — active development and releases live at **[xXNeilplayzXx/Winotch](https://github.com/xXNeilplayzXx/Winotch)**. All releases here (v0.2.3 → v1.0.1) remain downloadable as-is.

# Winotch

A Dynamic Island for **Windows 10/11** — a little pill that hangs from the top edge of your screen and expands on hover into a media player, weather, clock, app launcher, and settings panel. Inspired by macOS notch apps like boring.notch and [notchy.dev](https://notchy.dev). Built with Tauri 2 — a Rust shell driving a hand-crafted HTML/CSS/JS page in WebView2.

<p align="center">
  <a href="https://github.com/xXNeilplayzXx/Winotch/releases/download/v1.0.1/winotch.exe">
    <img alt="Download winotch.exe for Windows" src="https://img.shields.io/badge/Download%20winotch.exe%20for%20Windows-0078d7?style=for-the-badge" />
  </a>
</p>

> The repo is private — you must be logged into GitHub as `xXNeilplayzXx` for any link to work. Latest version: **v1.0.1** ([releases page](https://github.com/xXNeilplayzXx/Winotch/releases)).

---

## 🪟 For non-technical people (3 steps)

1. **Click the blue button above** (or [this link](https://github.com/xXNeilplayzXx/Winotch/releases/download/v1.0.1/winotch.exe)) and save `winotch.exe` — it's one single file, no installers, no extra downloads.
2. **Double-click `winotch.exe`.** If Windows pops up *"Windows protected your PC"* (SmartScreen — we haven't bought a code-signing certificate yet, so Windows doesn't trust the file), click **More info**, then **Run anyway**. If Windows Defender blocks it again or quarantines the file, add an exclusion (below). See [docs/SMARTSCREEN.md](docs/SMARTSCREEN.md) for the full walkthrough.
3. **Answer the 2-page setup**, click **Get started**, and the notch appears at the top-center of your screen. Move your mouse over it to expand it.

Done. To remove Winotch later, just delete the `.exe`.

**If Windows Defender blocks the file:** Settings → Privacy & Security → **Windows Security** → **Virus & threat protection** → *Virus & threat protection settings* → **Manage settings** → *Exclusions* → **Add or remove exclusions** → **Add an exclusion** → **File** → pick `winotch.exe`. (Only do that for files you trust — this one is open source, and you can build it yourself from this repo.)

---

## Features

- **Two-page setup wizard (no scrolling)** — page 1 picks the look: theme (Light/Dark/System Default), type (Notch vs Pill), feel (Glass vs Solid). Page 2 picks where it lives and arranges your widgets before the island ever launches
- **Notch or Pill mode** — a carved notch flush with the screen top, or a floating capsule; switch any time in settings
- **Sized to your screen (or not)** — width/height sliders in settings set an exact notch size (150–800 × 30–200 px), staged in a draft and applied with Apply (or discarded with Reset)
- **Theme system** — Light and Dark force every surface (notch, settings, menus, wizard) instantly; **System Default** follows your Windows app theme **live** — flip Windows between light/dark with no restart and the island re-skins itself; a **Color** theme unlocks preset accent swatches, a custom color picker, and a Follow-Windows accent option
- **Windows accent integration** — the notch's highlight follows your Windows accent color automatically, including the small underline beneath active taskbar icons; changes apply live
- **Choose your display** — main monitor, any specific display (shown by its real name), **All monitors** (one island per screen), or pick one on a visual map of your screen layout
- **Widgets** — stack, reorder (with animations), and remove: media control, clock, notes, weather (Open-Meteo, no API key), and an app launcher. Add with ＋, edit notes/weather city/launcher apps with the ⚙ on each card. Weather can also use your **Windows location** directly
- **Live media widget** — reads the Windows SMTC feed, so Spotify, Chrome/Edge YouTube and other media apps automatically swap the collapsed pill into a mini player with title, artist, album art, progress, and play/pause + skip
- **Live call indicator** — while you're in a Zoom, Teams, Discord, Slack, Skype, WhatsApp, Phone Link or other call, the notch swaps to a green pill showing the app and a ticking call timer; it disappears when the call ends
- **Status dot** — the little dot at the notch's left edge is always live: grey when idle, **red** while the microphone is in use, **green** while you're on a call, and **blue** on a call with your mic active (hover it for the current state)
- **Liquid glass mode** — translucent, blurred appearance (or solid dark) with two sliders that only appear while Glass is selected: **opacity** (10–100%) and **blur** (0–40 px)
- **Background gradients** — Sunset, Ocean, Aurora, Candy, or Graphite skins that tint the glass/solid notch
- **Drag to move** — grab the collapsed notch and drag it anywhere (toggle in settings); it remembers where you left it until the next settings change
- **Size presets** — one-click Compact / Standard / Wide sizes on top of the width & height sliders, in settings and setup
- **Idle content** — choose what the collapsed strip shows when nothing is playing: brand + clock, clock only, custom text, or just the dot
- **Right-click the notch** (while expanded) — **Hide Winotch**, **Restart Winotch**, or **Close Winotch**; a system-tray icon (Show / Hide / Quit) brings it back anytime
- **Hover to expand** — grows into a full panel with settings (theme, mode, appearance, display)
- **Always on top, zero scrollbars** — fixed-shape windows that hug the absolute top edge of their display

---

## Download (Windows)

Grab the latest build from the Releases page:

**https://github.com/xXNeilplayzXx/Winotch/releases**

Direct download: **https://github.com/xXNeilplayzXx/Winotch/releases/download/v1.0.1/winotch.exe**

## Install

1. Download `winotch.exe` (a single file — no zip, no DLLs).
2. Double-click `winotch.exe`.
3. If Windows SmartScreen shows *"Windows protected your PC"* (the build is unsigned), click **More info → Run anyway**. Full unblocking steps, antivirus false-positive handling, and code-signing options are in [docs/SMARTSCREEN.md](docs/SMARTSCREEN.md).
4. On first launch the two-page setup wizard opens (no scrolling): **page 1** — theme (Light/Dark/System Default), type (Notch vs Pill), feel (Glass vs Solid); **page 2** — which display(s) it lives on when you have more than one screen (main, a specific display, all displays, or pick one on the map) and your **widgets**, arranged before the island launches. Then hover the pill at the top-center of the chosen screen. Use the ⚙ button in the expanded panel to change theme, mode, appearance, or display at any time; use ＋ on the Widgets bar to add widgets and the arrows/✕ on each card to rearrange or remove them.

**Requirements**

- Windows 10 (1809+) or Windows 11
- Nothing else. If the WebView2 Runtime isn't already installed (it ships with Windows 11 and most Windows 10 machines), Winotch downloads and installs it silently on first run.
- No admin rights needed — it's a portable app; delete the exe to remove it.

---

## Guide

Everything is reachable from the notch itself — no settings app, no launcher.

### Daily use

- **Hover** the collapsed bar to expand it into the full panel; move the mouse away and it shrinks back.
- **While music plays**, the collapsed bar becomes a mini player (title, artist, progress, play/pause, next).
- **While you're on a call**, it becomes a green call pill with the app name and a live timer.
- **The left dot** mirrors your privacy indicators: red mic, green call, blue call + mic.
- **Right-click while expanded** for Hide / Restart / Close. Hidden? Left-click the tray icon (W) to bring it back.
- **Drag** the collapsed bar to move the notch anywhere (toggle *Drag to move* in settings to switch this off).
- **Notifications/status**: set a custom idle line in settings — e.g. `Lunch at 1` — and it replaces the brand text.

### Settings (⚙ gear in the expanded panel)

- **Mode** — Notch (carved into the top edge) or Pill (floating capsule).
- **Display** — main monitor, any specific display by name, or **All monitors** (one island per screen).
- **Size** — *Size preset* chips (Compact/Standard/Wide) or the Width/Height sliders (150–800 × 30–200). Everything is staged as a draft: **Apply** commits it, **Reset** discards it, **Reset to default** restores factory settings instantly.
- **Appearance** — Solid or Glass; when Glass is on, extra sliders control **opacity** and **blur**.
- **Gradient** — a background skin (None/Sunset/Ocean/Aurora/Candy/Graphite).
- **Drag to move** — on/off. **When idle** — what the collapsed strip shows.
- **Theme** — Light / Dark / System Default (follows Windows live) or **Color** for preset accent swatches, a custom color picker, or Follow-Windows accent.

### Widgets

- **＋** adds a widget; **↑/↓** reorder; **✕** removes; **⚙** edits notes, weather, or launcher apps (media and clock are automatic).
- **Notes** autosaves as you type. **Weather** can use a typed city or your Windows location. **App launcher** runs any exe/path from the notch.

## Troubleshooting

- **"Windows protected your PC" / Defender deletes the exe** — unsigned builds trigger SmartScreen; see the unblocking + exclusion steps above and [docs/SMARTSCREEN.md](docs/SMARTSCREEN.md).
- **The call pill never appears** — it only shows for known desktop call apps (Zoom, Teams, Discord, Slack, Skype, WhatsApp, Phone Link…); browser-based calls (Meet/Zoom web in Chrome/Edge) aren't detected yet, and silent mic-only apps that aren't call apps won't trigger it.
- **The mic dot stays grey while I'm talking** — the watcher meters capture devices; if your mic is an exotic/external device or the Windows meter reads ~0, it may not trip the threshold (privacy Settings → Microphone also matters).
- **Weather says "Windows location is unavailable"** — allow location in Settings → Privacy → Location, then retry the **Use Windows location** button.
- **The window snaps back to the default spot after a drag** — the Tauri bridge wasn't active (the page silently falls back to browser mode when `window.__TAURI__` is missing); run the real app (`tauri dev` / the built exe), never the raw `index.html` in a plain browser.

## Building from source

Winotch is **Windows-only** — the release exe must be built on a Windows machine (Tauri cannot cross-build it from macOS). The frontend is a static HTML/CSS/JS page, so there is no Node build step:

```bash
cd webview
npm exec --yes --package @tauri-apps/cli@^2 -- tauri dev     # run with the page hot-reloaded from webview/ui
npm exec --yes --package @tauri-apps/cli@^2 -- tauri build  # produce the release exe
```

Or, on Windows, one command end-to-end: `scripts\release.bat v1.0.0` builds `winotch.exe` and attaches it to the matching GitHub release. CI does the same automatically whenever a `v*` tag is pushed (`.github/workflows/build-release.yml`).

The repo also contains an earlier **egui experiment** in `native/` — a self-contained desktop port that compiles independently; it is not part of the releases.

## Development

`tauri dev` from `webview/` opens the app window pointed at the static page in `webview/ui/`. Because the page runs inside the real Tauri shell, the notch window is transparent, frameless, and always-on-top — opening `webview/ui/index.html` in a plain browser works for layout iteration but the Tauri bridge (`window.__TAURI__`) is absent there, so window moves, system stats, media, and settings windows are stubbed out.

## Project layout

- `webview/` — the current app: `src-tauri/` (Rust shell: window management, always-on-top + flush-top placement, SMTC media watcher (`media.rs`), call-activity + mic watchers, Windows geolocation (`weather.rs`), display enumeration + multi-monitor docking (`lib.rs`), theme/accent watcher (`theme.rs`), app launcher (`launcher.rs`), autostart (`autostart.rs`), system tray (`tray.rs`)) and `ui/` (the island page — the whole UI, no framework)
- `native/` — the earlier egui/eframe port (kept for reference)
- `scripts/release.bat` — Windows one-shot release build + upload
- `.github/workflows/build-release.yml` — CI that builds `winotch.exe` on a Windows runner and attaches it to tagged releases
