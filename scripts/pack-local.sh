#!/usr/bin/env bash
# Pack the seven DigitalBrain packages as <VERSION>-<suffix> into a flat local feed (outside every repository).
#
# Usage: scripts/pack-local.sh <preview-number> [feed]
#   scripts/pack-local.sh 1                 -> 0.6.0-preview.1 into E:/intochat/local-feed
#   scripts/pack-local.sh 2 /path/to/feed   -> 0.6.0-preview.2 into /path/to/feed
# Bump the preview number after every digitalbrain change; consumers pin the exact version.
set -euo pipefail

n=${1:?preview number, e.g. 1}
feed=${2:-E:/intochat/local-feed}
root=$(cd "$(dirname "$0")/.." && pwd)
version="$(tr -d '[:space:]' < "$root/VERSION")-preview.$n"

mkdir -p "$feed"
for id in DigitalBrain DigitalBrain.UI DigitalBrain.Testing DigitalBrain.Analyzers DigitalBrain.Templates DigitalBrain.Runtime.Orleans DigitalBrain.Runtime.Orleans.Azure DigitalBrain.Capabilities.Storage DigitalBrain.Capabilities.Secrets DigitalBrain.Capabilities.Identity; do
  if [ -e "$feed/$id.$version.nupkg" ]; then
    echo "$feed/$id.$version.nupkg exists; bump the preview number" >&2
    exit 1
  fi
done

dotnet pack "$root/DigitalBrain.slnx" -c Release -o "$feed" "-p:VersionSuffix=preview.$n"
dotnet pack "$root/templates/DigitalBrain.Templates/DigitalBrain.Templates.csproj" -c Release -o "$feed" "-p:VersionSuffix=preview.$n"

echo "packed $version into $feed:"
ls "$feed" | grep -F ".$version." || true
