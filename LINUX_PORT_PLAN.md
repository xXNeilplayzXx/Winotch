# Winotch Linux Port Implementation Plan

## Overview
Port Winotch (Dynamic Island for Windows) to Linux with support for both X11 and Wayland display servers. The port maintains feature parity with the Windows version while abstracting platform-specific functionality.

## Architecture

### UI Framework: Avalonia UI
**Justification:**
- Cross-platform WPF-like framework with minimal API differences
- Native SkiaSharp integration (reuse existing rendering engine)
- Built-in support for X11 and Wayland via windowing hints
- Layer-shell protocol support for Wayland overlays
- Large community and good documentation
- Allows keeping most XAML-like UI patterns

### Overlay Strategy
**X11:**
- Override-redirect window (bypass window manager)
- XShape extension for non-rectangular regions
- XFixes extension for input regions (click-through)
- XGetImage/XShmGetImage for screen capture

**Wayland:**
- wlr-layer-shell protocol for overlay panels
- input_region for click-through areas
- xdg-desktop-portal screencast (PipeWire) for screen capture
- Compositor-specific blur extensions (sway, wayfire, etc.)

### Abstraction Layer
Created `Platform/` directory with 12 interfaces:
1. `IMediaService` - Media playback (SMTC → MPRIS)
2. `IThemeService` - Theme detection (Registry → xdg-desktop-portal)
3. `IClipboardService` - Clipboard operations (WinForms → X11/Wayland)
4. `ISystemVolumeService` - Volume control (CoreAudio → PulseAudio/PipeWire)
5. `ISystemStatsService` - CPU/memory/network (Win32 → /proc)
6. `IBatteryService` - Battery status (PowerStatus → sysfs/UPower)
7. `IMonitorService` - Monitor enumeration (WinForms → XRandR/xdg-output)
8. `IStartupService` - Auto-start (Registry → XDG autostart)
9. `ILocationService` - Geolocation (WinRT → GeoClue2)
10. `ILauncherService` - App scanning (Start Menu → .desktop files)
11. `IScreenCapture` - Screen capture (GDI → XGetImage/PipeWire)
12. `IPlatformWindow` - Overlay window (WPF → Avalonia + platform-specific)

## Migration Phases

### Phase 1: Extract Platform Abstractions ✅ COMPLETED
- Created `Platform/` directory
- Defined 12 service interfaces
- Separated concerns: Windows-specific code stays in root, new interfaces in Platform/

### Phase 2: Create Linux Service Implementations
**Priority services (required for basic functionality):**
1. `LinuxClipboardService` - Use `wl-paste`/`wl-copy` for Wayland, `xclip`/`xsel` for X11
2. `LinuxMonitorService` - Parse `xrandr --query` output, or use Gdk.Monitor
3. `LinuxSystemStatsService` - Read `/proc/stat`, `/proc/meminfo`, `/proc/net/dev`
4. `LinuxBatteryService` - Read `/sys/class/power_supply/BAT*/capacity` and `status`
5. `LinuxStartupService` - Create/delete `.desktop` file in `~/.config/autostart/`
6. `LinuxLauncherService` - Parse `.desktop` files from `/usr/share/applications/` and `~/.local/share/applications/`

**Advanced services (for full feature parity):**
7. `LinuxMediaService` - Use Tmds.DBus to connect to MPRIS (`org.mpris.MediaPlayer2`)
8. `LinuxThemeService` - Use xdg-desktop-portal D-Bus (`org.freedesktop.portal.Settings`)
9. `LinuxSystemVolumeService` - Shell out to `pactl` or use managed PulseAudio bindings
10. `LinuxLocationService` - Use GeoClue2 D-Bus (`org.freedesktop.GeoClue2`)

**Files to create:**
- `Platform/Linux/LinuxClipboardService.cs`
- `Platform/Linux/LinuxMonitorService.cs`
- `Platform/Linux/LinuxSystemStatsService.cs`
- `Platform/Linux/LinuxBatteryService.cs`
- `Platform/Linux/LinuxStartupService.cs`
- `Platform/Linux/LinuxLauncherService.cs`
- `Platform/Linux/LinuxMediaService.cs`
- `Platform/Linux/LinuxThemeService.cs`
- `Platform/Linux/LinuxSystemVolumeService.cs`
- `Platform/Linux/LinuxLocationService.cs`

