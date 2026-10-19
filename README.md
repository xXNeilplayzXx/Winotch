# Winotch

**A Dynamic Island–style notch for Windows 10/11.** A slim pill hangs at the top of your screen showing the essentials — then springs open on hover into a full panel of widgets: media controls, weather, a focus timer, clipboard history, app shortcuts and more.

<!-- HERO: screenshots/collapsed.png — clean capture of the pill on a desktop -->

<p align="center">
  <img src="screenshots/collapsed.png" alt="The Winotch pill" width="720">
</p>

Written in C# with a custom SkiaSharp renderer — lightweight, smooth, and every pixel of the screen stays clickable except the pill itself.

> **Note:** this `main` branch is the new C# rewrite (v2.0.0+). The earlier WebView2/Tauri build is archived on the [`legacy`](https://github.com/xXNeilplayzXx/Winotch/tree/legacy) branch — its releases (v0.2.3 → v1.0.1) remain downloadable from the [Releases](https://github.com/xXNeilplayzXx/Winotch/releases) page.

## What it looks like

<p align="center">
  <img src="screenshots/expanded.png" alt="The expanded notch with widgets" width="720">
</p>

<p align="center">
  <img src="screenshots/settings.png" alt="Winotch Settings" width="720">
</p>

## Highlights

- **The pill** — a springy, weightless notch that collapses to a tidy pill (clock, status light, live chips) and morphs open on hover. It also changes face: press a volume or media key and it becomes a slider or a transport chip, take a call and it turns green, copy something or plug in a charger and a chip slides in. Hang it flush against the top edge like a notch, or float it like an island.
- **Widgets, your way** — clock, now playing, weather, focus timer, notes, clipboard history, system stats and an app launcher. Add, remove, resize and reorder them; everything persists.
- **Per-monitor profiles** — independently customize the notch, theme and widget settings for each display.
- **Calls at a glance** — when you're on a call the pill turns into a green chip with the app name and a live timer — desktop apps (Discord, Teams, Zoom, Slack, Webex, Skype, WhatsApp, Signal and more) and calls in the browser (Google Meet, web Teams/Slack/Discord, WhatsApp Web, Jitsi, Whereby…).
- **Volume & brightness, not Windows' OSD** — press a volume key and the pill morphs into a slider showing the level; brightness does the same. Winotch claims the volume keys, so Windows' own volume pop-up never appears.
- **Theme it completely** — Dark / Light / System / Colour / Background themes, glass or acrylic surfaces, ten background gradients, your pick of any font installed on your PC (or import your own), and a size that fits your screen.
- **Small touches** — status alerts for low battery, Wi-Fi drops and charger events; a clipboard that keeps your last copies handy; a focus timer that nudges you through work/break cycles.

## Getting started

