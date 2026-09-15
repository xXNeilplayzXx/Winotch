# HANDOFF — where we left off

_Last updated: 2026-09-15, after v2.3.4._

## Current state (TL;DR)

- **Every release is a pre-release.** Newest: **v2.3.4**. Publishing as a pre-release is deliberate and permanent — see
  "Release policy" below, because it changes more than the flag on the release page.
- **CI is green on `main`** on all three gates: `build-test-release` (Windows build + Linux build + 146 unit tests) and
  `windows-smoke` (launches the real exe on a real Windows runner).
- `main` is the only live branch. `legacy` holds the old WebView2/Tauri app; `linux` is a porting branch kept for
  reference. Version history on `main`: … 2.1.10 → **2.2.0 was cut on a branch and never merged** (its fixes *are* in
  `main` now, re-applied) → 2.3.0 → 2.3.1 → 2.3.2 → 2.3.3 → 2.3.4.
- Local environment has **no .NET SDK**. CI is the only compiler and the only place the app is ever executed. Push and
  watch: `gh run watch <id> --exit-status`.

## What the 2.3.x line fixed (and why it matters when touching the Windows host)

The Linux port (`275f12b`) rewrote the Windows overlay as Avalonia code that had never been run on Windows. Everything
below was invisible to a compile and to unit tests, and each one made the app look "dead" or wrong on a real desktop:

| Version | Fix |
|---|---|
| 2.3.1 | Build: restored the conditional Windows TFM; `#if WINDOWS` guards on `Platform/Windows/*.cs`; `FakeWindowsThemeSource` renamed to `FakeThemeSource`. |
| 2.3.2 | Nothing actually worked yet — the "fix" only repackaged a framework-dependent build. Do not trust a zip's size alone: self-contained is ~80 MB, framework-dependent ~10 MB. |
| 2.3.3 | Launch: the Windows host never started the shell's frame loop (only Linux did); the renderer PNG-encoded a full-screen surface per frame and handed zero-sized surfaces to Skia; the WndProc chain called a null pointer; WPF resources (`FontFamily`, tray icon pack URI) were used inside an Avalonia app. Also: all eight editor dialogs called Avalonia's `ShowDialog` with a **null owner**, which WPF allowed and Avalonia rejects — fatal on every first run. |
| 2.3.4 | The overlay covered the whole screen: `TransparencyLevelHint` listed **Mica** first, and on Windows 11 the window material fills the entire (monitor-sized) window. Now transparency only, with a transparent fallback brush. Also fixed DIP coordinates being passed to `Window.Position`, which is in **physical** pixels. |

## Release policy: pre-releases, always

Decided by the maintainer: every build ships as a GitHub **pre-release** ("testing build"), from v2.3.4 on. That is not
just a flag — `releases/latest` and everything built on it ignore pre-releases, so three things had to change with it:

1. **`.github/workflows/build-release.yml`** — on a tag it now creates the release if it is missing *and* forces
   `--prerelease`, so a tag can never publish itself as a stable "Latest" release.
2. **`src/Winotch/UpdateService.cs`** — the in-app updater reads `releases?per_page=30` and picks the highest version
   (skipping drafts, and releases without a win-x64 zip). With `releases/latest` it would have reported "no update" for
   ever, or 404'd once no stable release remained. Covered by tests in `tests/Winotch.Tests/UpdateServiceTests.cs`.
3. **`install.ps1` / `install.sh` / README** — installers resolve the newest release through the API (or `WINOTCH_URL`)
   and fetch the real asset names, `Winotch-vX.Y.Z-x64.zip` / `-linux-x64.zip`.

Consequence to keep in mind: the repo homepage and the Releases page will show a **stale "Latest"** badge on the last
stable release. Point people at "Show pre-releases", or the installers.

## Release mechanics (hard-won, don't relearn these)

- **`<Version>` in `src/Winotch/Winotch.csproj` must match the tag.** The app reports it to its own updater; v2.2.0 once
  shipped still saying 2.1.10, and no installed build was ever considered current.
- **The upload step needs the release to exist.** Creating it yourself with notes before pushing the tag is still nicest
  (the workflow's fallback creates one with a generic title). The workflow now also tolerates a missing release, but the
  notes are better when human-written.
- Tags are cut from a **version-stamp commit**: bump `<Version>`, commit, push, wait for green, then tag and push.
- Compose commit messages via a temp file + `git commit -F` — heredoc transport is flaky in this environment.
- **Simulate algorithms in `python3` before writing C#** when they have boundary conditions (column splits, hover rects,
  text wrap, the releases-list version pick). It has caught real bugs repeatedly.
- Editing app code from a Linux/macOS box is fine, but *nothing* on Windows can be verified without a CI run — rely on
  the smoke job, not on reasoning about Win32.

## Verification infrastructure

- `.github/workflows/windows-smoke.yml` launches the built `Winotch.exe` on a fresh `windows-latest` runner and fails on:
  a process that exits during start-up, a `FATAL` line in the log, a frame loop that never starts, no rendered frame, a
  window that resolves to a backdrop material instead of `Transparent`, or a frame that is opaque away from the island
  (which is exactly "the app is covering my screen").
- The app logs to `%LOCALAPPDATA%\Winotch\logs\winotch.log` (`AppLog`) and shows a message box on a fatal start-up
  failure, so a launch failure is never silent again. Useful lines: `overlay transparency: …`, `overlay placed at …`,
  `overlay frame is transparent outside the island`, `FATAL (startup): …`.
- Unit tests: `tests/Winotch.Tests` (146 tests, Windows-only project because it touches Win32/WinForms types).

## Open items / next steps

1. **Verify v2.3.4 on the maintainer's desktop** — especially on a scaled display (125 %/150 %) or multi-monitor setup,
   where the `Window.Position` unit fix changes placement. The log lines above are the fastest way to diagnose.
2. **Fullscreen notch hiding** — approved, designed (hide when a foreground window covers ≥98 % of a monitor, plus a
   "Keep notch visible in fullscreen" toggle), never built. `HideNotch`/`ShowNotch`/`SetPaused` hooks exist;
   `MonitorTargeting.cs` is where the probe belongs.
3. **The Linux host has never been smoke-tested.** It has no Avalonia window, draws straight to X11, and its dialogs now
   fall back to non-modal behaviour. It shares the same port that produced every bug above.
4. **Shrink the overlay window** to the island's bounds rather than a whole monitor, so no window-level mistake can ever
   cover the screen again.
5. Housekeeping: stray `.DS_Store` files in the repo root are untracked — delete and gitignore.