### Phase 3: Replace WPF with Avalonia
**Files to replace:**
1. `App.xaml` → `App.axaml` (Avalonia XAML)
2. `App.xaml.cs` → `App.axaml.cs` (inherit `Application`)
3. `OverlayWindow.cs` → Refactor to use `IPlatformWindow` abstraction
4. `SettingsWindow.xaml` → `SettingsWindow.axaml`
5. `SettingsWindow.xaml.cs` → Update to Avalonia controls
6. `SetupWindow.cs` → Convert to Avalonia
7. `UpdateWindow.cs` → Convert to Avalonia
8. `WidgetEditWindows.cs` → Convert all 6 dialog classes to Avalonia

**Key changes:**
- Replace `System.Windows.*` with `Avalonia.*`
- Replace `System.Windows.Media.*` with `Avalonia.Media.*`
- Replace `CompositionTarget.Rendering` with `Avalonia.Rendering` or custom frame loop
- Replace `HwndSource` with Avalonia's platform-specific window access
- Replace `SKElement` (WPF) with `Avalonia.Controls.SkiaRenderControl` or custom `SKCanvas`

**Project file changes:**
```xml
<TargetFramework>net8.0</TargetFramework>
<Remove>UseWPF</Remove>
<Remove>UseWindowsForms</Remove>
<Add>Avalonia</Add>
<Add>Avalonia.Desktop</Add>
<Add>Avalonia.Skia</Add>
<Add>Tmds.DBus</Add> (for MPRIS, xdg-desktop-portal)
```

### Phase 4: Implement Linux Overlay with Click-Through
**X11 implementation:**
- Create `Platform/X11/X11PlatformWindow.cs`
- Use Avalonia's X11 platform layer
- Access underlying Xlib window via `Window.PlatformImpl`
- Call `XShapeCombineRegion` for non-rectangular windows
- Call `XFixesCreateRegion` + `XFixesSetWindowShapeRegion` for input regions
- Update input region dynamically based on `IslandShell.IsInteractiveAt()`

**Wayland implementation:**
- Create `Platform/Wayland/WaylandPlatformWindow.cs`
- Use `wlr-layer-shell-unstable-v1` protocol
- Bind to `zwlr_layer_shell_v1` interface
- Create layer surface with `ZWLR_LAYER_SHELL_V1_LAYER_OVERLAY`
- Set `input_region` via `wl_surface.set_input_region`
- Handle `zwlr_layer_surface_v1.configure` events

**Detection:**
- Check `WAYLAND_DISPLAY` environment variable
- If set → Wayland mode
- If unset → X11 mode (or XWayland)

### Phase 5: Implement Linux Screen Capture
**X11:**
- Create `Platform/X11/X11ScreenCapture.cs`
- Use `XGetImage` or `XShmGetImage` (shared memory for performance)
- Convert XImage to SKBitmap
- Handle DPI scaling via XRandR

**Wayland:**
- Create `Platform/Wayland/WaylandScreenCapture.cs`
- Use `xdg-desktop-portal` D-Bus interface `org.freedesktop.portal.ScreenCast`
- Acquire PipeWire stream
- Use PipeWire C# bindings (e.g., `PipeWireSharp`) to capture frames
- Convert to SKBitmap

**Fallback:**
- If PipeWire unavailable, disable backdrop blur (use solid color)
- Or use `grim` (screenshots) for static capture on sway

### Phase 6: Test on X11 and Wayland
**Testing matrix:**
1. **X11 (Xorg):**
   - GNOME on Xorg
   - KDE Plasma on X11
   - XFCE
   - i3/sway (XWayland mode)

2. **Wayland:**
   - GNOME on Wayland (native)
   - KDE Plasma on Wayland
   - sway (native Wayland)
   - wayfire

**Test scenarios:**
- Overlay appears on correct monitor
- Click-through works (clicks pass through non-interactive areas)
- Interactive elements respond to clicks
- Backdrop blur captures screen content
- All 8 widgets render correctly
- Media playback detection works
- Theme switching works
- Clipboard history captures text/images/files
- System stats (CPU, memory, network) update
- Battery status updates
- Settings window opens and saves
- Tray icon appears with context menu

## Key Technical Decisions

### 1. Avalonia vs GTK4
**Chose Avalonia because:**
- Minimal API surface change from WPF (same XAML patterns, same binding model)
- Can reuse most XAML UI code with minor syntax changes
- Built-in SkiaSharp integration
- Better layer-shell support than GTK for Wayland overlays
- Smaller dependency tree (no native GTK libraries)

