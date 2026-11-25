#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p dist
LOG="$PWD/dist/build.log"
exec > >(tee "$LOG") 2>&1
trap 'echo "Build failed. See $LOG" >&2' ERR
[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || { echo "Run on an Apple Silicon Mac."; exit 1; }
if ! xcode-select -p >/dev/null 2>&1; then
  echo "Install Apple command line tools, then rerun this script."
  xcode-select --install
  exit 1
fi
swift --version
echo "Resolving dependencies and compiling..."
swift build -c release --arch arm64
BIN="$(swift build -c release --arch arm64 --show-bin-path)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/Sermon Studio.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN/SermonStudio" "$APP/Contents/MacOS/"
cp Info.plist "$APP/Contents/Info.plist"
# Include SwiftPM resource bundles if dependencies provide them.
for bundle in "$BIN"/*.bundle; do
  [[ -d "$bundle" ]] || continue
  cp -R "$bundle" "$APP/Contents/Resources/"
done
plutil -lint "$APP/Contents/Info.plist"
codesign --force --sign - "$APP"
codesign --verify --strict "$APP"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Sermon Studio" -srcfolder "$STAGE" -ov -format UDZO "$PWD/dist/Sermon-Studio-macOS-arm64.dmg"
echo "Installer ready: $PWD/dist/Sermon-Studio-macOS-arm64.dmg"
open -R "$PWD/dist/Sermon-Studio-macOS-arm64.dmg"
