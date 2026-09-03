@echo off
rem One-command release: build winotch.exe and attach it to a GitHub release.
rem
rem Winotch is Windows-only, so this MUST run on the Windows machine - Tauri
rem cannot cross-build the Windows exe from macOS. Steps it performs:
rem   1. Sanity-checks the toolchain (git, gh, Node)
rem   2. tauri release build via the prebuilt CLI (npm exec @tauri-apps/cli)
rem   3. Uploads winotch.exe to the release via the GitHub CLI
rem
rem Usage:  scripts\release.bat [TAG]
rem         (default TAG: newest v* tag, e.g. v1.0.0)
rem
rem Example: scripts\release.bat v1.0.0

setlocal

rem 0. Resolve the release tag (first arg, else the newest v* tag).
set "TAG=%~1"
if "%TAG%"=="" (
    git tag --sort=-v:refname > "%TEMP%\winotch-tags.txt" 2>nul
    set /p TAG=<"%TEMP%\winotch-tags.txt"
)
if "%TAG%"=="" (
    echo [release] Could not determine a release tag. Pass one explicitly:
    echo           scripts\release.bat v1.0.0
    exit /b 1
)
echo [release] Target release: %TAG%

rem 1. Toolchain sanity checks.
where git >nul 2>nul || (echo [release] git not found - install from https://git-scm.com & exit /b 1)
where gh >nul 2>nul || (echo [release] gh not found - install GitHub CLI: winget install GitHub.cli & exit /b 1)
where npm >nul 2>nul || (echo [release] npm not found - install Node.js from https://nodejs.org & exit /b 1)

rem 2. Release build from webview/ (the Tauri project). The frontend is plain
rem    HTML/CSS/JS, so there is no npm build step. --no-bundle skips
rem    installers/MSI; we only want the raw exe.
echo [release] Building winotch.exe... first run downloads and compiles a lot.
pushd webview
call npm exec --yes --package @tauri-apps/cli@^2 -- tauri build --no-bundle
if errorlevel 1 (popd & exit /b 1)
popd

rem 3. Locate and attach the exe.
set "EXE=webview\src-tauri\target\release\winotch.exe"
if not exist "%EXE%" (
    echo [release] Expected exe missing: %EXE%
    echo           If your toolchain targets a different triple, search under webview\src-tauri\target\release\
    exit /b 1
)
gh release upload "%TAG%" "%EXE%" || exit /b 1
echo [release] Done - attached winotch.exe to %TAG%