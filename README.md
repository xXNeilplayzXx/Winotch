# Winotch

A Dynamic-Island-style widget for **Windows 10/11** — a small live pill that hangs from the top edge of your screen and expands on hover into a media player, clock, and settings panel. Inspired by macOS notch apps like boring.notch and [notchy.dev](https://notchy.dev).

Built with Tauri 2 (Rust + React).

## Features

- **Notch or Pill mode** — a carved notch flush with the screen top, or a floating capsule; switch any time in settings
- **Choose your display** — pick the main monitor, any specific display (shown by its real name), or **All monitors** (one island per screen); configurable in the first-run wizard and in settings
- **Custom display picker** — choose “Custom setup” in the wizard to click any screen on a visual monitor map
- **Widgets** — stack, reorder, and remove widgets in the expanded panel (＋ to add, ✕/↑/↓ on each card): media control, clock, weather, and an app launcher. Media/weather/launcher each carry their own inline settings (media playback comes from the Windows SMTC feed; weather uses Open-Meteo with no API key; the launcher quick-launches apps by path or name)
- **Live media widget** — the media control reads the Windows SMTC feed, so Spotify, Chrome/Edge YouTube, and other media apps automatically swap the collapsed pill into a mini player with title, artist, album art, progress, and play/pause + skip controls
- **Liquid glass mode** — translucent, blurred appearance (toggleable against solid dark)
- **Theme system** — **Light / Dark / System Default** in the settings panel. Light and Dark force every surface (notch, settings, menus, wizard) to that theme instantly; System Default follows the Windows app theme **live** — flip Windows between light/dark (no restart) and the island re-skins itself. The choice persists across launches (`winotch.theme`)
- **Windows accent integration** — the island's highlight color follows your Windows accent color (`DWM\ColorizationColor`), updated live whenever you change accent; the accent underlines (media progress, live dot) are derived to match the underline Windows draws beneath active taskbar icons (accent blended into the taskbar surface per theme)
- **Hover to expand** — the widget grows into a full panel with settings (mode, appearance, display, theme)
- **Always on top, zero scrollbars** — fixed-shape windows that hug the absolute top edge of their display

## Download (Windows)

Grab the latest build from the Releases page:

**https://github.com/xXNeilplayzXx/Winotch/releases**

> The repo is private, so you must be logged in as `xXNeilplayzXx` for the link to work.

## Install

1. Download `winotch.exe` (a single file — no zip, no DLLs).
2. Double-click `winotch.exe`.
3. If Windows SmartScreen shows *"Windows protected your PC"* (the build is unsigned), click **More info → Run anyway**. Full unblocking steps, antivirus false-positive handling, and code-signing options are in [docs/SMARTSCREEN.md](docs/SMARTSCREEN.md).
4. On first launch a two-page setup wizard opens (no scrolling): **page 1** picks the island's look — theme (Light/Dark/System Default), type (Notch vs Pill), and feel (Glass vs Solid); **page 2** picks where it lives when you have more than one screen (main, a specific display, all displays, or pick one on the map) and lets you arrange your **widgets** before the island launches (add, remove, reorder, and set up weather/launcher right there). Then hover the pill at the top-center of the chosen screen. Use the ⚙ button in the expanded panel to change theme, mode, appearance, or display at any time; use ＋ on the Widgets bar to add widgets and the arrows/✕ on each card to rearrange or remove them.

**Requirements**

- Windows 10 (1809+) or Windows 11
- Nothing else. If the WebView2 Runtime isn't already installed (it ships with Windows 11 and most Windows 10 machines), Winotch downloads and installs it silently on first run.
- No admin rights needed — it's a portable app; delete the exe to remove it.

## Building from source

```bash
npm install
npm run tauri dev    # run with live reload
npm run tauri build  # produce a release bundle
```

The Windows exe in the releases is cross-compiled from macOS with the `x86_64-pc-windows-gnullvm` target (portable llvm-mingw toolchain).

## Development server notes

`tauri dev` starts Vite automatically on **http://127.0.0.1:1420** (HMR on 1421) and the Rust shell loads that URL — you normally don't run anything by hand. If the webview ever shows `ERR_CONNECTION_REFUSED` or a blank window:

1. A previous dev session probably died uncleanly and something still holds port 1420 (or the Vite process is simply not running). Free the ports and relaunch:

   ```bash
   npm run tauri:dev   # frees ports 1420/1421, then runs tauri dev
   ```

   (On Windows use `scripts\kill-dev-ports.bat` before `npm run tauri dev`.)

2. The dev server and the app must agree on the address: Vite binds `127.0.0.1:1420` (`vite.config.ts`, `strictPort`) and `tauri.conf.json` points `devUrl` at the same `http://127.0.0.1:1420`. Both are pinned to the explicit IPv4 loopback so a `localhost` → `::1` resolution mismatch on Windows can never produce a refused connection.

3. `strictPort: true` means Vite **fails loudly** if 1420 is taken instead of silently drifting to 1421 (which the webview wouldn't know about) — combined with the cleanup script this keeps the pair in sync.

## Project layout

- `src/` — React frontend (island UI, widgets, styles)
- `src-tauri/` — Rust shell: window management, always-on-top + flush-top placement, SMTC media watcher (`src/media.rs`)
