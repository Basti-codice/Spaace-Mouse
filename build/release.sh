#!/usr/bin/env bash
# Builds tests + the Windows release (portable ZIP, optional NSIS installer). Works on Linux (cross-build) and in Git Bash.
# Requires: .NET 8 SDK. Installer step additionally needs `makensis` (NSIS 3) on PATH.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${VERSION:-1.0.0}"
OUT="dist"
PUB="$OUT/publish"
rm -rf "$OUT"; mkdir -p "$PUB"

echo "== 1/4 tests"
dotnet test tests/SpaceMouse.Core.Tests -c Release --nologo -v q

echo "== 2/4 publish win-x64 (self-contained, single file)"
dotnet publish src/SpaceMouse.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None -p:DebugSymbols=false -p:Version="$VERSION" -o "$PUB" --nologo -v q
cp README.md "$PUB/README.txt"

echo "== 3/4 portable zip"
PORT="$OUT/portable"; mkdir -p "$PORT"
cp "$PUB/SpaceMouseStudio.exe" "$PORT/"
cp README.md "$PORT/README.txt"
: > "$PORT/portable.flag"          # data lives next to the EXE in .\data
( cd "$PORT" && zip -q -r "../SpaceMouseStudio-$VERSION-portable-win-x64.zip" . )

echo "== 4/4 installer"
if command -v makensis >/dev/null 2>&1; then
  makensis -V2 -DVERSION="$VERSION" -DSRCDIR="$(pwd)/$PUB" -DOUTFILE="$(pwd)/$OUT/SpaceMouseStudio-$VERSION-setup.exe" installer/SpaceMouseStudio.nsi
else
  echo "makensis not found - installer skipped (CI builds it)."
fi
echo; ls -la "$OUT"/*.zip "$OUT"/*.exe 2>/dev/null || true