### 2. Layer-Shell vs Regular Window
**Must use layer-shell on Wayland because:**
- Regular windows cannot be truly "on top" in Wayland
- Layer-shell is the only way to create overlay panels
- Input regions work natively with layer-shell
- Compositors guarantee layer-shell surfaces render above regular windows

### 3. PipeWire for Screen Capture
**Why PipeWire:**
- Only portable way to capture screen on Wayland
- Works across GNOME, KDE, sway, and other compositors
- xdg-desktop-portal is the standard freedesktop API
- Future-proof (X11 also moving to PipeWire for screen sharing)

### 4. MPRIS for Media
**Why MPRIS:**
- Standard D-Bus interface for media players on Linux
- Supported by Spotify, VLC, Chrome, Firefox, etc.
- `Tmds.DBus` library provides good C# bindings
- Can query metadata, control playback, track position

### 5. Fallback Strategy
**When features unavailable:**
- No backdrop blur → use solid color with transparency
- No MPRIS → disable media widget
- No PipeWire → disable blur or use static screenshot
- No xdg-desktop-portal → use gsettings directly for GNOME

## File Structure After Port

```
src/Winotch/
├── Platform/
│   ├── IMediaService.cs
│   ├── IThemeService.cs
│   ├── IClipboardService.cs
│   ├── ISystemVolumeService.cs
│   ├── ISystemStatsService.cs
│   ├── IBatteryService.cs
│   ├── IMonitorService.cs
│   ├── IStartupService.cs
│   ├── ILocationService.cs
│   ├── ILauncherService.cs
│   ├── IScreenCapture.cs
│   ├── IPlatformWindow.cs
│   ├── Linux/           ← Linux implementations
│   ├── Windows/         ← Windows implementations (moved from root)
│   └── Common/          ← Shared utilities
├── Engine/              ← Unchanged (SkiaSharp rendering)
├── Models.cs            ← Unchanged
├── App.axaml            ← Avalonia XAML (replaces App.xaml)
├── OverlayWindow.cs     ← Refactored to use IPlatformWindow
├── SettingsWindow.axaml ← Avalonia XAML
└── ... (other windows)
```

## Dependencies to Add

```xml
<PackageReference Include="Avalonia" Version="11.0.6" />
<PackageReference Include="Avalonia.Desktop" Version="11.0.6" />
<PackageReference Include="Avalonia.Skia" Version="11.0.6" />
<PackageReference Include="Tmds.DBus" Version="0.15.0" />
<PackageReference Include="SkiaSharp" Version="2.88.7" />
<PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="2.88.7" />
```

## Risks and Mitigations

### Risk 1: Layer-Shell Support
**Issue:** Not all Wayland compositors support wlr-layer-shell (GNOME doesn't by default)
**Mitigation:** Fall back to regular overlay window if layer-shell unavailable. Detect via `zwlr_layer_shell_v1` global.

### Risk 2: PipeWire Complexity
**Issue:** PipeWire C# bindings are less mature than Python/Rust
**Mitigation:** Shell out to `pw-dump` and `pw-cat` as fallback. Or disable blur on Wayland initially.

### Risk 3: DPI Scaling
**Issue:** X11 and Wayland handle DPI differently
**Mitigation:** Use Avalonia's built-in DPI handling. Test on HiDPI displays.

### Risk 4: Performance
**Issue:** Screen capture on Wayland may be slower than X11
**Mitigation:** Increase blur cache refresh interval (500ms → 1000ms). Reduce capture resolution.

## Success Criteria
- Application runs on X11 (Xorg) without errors
- Application runs on Wayland (GNOME/sway) without errors
- Overlay appears on all monitors
- Click-through works correctly
- All 8 widgets render and function
- Settings persist across sessions
- Auto-start works
- Media detection works (MPRIS)
- Theme detection works (light/dark)
- Backdrop blur works (or gracefully degrades)

## Timeline Estimate
- Phase 2 (Linux services): 2-3 days
- Phase 3 (Avalonia migration): 3-4 days
- Phase 4 (Overlay): 2-3 days
- Phase 5 (Screen capture): 2-3 days
- Phase 6 (Testing): 2-3 days
**Total: 11-16 days**

## Notes
- Keep Windows implementation intact for backwards compatibility
- Use conditional compilation (`#if WINDOWS` / `#if LINUX`) sparingly
- Prefer runtime detection over compile-time switches
- Test on real hardware (not just VMs) for Wayland
- Document Linux-specific quirks in README.md
