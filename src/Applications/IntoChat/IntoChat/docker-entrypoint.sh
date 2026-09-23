#!/bin/sh
# Hosted product packaging: the digitalbrain-kernel image runs the silo process only.
# No secrets here — inject via env / managed identity + Key Vault.
# The product module list is baked into the image (Dockerfile ENV / Container.pubxml), so this
# entrypoint never loads the Windows developer executor.
set -eu

SILO_DIR="${SILO_DIR:-/app/silo}"
SILO_URLS="${SILO_ASPNETCORE_URLS:-http://0.0.0.0:8080}"

if [ ! -f "$SILO_DIR/IntoChat.dll" ]; then
    echo "IntoChat.dll was not found under $SILO_DIR; the image is incomplete." >&2
    exit 1
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export IntoChat__Hosted__Enabled="${IntoChat__Hosted__Enabled:-true}"

exec env ASPNETCORE_URLS="$SILO_URLS" dotnet "$SILO_DIR/IntoChat.dll"
