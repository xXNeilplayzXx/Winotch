# HANDOFF — where we left off

_Last updated: 2026-09-09 by Buffy (Codebuff agent). Read this before touching anything._

## Current state (TL;DR)

- **Latest release:** v2.3.0 "Linux Support" — but **CI is red on `main` and on the v2.3.0 tag**: the Windows build fails with 2 compile errors (details below). The release *does* have assets (`Winotch-win-x64.tar.gz`, `Winotch-linux-x64.tar.gz`) — packaging changed from `.zip` to `.tar.gz` in the Linux port.
- **The 7 critical bug fixes are stranded on a branch.** `main` does **not** contain them — the Linux port branched from 2.1.10, before those fixes existed. Only the **v2.2.0** release (Windows zip) has them.
- **Version history is out of order:** main went 2.1.10 → 2.3.0, skipping 2.2.0 (which exists only on the qwen branch + its release).

## CI failures on main / v2.3.0 (blockers, fix first)

Run 34391459754 (v2.3.0) and 34297138372 (main) — Windows job, Build step:

| File | Error | Cause |
|---|---|---|
| `src/Winotch/PrivacyUseProbe.cs` | CS1529 — `using` clause must precede all other elements | `using Microsoft.Win32;` sits mid-file *after* the `IPrivacyConsentStore` interface, inside a `#if WINDOWS` block (merge mangled it) |
| `src/Winotch/WindowsLocation.cs` | CS1022 — type/namespace or end-of-file expected | Extra closing brace before the final `#endif` |

**Shortcut:** the `qwen-code-b353ad55-5699-4a9e-8a58-a369f8205817` branch has *corrected* versions of both files (plain Windows forms, no `#if` mangle). Note `WindowsLocation.cs` on that branch is the plain `Geolocator` version — merge it deliberately, don't copy blindly, since main's copy may be the cross-platform-intended one with the braces just botched.

## The stranded bug-fix branch (needs merging into main)

Branch `qwen-code-b353ad55-5699-4a9e-8a58-a369f8205817`, tip `0fda030`:

- `8454e3f` — the 7 fixes: `CheckForUpdates` → `CheckForUpdatesAsync` on `INotchHost`, DPI divide-by-zero guard, mutex try/finally, logging in every swallowed `catch` in `MediaService.cs`, structural (deep) equality in `SettingsService.cs`, safe async event handlers in `OverlayWindow.cs`, thread-safe CTS dispose via `Interlocked.Exchange`.
- `0fda030` — fixes the compile break the 7-fix commit introduced (tray still called old `CheckForUpdates`) and bumps `<Version>` to 2.2.0.

Tag `v2.2.0` points at `0fda030`. Its release has `Winotch-v2.2.0-x64.zip` (76 MB). These fixes are **in that release but not in main**.

⚠️ **When merging into main, expect conflicts:** main's Linux port stubbed D-Bus services and added `#if WINDOWS` conditionals; the branch is Windows-era code. Also the branch renamed `CheckForUpdatesAsync` — main's call sites still use `CheckForUpdates`. Resolve toward `CheckForUpdatesAsync` everywhere.

## Pending work, in priority order

1. **Fix the two compile errors on main** (see table above) → get CI green.
2. **Merge the qwen bug-fix branch into main**, resolving the `CheckForUpdatesAsync` rename and `#if WINDOWS` conflicts.
3. **Decide the next version.** Main skipped 2.2.0; sensible next cut is **2.3.1** (or re-plan: if you want the bug fixes labeled, some teams would re-cut 2.4.0 — user's call).
4. **Fullscreen "hide the notch" feature — approved but never built.** Agreed design: hide by default when a foreground window covers ≥98% of a monitor (and is topmost/not the shell), plus a **"Keep notch visible in fullscreen"** toggle in Settings → Pill. `HideNotch`/`ShowNotch`/`SetPaused` hooks already exist on the overlay; `MonitorTargeting.cs` is where the monitor probe belongs. Plan a `FullscreenDetector` pure helper + tests, per repo conventions.
5. **Housekeeping:** stray `.DS_Store` files are untracked in the repo root — delete and gitignore them. Consider making the release workflow create its own release if missing (the upload step has failed twice for this reason).

## Release mechanics (hard-won, don't relearn these)

- **The workflow's upload step requires the GitHub release to already exist.** Create the release (with notes) *before* the tag's CI run reaches the upload step, or it fails and you're re-running jobs. (v2.1.8 needed a rerun dance; v2.1.9/2.2.0 were clean because the release was pre-created.)
- **`<Version>` in `src/Winotch/Winotch.csproj` must match the tag** — the app self-reports this version to the auto-updater. v2.2.0's tag initially shipped still saying 2.1.10.
- CI is currently the **only** compile gate: there's no .NET SDK in the local environment. Push and watch `gh run watch <id> --exit-status`.
- Compose git commit messages via a temp file + `git commit -F` (heredoc transport has been flaky in this environment).
- When an algorithm has boundary conditions (column splits, hover rects, text wrap), **simulate it in `python3` before writing the C#** — this caught real bugs three times (unbalanced column split, 238-flip hover oscillation, wrap overflow).

## Release inventory

| Release | Notes |
|---|---|
| v2.3.0 (Latest) | Linux port (PR #2). tar.gz assets. Tag run **failed** (Windows build). |
| v2.2.0 | 7 critical bug fixes. Windows zip. Tag lives **only** on the qwen branch, not main. |
| v2.1.10 | Empty-state text wrap fix (clipboard/notes hints clipped at card edge). |
| v2.1.9 | Hover oscillation fix + side-pill sweep loop fix (audit-verified: 0 oscillating cells). |
| v2.1.8 | Two-column horizontal open direction (~620px panel, height-balanced columns). |

## Shipped features recap (recent)

Per-widget sizes (compact/standard/large) • vertical text option • two-column Horizontal panel with cross-column reorder • drag-to-seek + volume on Now Playing • precipitation-start toasts (once per event) • vertical/horizontal pill open direction setting • launcher executable scan in setup • wrapped empty-state hints.

## Approved-but-unbuilt feature backlog

- Fullscreen notch hiding (designed above — top of the queue).
- Fuller clipboard card (real entry previews, per-row copy) — was offered, never requested.
- Text-overflow audit of all widget cards at compact size / column widths.