1. Grab the latest **Winotch-vX.Y.Z-x64.zip** from the [Releases](https://github.com/xXNeilplayzXx/Winotch/releases) page.
2. Unzip it anywhere — it's a portable exe, nothing to install.
3. Run **`Winotch.exe`**. A first-run wizard sets up your theme and widgets; Windows may show a SmartScreen prompt because the build isn't code-signed — click **More info → Run anyway**.

Works on **Windows 10 (2004+)** and **Windows 11**.

## Features

### The notch

- **Springy pill** — collapses to a pill and springs open on hover; a critically-damped spring drives the morph so it feels weightless.
- **Idle content** — default (status light + clock), brand + clock, custom text (long text auto-scrolls), or just the light.
- **Pill chips** — show or hide calls, now playing, focus timer and battery chips right on the collapsed pill, plus an optional date chip.
- **Volume & brightness HUD** — press a volume key and the pill morphs into a slider: glyph, bar and percentage (**Muted** when it is). Winotch claims the volume keys themselves, so Windows' own OSD never appears and the level is applied straight to the audio endpoint — where possible the Now playing slider follows immediately, rather than on the next poll. Brightness shows the same HUD whenever it changes, read from either the monitor (DDC/CI) or a built-in panel (the Windows backlight), whichever your PC answers on. Both are toggles in Settings → Pill, and a hidden or paused Winotch hands the keys straight back to Windows. **Media key feedback** (also Settings → Pill) does the same for play / pause / next / previous — except those keys are only watched, never taken over.
- **One pill, many faces** — the collapsed pill has a few ways to take over from its usual row: drag mode, the volume / brightness slider, the call chip, the now-playing chip, and short-lived chips for a media key you just pressed or something worth a glance (charger in, rain coming, something copied). A chip yields to whatever you did last, so the pill always answers the most recent action; drag mode sits on top of all of it.
- **Media key feedback** — press play / pause / next / previous and the pill shows the key you pressed with the track it applies to. Winotch never claims these keys: your player and Windows keep handling them exactly as before, and the chip is purely a readout.
- **Glance chips** — rain starting within the half hour, the charger going in or out, and a fresh copy show as a chip on the pill instead of a toast. The rest (low battery, Wi-Fi, camera and mic) keep their toasts, because they are worth a sentence.
- **Drag it anywhere** — hit the **Drag mode** button in Settings and the pill switches to a draggable handle with **Done / Reset** buttons: drag it anywhere on screen, **Done** keeps the spot, **Reset** snaps back to top-center. The pill can never be pushed off screen.
- **Display targeting** — show it on your main monitor, one specific display (pick by name or on a visual map of your layout), **all monitors**, or **follow the active window** to whichever screen you're using.
- **Side mode** — attach the pill to the **left or right edge** (Settings → Pill → Edge attachment): it rotates 90° into a slim vertical capsule that hugs the side of your screen and slides along it when dragged. Hover it and the panel slides out beside the capsule — opening downward when there's room, upward when the capsule sits low. **Vertical text** (Settings → Pill) renders the pill's letters upright, stacked top-to-bottom, instead of rotated 90°. **Open direction** (Settings → Pill) picks how the panel expands: **Vertical** keeps the single column growing downward, while **Horizontal** blooms wide to a two-column layout with the widgets split across balanced columns — the launch grid adapts from 4 to 3 tiles per row, and drag-to-reorder works across both columns.
- **Click-through everywhere else** — the rest of the screen stays fully interactive; the notch never steals focus and never shows in the taskbar or Alt-Tab.

### Widgets

Open the expanded panel and manage cards with **+** (add), the arrows or drag (reorder), **⚙** (configure) and **✕** (remove).

- **Now playing** — title, artist, album art, transport controls and a timeline via Windows SMTC. Toasts announce track changes ("now-playing toasts"). While music plays the pill becomes a compact now-playing chip — rounded album art, the track and the artist, and a hairline progress bar that advances with the track. Pause and the chip dims and shows a play mark for twenty seconds before the pill goes back to the clock, so a long-stopped track doesn't sit there forever, and a title too long for the chip scrolls instead of being cut off. Drag the timeline to seek (with a live time readout — shown when the media app allows seeking), and use the volume slider and mute button for system volume.
- **Rain alerts** — a chip on the pill when precipitation is about to start at your weather location (within 30 minutes), once per shower. Toggle in Settings → Alerts.
- **Weather** — current conditions, feels-like, today's high/low and city from Open-Meteo (no API key needed). Set a city or let Windows decide.
- **Focus timer** — work/break presets (15/5, 25/5, 50/10, 90/20) or your own custom minutes, with an optional auto-cycle that keeps alternating; a live countdown shows on the pill.
- **Clipboard history** — your last 10 copies (text, files, images with thumbnails), kept only in memory — never written to disk. Copy again, or **Clear**.
- **App launcher** — a grid of shortcuts with each app's real icon; its ⚙ settings scan your installed apps so you can add them in one tap, alongside suggested entries (Chrome, Edge, VS Code, Spotify…).
- **System stats** — live CPU, memory, battery and network rates.
- **Notes** — a free-form scratchpad that persists.
- **Clock** — time, date and your city.

Each card sizes itself independently — hit **⚙** on a card and pick **Compact / Standard / Large** for that widget only. The whole card content — text, icons and rows — scales with the size, so a Compact card is genuinely smaller and never clips, while a Large card fills its taller box.

### Calls & status light

- **Call chip** — while you're actually on a call the pill becomes a green chip naming the app with a ticking timer. Desktop apps are matched by process name and browser calls (Google Meet, web Teams/Discord/Slack, WhatsApp Web, Jitsi, Whereby) by their tab title. A running app only lights the pill when there's real evidence of a call — a call-style window title, or a hot mic/camera for a known desktop app.
- **Status light** — the little dot beside the clock tells you what's active: **red** while your mic is in use, **amber** while your camera is on, **purple** when both are. Hover it for details.

### Appearance

- **Theme** — Dark, Light, System (follows your Windows theme live), **Colour** (tints the panels with your accent), or **Background** (follows your wallpaper's lightness).
- **Accent** — preset swatches, a custom hex value, or **Follow Windows accent** for the live Windows highlight colour.
- **Surface** — Glass (real backdrop blur), Acrylic (heavier blur + accent tint), or Solid.
- **Gradients** — Sunset, Ocean, Aurora, Candy, Graphite, Peach, Mint, Berry, Midnight or Forest.
- **Fonts** — every font family installed on your PC, or **Import font…** to add your own `.ttf`/`.otf` (stored in `%LOCALAPPDATA%\Winotch\Fonts`). Applies to the notch and the whole UI.
- **Size** — Compact / Standard / Wide presets plus fine width/height sliders (draft-and-apply so you can compare before committing). The standard default is 220 × 45 px.
- **Glance** — the high-gloss glass skin for the notch and its panel, at the same size as Classic.
- **Glass settings** — opacity and blur strength, with a glass on/off switch.

### Alerts & extras

- **Status alerts** — low battery, Wi-Fi loss/reconnect, mic/camera in use, and charger connected/disconnected (those two show as a chip on the pill, with a little battery-fill animation).
- **Right-click the notch** — hide, restart or close Winotch.
- **Settings window** — everything above plus 24-hour clock, start-with-Windows ("launch when you sign in"), rerun setup, **Reset to defaults** and a **Factory reset** that wipes your settings. Sections fold away with a click so the window is a short list of headings rather than one long scroll, and the ones you leave open are remembered between visits.
- **Tray menu** — settings, pause/resume the notch, hide/show, start with Windows, restart, exit.
- **Setup wizard** — a friendly two-page first run (theme → display & widgets) with a live preview; when the App launcher is enabled it scans the Start Menu and offers your installed apps as one-tap chips. Reopen it any time from Settings or the tray.
- **Auto-updates** — checks GitHub for newer builds at startup (toggle in Settings) and offers a one-click download + restart; the tray menu and Settings can check on demand. Needs the GitHub repo to be public.

## Building from source

Requires the **.NET 8 SDK** on Windows (10 2004+ / 11).

```powershell
dotnet restore Winotch.slnx
dotnet build Winotch.slnx -c Release
dotnet test  Winotch.slnx -c Release
```

Run it with `dotnet run --project src/Winotch`, or launch the built exe.

Tagging a commit `vX.Y.Z` and pushing it triggers the [build-test-release](.github/workflows/build-release.yml) workflow, which tests, publishes the self-contained `Winotch.exe` and attaches it to the release. The MSIX packaging project is separate and requires Windows with Visual Studio's MSIX Packaging Tools; the portable zip remains the automated release artifact.

## Good to know

- **Fonts** come from your system (Fluent icons, Segoe UI Emoji, Cascadia Mono for timers) — nothing is bundled. A missing font falls back to the OS default.
- **Glass** blurs the desktop behind the notch. Because the overlay is a full-screen transparent surface, the capture includes the notch's own previous frame, which settles into a soft glow — content on top stays crisp. If the backdrop can't be captured (locked session, etc.) it falls back to a translucent panel.
- **Clipboard** thumbnails live only in memory.
- **Brightness is mirrored, not taken over.** Windows defines no brightness key an app can claim, and on most laptops the brightness keys are handled by your PC maker's utility before Windows ever sees them — so your PC's own brightness indicator can still appear, a beat before Winotch's. Reading the level means asking the display, and Windows exposes brightness through two unrelated interfaces: DDC/CI down the display cable, which is how an external monitor answers, and the ACPI backlight behind WMI, which is how a laptop's own panel answers. Winotch reads both every poll and mirrors whichever one moved. If neither answers — some monitors ship with DDC/CI switched off in their own menu, and a virtual machine usually has no backlight at all — the brightness HUD stays away, and Settings → Pill says so on the brightness row itself.
- **Media keys are watched, not taken over.** Play / pause / next / previous are handled by Windows and whatever app owns the session, exactly as before; Winotch only mirrors the press onto the pill. So the chip names the key you pressed rather than claiming a playing state, and a chip that never appears costs you nothing but a chip.
- **Call detection** matches known desktop call apps and browser calls, and it's heuristic by nature: it looks for a call-style window title, or a hot mic/camera plus a running call app. So a window that keeps a call-like title while idle (a Zoom pre-join page, an open Jitsi tab) can light the pill early, and a web call whose tab title doesn't name the service can be missed.
- The notch is a fully custom surface, so it isn't exposed to screen readers; the Settings window is standard accessible XAML.

## Credits & license

Architecture inspiration: [DynamicWin](https://github.com/FlorianButz/DynamicWin) (SkiaSharp island, spring motion) and [yappologistic/winotch](https://github.com/yappologistic/winotch) (feature set). Both predate this codebase, which is an independent implementation.

Winotch itself is source-available under the [PolyForm Strict License 1.0.0](LICENSE). You're free to read it, build it, and use it for any noncommercial purpose — personal use, study, hobby projects, or at a charity, school, public research body, or government institution. You may not use it commercially, redistribute it, or publish modified or derived versions. It isn't OSI open source, so it isn't meant for package managers or the Microsoft Store.

## Release notes (v4.6.1)

- **Glance is a skin, not a layout.** Instead of floating a clock capsule and four glass cards around the pill, Glance now repaints the classic notch and its widget panel in the high-gloss glass material — a milled capsule rim with a pooled blue under-glow and framed slate cards with a top-lit 1px edge.
- The notch keeps its classic width, height, position, hover panel and drag behaviour, so nothing is added to or moved around the desktop. The floating deck from 4.6.0 is gone.
- The glass material is still available on the classic layout through the **Glossy glass** toggle; both settings stay off by default.
