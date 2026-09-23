#!/bin/sh
# Product packaging: the digitalbrain-kernel image runs the silo process.
# No secrets here — inject via env / Key Vault.
set -eu

SILO_DIR="${SILO_DIR:-/app/silo}"
SILO_URLS="${SILO_ASPNETCORE_URLS:-http://0.0.0.0:8080}"

exec env ASPNETCORE_URLS="$SILO_URLS" dotnet "$SILO_DIR/IntoChat.dll"
