#!/usr/bin/env bash
# Instantiate intochat-app and intochat-module from DigitalBrain.Templates, then build and test each instance
# from a clean package cache. The only remote host is nuget.org.
#
# Usage:
#   template-smoke.sh local <workdir> <artifacts>   templates and DigitalBrain* packages from the <artifacts> folder,
#                                                   everything else from nuget.org (throwaway nuget.config per instance)
#   template-smoke.sh feed  <workdir> <version>     DigitalBrain.Templates <version> from nuget.org; waits up to
#                                                   20 minutes for it to be indexed; instances keep their own nuget.config
#   workdir is an empty directory outside the repository.
set -euo pipefail

mode=${1:?mode: local or feed}
workdir=${2:?workdir}
source=${3:?artifacts folder (local) or version (feed)}

nuget_org=https://api.nuget.org/v3/index.json

mkdir -p "$workdir"
workdir=$(cd "$workdir" && pwd -W 2>/dev/null || pwd)

# Clean machine: fresh package cache and fresh CLI home (template installs land there, not in the user's home).
export NUGET_PACKAGES="$workdir/.nuget/packages"
export DOTNET_CLI_HOME="$workdir/.dotnet-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$NUGET_PACKAGES" "$DOTNET_CLI_HOME"

case "$mode" in
  local)
    packages=$(cd "$source" && pwd -W 2>/dev/null || pwd)
    pack=$(ls -t "$packages"/DigitalBrain.Templates.*.nupkg 2>/dev/null | head -n 1 || true)
    [ -n "$pack" ] || { echo "no DigitalBrain.Templates package in $packages" >&2; exit 1; }
    dotnet new install "$pack" --force >/dev/null
    ;;
  feed)
    version=$source
    flat="https://api.nuget.org/v3-flatcontainer/digitalbrain.templates/$version/digitalbrain.templates.$version.nupkg"
    deadline=$(( $(date +%s) + 1200 ))
    until curl -fsI "$flat" >/dev/null 2>&1; do
      [ "$(date +%s)" -lt "$deadline" ] || { echo "DigitalBrain.Templates $version not on nuget.org after 20 minutes" >&2; exit 1; }
      sleep 30
    done
    dotnet new install "DigitalBrain.Templates::$version" --nuget-source "$nuget_org" --force >/dev/null
    ;;
  *) echo "mode must be local or feed" >&2; exit 2 ;;
esac

cleanup() { dotnet new uninstall DigitalBrain.Templates >/dev/null 2>&1 || true; }
trap cleanup EXIT

write_local_config() {
  cat > "$1/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages" />
    <add key="nuget.org" value="$nuget_org" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="DigitalBrain*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
}

# instantiate <template> <name> <id-option> <id> <test-project-relative-path>
instantiate() {
  local template=$1 name=$2 option=$3 id=$4
  local dir="$workdir/$template/$name"
  dotnet new "$template" -n "$name" "$option" "$id" -o "$dir" >/dev/null
  [ "$mode" = "feed" ] || write_local_config "$dir"
  echo "$dir"
}

run() {
  local dir=$1 tests=$2 started
  started=$(date +%s)
  (cd "$dir" && dotnet build "$tests" -c Release >/dev/null)
  (cd "$dir" && dotnet test --project "$tests" -c Release --no-build)
  echo "template-smoke: $(basename "$(dirname "$dir")")/$(basename "$dir") ok in $(( $(date +%s) - started ))s"
}

for entry in Notes:acme.notes Foo.Bar:acme.foo-bar; do
  name=${entry%%:*}
  dir=$(instantiate intochat-app "$name" --AppId "${entry##*:}")
  run "$dir" "$name.Tests.Unit/$name.Tests.Unit.csproj"
done

for entry in Weather:acme.weather MyModule:acme.my-module; do
  name=${entry%%:*}
  dir=$(instantiate intochat-module "$name" --ModuleId "${entry##*:}")
  run "$dir" "tests/$name.Tests.Unit/$name.Tests.Unit.csproj"
done

# An invalid module id instantiates (the template engine has no validation) but the generated test rejects it.
bad=$(instantiate intochat-module Bad --ModuleId "Bad Id")
if (cd "$bad" && dotnet test --project tests/Bad.Tests.Unit/Bad.Tests.Unit.csproj -c Release >/dev/null 2>&1); then
  echo "template-smoke: --ModuleId 'Bad Id' was accepted" >&2
  exit 1
fi
echo "template-smoke: 'Bad Id' rejected"

# The DigitalBrain analyzers flow to consumers: a module without its Manifest fails the build with DB0001.
probe=$(instantiate intochat-module Probe --ModuleId acme.probe)
sed -i '/public static ModuleManifest Manifest/,/Secrets: \[\]);/d' "$probe/Probe/ProbeModule.cs"
if output=$(cd "$probe" && dotnet build Probe/Probe.csproj -c Release 2>&1); then
  echo "template-smoke: a module without Manifest built; analyzers did not flow" >&2
  exit 1
fi
echo "$output" | grep -q "DB0001" || { echo "$output" >&2; echo "template-smoke: build failed without DB0001" >&2; exit 1; }
echo "template-smoke: DB0001 reported from the packed analyzers"
