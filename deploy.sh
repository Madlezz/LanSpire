#!/usr/bin/env bash
# Deploy LanSpire build to game mods folder.
# Usage: ./deploy.sh
# After build: dotnet build -c Release && ./deploy.sh

set -euo pipefail

GAME_DIR="${STS2_GAME_DIR:-D:/Torrent/Games/STS2 B23811903~AG/Slay the Spire 2}"
MODS_DIR="$GAME_DIR/mods/LanSpire"
SRC_DIR="$(dirname "$0")/LanSpire/bin/Release/net9.0"

if [ ! -f "$SRC_DIR/LanSpire.dll" ]; then
    echo "ERROR: Build not found. Run: cd LanSpire && dotnet build -c Release"
    exit 1
fi

if [ ! -d "$MODS_DIR" ]; then
    mkdir -p "$MODS_DIR"
fi

cp "$SRC_DIR/LanSpire.dll" "$MODS_DIR/LanSpire.dll"
cp "$SRC_DIR/LanSpire.pdb" "$MODS_DIR/LanSpire.pdb"
cp "$(dirname "$0")/LanSpire/LanSpire.json" "$MODS_DIR/LanSpire.json"

echo "Deployed to: $MODS_DIR"
cat "$MODS_DIR/LanSpire.json" | grep version
