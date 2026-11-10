#!/usr/bin/env bash
# Builds the offline feed for the closed network: runs on a machine WITH internet, once per Core release.
# 1) pack Core:  eng/pack-core.sh <core-checkout> <version>        -> packages/ (NexusCore packages)
# 2) this script: restores the host + tests from nuget.org into the same folder, so every third-party package is
#    present, writes SHA-256 sums, and proves the folder restores on its own.
set -euo pipefail
cd "$(dirname "$0")/.."
FEED="$PWD/packages"; TMP="$(mktemp -d)"
[ -n "$(ls "$FEED"/Nexus.TaskManagement.*.nupkg 2>/dev/null)" ] || { echo "Pack Core first (eng/pack-core.sh)"; exit 1; }

# Restore with the offline feed + nuget.org into a scratch cache, then copy every downloaded .nupkg into the feed.
dotnet restore TM.Host.sln --source "$FEED" --source https://api.nuget.org/v3/index.json --packages "$TMP"
find "$TMP" -name '*.nupkg' -exec cp -n {} "$FEED"/ \;

( cd "$FEED" && sha256sum *.nupkg > SHA256SUMS.txt )
echo "feed: $(ls "$FEED"/*.nupkg | wc -l) packages"

# Proof: a clean restore from the feed alone.
rm -rf "$TMP"; TMP="$(mktemp -d)"
dotnet restore TM.Host.sln --source "$FEED" --packages "$TMP" && echo "OFFLINE RESTORE OK"
