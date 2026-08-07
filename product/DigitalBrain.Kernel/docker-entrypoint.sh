#!/bin/sh
# Product packaging Option A: one digitalbrain image supervises silo + mcp + behavior-host
# as separate OS processes. Authored assemblies load only in behavior-host. Northbound MCP
# is a cluster client process. No secrets here — inject via env / Key Vault (see Dockerfile).
set -eu

SILO_DIR="${SILO_DIR:-/app/silo}"
MCP_DIR="${MCP_DIR:-/app/mcp}"
BEHAVIOR_HOST_DIR="${BEHAVIOR_HOST_DIR:-/app/behavior-host}"
SILO_URLS="${SILO_ASPNETCORE_URLS:-http://0.0.0.0:8080}"
MCP_URLS="${MCP_ASPNETCORE_URLS:-http://0.0.0.0:5000}"
BEHAVIOR_HOST_URLS="${BEHAVIOR_HOST_ASPNETCORE_URLS:-http://127.0.0.1:8081}"
SILO_HTTP="${SILO_HTTP_BASE:-http://127.0.0.1:8080}"
BEHAVIOR_HOST_HTTP="${BEHAVIOR_HOST_HTTP_BASE:-http://127.0.0.1:8081}"

# In-image wiring defaults (not secrets). Callers may override for multi-container Option B.
export DigitalBrain__Behaviors__Executor="${DigitalBrain__Behaviors__Executor:-Host}"
export DigitalBrain__Behaviors__Host__BaseAddress="${DigitalBrain__Behaviors__Host__BaseAddress:-$BEHAVIOR_HOST_HTTP}"
export DigitalBrain__Behaviors__Broker__BaseAddress="${DigitalBrain__Behaviors__Broker__BaseAddress:-$SILO_HTTP}"

silo_pid=""
mcp_pid=""
behavior_pid=""

terminate() {
  code=0
  if [ -n "$silo_pid" ] && kill -0 "$silo_pid" 2>/dev/null; then
    kill -TERM "$silo_pid" 2>/dev/null || true
  fi
  if [ -n "$mcp_pid" ] && kill -0 "$mcp_pid" 2>/dev/null; then
    kill -TERM "$mcp_pid" 2>/dev/null || true
  fi
  if [ -n "$behavior_pid" ] && kill -0 "$behavior_pid" 2>/dev/null; then
    kill -TERM "$behavior_pid" 2>/dev/null || true
  fi
  if [ -n "$silo_pid" ]; then
    wait "$silo_pid" 2>/dev/null || code=$?
  fi
  if [ -n "$mcp_pid" ]; then
    wait "$mcp_pid" 2>/dev/null || true
  fi
  if [ -n "$behavior_pid" ]; then
    wait "$behavior_pid" 2>/dev/null || true
  fi
  exit "$code"
}

trap terminate INT TERM

# Behavior worker first (loopback); silo residual Host executor dials it after start.
ASPNETCORE_URLS="$BEHAVIOR_HOST_URLS" \
  dotnet "$BEHAVIOR_HOST_DIR/DigitalBrain.BehaviorHost.dll" &
behavior_pid=$!

ASPNETCORE_URLS="$SILO_URLS" \
  dotnet "$SILO_DIR/DigitalBrain.Kernel.dll" &
silo_pid=$!

# Northbound MCP is a client of the silo — start after silo process is up.
ASPNETCORE_URLS="$MCP_URLS" \
  dotnet "$MCP_DIR/DigitalBrain.Mcp.dll" &
mcp_pid=$!

# Exit when any supervised child exits; the others are torn down by the trap.
while kill -0 "$silo_pid" 2>/dev/null \
  && kill -0 "$mcp_pid" 2>/dev/null \
  && kill -0 "$behavior_pid" 2>/dev/null; do
  sleep 1
done

terminate
