#!/usr/bin/env bash
# Build the mod (Release) and stage the loader layout in dist/RelockAscensions/:
# the DLL, the manifest, a PCK holding the localization (scripts/pack_pck.py), and the optional
# BaseLib config DLL (loaded by the mod only when BaseLib is installed).
# Needs local.props (see local.props.example) pointing at the game's managed DLLs.
set -euo pipefail
cd "$(dirname "$0")/.."
id=RelockAscensions
out=dist/$id
dotnet build src/$id.BaseLibConfig -c Release -nologo -v quiet
rm -rf "$out"
mkdir -p "$out"
cp "src/$id/bin/Release/net9.0/$id.dll" "src/$id/bin/Release/net9.0/$id.json" "$out/"
cp "src/$id.BaseLibConfig/bin/Release/net9.0/$id.BaseLibConfig.dll" "$out/"
python3 scripts/pack_pck.py "$out/$id.pck" "$id/localization" "src/$id/localization"
echo "Staged $(pwd)/$out:"
ls -1 "$out"
