@echo off
rem Cross-platform cleanup for Winotch dev ports (Windows version).
rem
rem `tauri dev` expects Vite on a fixed port (1420, HMR on 1421). If a previous
rem dev session crashed or was killed uncleanly, stale node/esbuild processes
rem keep holding the port and the next launch fails with ERR_CONNECTION_REFUSED
rem in the webview. This script kills anything holding those ports plus leftover
rem dev processes, so `npm run tauri dev` always starts from a clean slate.
rem
rem Usage: scripts\kill-dev-ports.bat   (safe to run any time)

setlocal enabledelayedexpansion

rem 1. Kill anything listening on the dev ports.
for %%P in (1420 1421) do (
    for /f "tokens=5" %%A in ('netstat -aon -p tcp 2^>nul ^| findstr /r /c:":%%P .*LISTENING"') do (
        echo killing port %%P: PID %%A
        taskkill /PID %%A /F >nul 2>&1
    )
)

rem 2. Kill leftover frontend dev processes by image name.
taskkill /IM node.exe /F >nul 2>&1

endlocal
exit /b 0
