#!/usr/bin/env bash
# Cross-platform (macOS/Linux) cleanup for Winotch dev ports.
#
# `tauri dev` expects Vite on a fixed port (1420, HMR on 1421). If a previous
# dev session crashed or was killed uncleanly, stale node/esbuild/tauri
# processes keep holding the port and the next launch fails with
# ERR_CONNECTION_REFUSED in the webview. This script kills anything holding
# those ports plus leftover dev processes, so `npm run tauri dev` always starts
# from a clean slate.
#
# Usage: scripts/kill-dev-ports.sh   (safe to run any time; exits 0 even when
# there is nothing to kill)

set +e

PORTS=(1420 1421)

# 1. Kill anything listening on the dev ports.
for port in "${PORTS[@]}"; do
  pids="$(lsof -nP -tiTCP:"$port" -sTCP:LISTEN 2>/dev/null)"
  if [ -n "$pids" ]; then
    echo "killing port $port: $(echo "$pids" | tr '\n' ' ')"
    kill $pids 2>/dev/null
  fi
done

# 2. Kill leftover frontend dev processes by name (they may linger without an
#    open port for a moment after a crash).
pkill -f "vite" 2>/dev/null
pkill -f "esbuild" 2>/dev/null

# 3. Give the OS a moment to release the sockets, then force-kill survivors.
sleep 1
for port in "${PORTS[@]}"; do
  pids="$(lsof -nP -tiTCP:"$port" -sTCP:LISTEN 2>/dev/null)"
  if [ -n "$pids" ]; then
    kill -9 $pids 2>/dev/null
  fi
done

exit 0
