#!/usr/bin/env bash
# Linux/macOS twin of Core's eng/pack-core-modules.ps1 (same project list, same flags), for machines without PowerShell.
# Usage: eng/pack-core.sh <path-to-core-checkout> <package-version> [output-dir]
set -euo pipefail
CORE="$1"; VER="$2"; OUT="${3:-$(dirname "$0")/../packages}"
mkdir -p "$OUT"
grep -oE '"[A-Za-z0-9_.]+/[A-Za-z0-9_.]+\.csproj"' "$CORE/eng/pack-core-modules.ps1" | tr -d '"' | awk '!s[$0]++' |
while read -r p; do
  echo "== $p"
  dotnet pack "$CORE/$p" -c Release -p:PackageVersion="$VER" -p:IsPackable=true -p:ContinuousIntegrationBuild=true -p:IncludeSymbols=false -o "$OUT" -v q
done
echo "packages: $(ls "$OUT"/*.nupkg | wc -l)"
