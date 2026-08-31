# Winotch

A Dynamic-Island-style widget for **Windows 10/11** — a small live pill that hangs from the top edge of your screen and expands on hover into a media player, clock, and settings panel. Inspired by macOS notch apps like boring.notch and [notchy.dev](https://notchy.dev).

Built with Tauri 2 (Rust + React).

## Features

- **Notch or Pill mode** — a carved notch flush with the screen top, or a floating capsule; switch any time in settings
- **Live media widget** — reads the Windows System Media Transport Controls (SMTC), so Spotify, Chrome/Edge YouTube, and other media apps automatically swap the pill into a mini player with title, artist, album art, progress, and play/pause + skip controls
- **Liquid glass mode** — translucent, blurred appearance (toggleable against solid dark)
- **Hover to expand** — the widget grows into a full panel with settings (mode, appearance)
- **Always on top, zero scrollbars** — fixed-shape window that hugs the absolute top edge of the display

## Download (Windows)

Grab the latest build from the Releases page:

**https://github.com/xXNeilplayzXx/Winotch/releases/tag/v0.2.0**

Direct download: [Winotch-v0.2.0-Windows.zip](https://github.com/xXNeilplayzXx/Winotch/releases/download/v0.2.0/Winotch-v0.2.0-Windows.zip)

> The repo is private, so you must be logged in as `xXNeilplayzXx` for the link to work.

## Install

1. Download and extract the zip — it contains `winotch.exe`, `WebView2Loader.dll`, and `libunwind.dll`. **Keep all three files in the same folder.**
2. Double-click `winotch.exe`.
3. If Windows SmartScreen shows *"Windows protected your PC"* (the build is unsigned), click **More info → Run anyway**. Full unblocking steps, antivirus false-positive handling, and code-signing options are in [docs/SMARTSCREEN.md](docs/SMARTSCREEN.md).
4. Hover the pill at the top-center of your screen. Use the ⚙ button in the expanded panel to switch modes and appearance.

**Requirements**

- Windows 10 (1809+) or Windows 11
- **WebView2 Runtime** — preinstalled on Windows 11 and nearly all Windows 10 machines. If the window never appears, install the free [Evergreen WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2) ("Evergreen Bootstrapper").
- No admin rights needed — it's a portable app; delete the folder to remove it.

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
