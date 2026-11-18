#!/usr/bin/env bash
# Build the mod (Release) and stage the loader layout in dist/RelockAscensions/.
# Needs local.props (see local.props.example) pointing at the game's managed DLLs.
set -euo pipefail
cd "$(dirname "$0")/.."
id=RelockAscensions
out=dist/$id
dotnet build src/$id -c Release -nologo -v quiet
rm -rf "$out"
mkdir -p "$out"
cp "src/$id/bin/Release/net9.0/$id.dll" "src/$id/bin/Release/net9.0/$id.json" "$out/"
echo "Staged $(pwd)/$out:"
ls -1 "$out"
