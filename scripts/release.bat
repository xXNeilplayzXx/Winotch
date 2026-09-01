@echo off
rem One-command release: build winotch.exe and attach it to a GitHub release.
rem
rem Winotch is Windows-only, so this MUST run on the Windows machine - Tauri
rem cannot cross-build the Windows exe from macOS. Steps it performs:
rem   1. Sanity-checks the toolchain (cargo, gh, Node deps)
rem   2. cargo/tauri release build  (tauri build runs `npm run build` itself)
rem   3. Uploads winotch.exe to the release via the GitHub CLI
rem
rem Usage:  scripts\release.bat [TAG]
rem         (default TAG: newest v* tag, e.g. v0.2.8)
rem
rem Example: scripts\release.bat v0.2.8

setlocal

rem 0. Resolve the release tag (first arg, else the newest v* tag).
set "TAG=%~1"
if "%TAG%"=="" (
    git tag --sort=-v:refname > "%TEMP%\winotch-tags.txt" 2>nul
    set /p TAG=<"%TEMP%\winotch-tags.txt"
)
if "%TAG%"=="" (
    echo [release] Could not determine a release tag. Pass one explicitly:
    echo           scripts\release.bat v0.2.8
    exit /b 1
)
echo [release] Target release: %TAG%

rem 1. Toolchain sanity checks.
where cargo >nul 2>nul || (echo [release] cargo not found - install Rust from https://rustup.rs & exit /b 1)
where gh >nul 2>nul || (echo [release] gh not found - install GitHub CLI: winget install GitHub.cli & exit /b 1)
if not exist node_modules (
    echo [release] Installing frontend dependencies (npm ci)...
    call npm ci || exit /b 1
)

rem 2. Release build. --no-bundle skips installers/MSI; we only want the raw exe.
echo [release] Building winotch.exe... first run downloads and compiles a lot.
call npx tauri build --no-bundle || exit /b 1

rem 3. Locate and attach the exe.
set "EXE=src-tauri\target\release\winotch.exe"
if not exist "%EXE%" (
    echo [release] Expected exe missing: %EXE%
    echo           If your toolchain targets a different triple, search under src-tauri\target\release\
    exit /b 1
)
gh release upload "%TAG%" "%EXE%" || exit /b 1
echo [release] Done - attached winotch.exe to %TAG%