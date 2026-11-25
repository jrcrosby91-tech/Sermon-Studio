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
FRAMEWORK="$(find "$PWD/.build/artifacts" -type d -name Sparkle.framework -print -quit 2>/dev/null || true)"
if [[ -z "$FRAMEWORK" && -d "$PWD/.codemagic-sparkle" ]]; then
  FRAMEWORK="$(find "$PWD/.codemagic-sparkle" -type d -name Sparkle.framework -print -quit)"
fi
[[ -n "$FRAMEWORK" ]] || { echo "Sparkle framework not found"; exit 1; }
mkdir -p "$APP/Contents/Frameworks"
ditto "$FRAMEWORK" "$APP/Contents/Frameworks/Sparkle.framework"
if ! otool -l "$APP/Contents/MacOS/SermonStudio" | grep -q '@executable_path/../Frameworks'; then
  install_name_tool -add_rpath '@executable_path/../Frameworks' "$APP/Contents/MacOS/SermonStudio"
fi
SERMON_UPDATE_PUBLIC_KEY="${SERMON_UPDATE_PUBLIC_KEY:-aA1xrq1M6yud/bEMlOzTswjmIKOAZpMOrgR4zX8o0CE=}"
if [[ "$SERMON_UPDATE_PUBLIC_KEY" != "aA1xrq1M6yud/bEMlOzTswjmIKOAZpMOrgR4zX8o0CE=" ]]; then
  echo "Signing public key does not match the configured release key."; exit 1
fi
if [[ -n "$SERMON_UPDATE_PUBLIC_KEY" ]]; then
  /usr/libexec/PlistBuddy -c "Add :SUPublicEDKey string $SERMON_UPDATE_PUBLIC_KEY" "$APP/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Add :SUFeedURL string https://jrcrosby91-tech.github.io/Sermon-Studio/appcast.xml" "$APP/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Add :SUAllowsAutomaticUpdates bool false" "$APP/Contents/Info.plist"
fi
if [[ -n "${SERMON_BUILD_NUMBER:-}" ]]; then
  [[ "$SERMON_BUILD_NUMBER" =~ ^[0-9]+$ ]] || exit 1
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion $SERMON_BUILD_NUMBER" "$APP/Contents/Info.plist"
fi
if [[ -n "${SERMON_VERSION:-}" ]]; then
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $SERMON_VERSION" "$APP/Contents/Info.plist"
fi
plutil -lint "$APP/Contents/Info.plist"
echo "Removing extended attributes from staged application..."
xattr -cr "$APP"
codesign --force --deep --sign - "$APP"
codesign --verify --strict "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$PWD/dist/Sermon-Studio-macOS-arm64.zip"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Sermon Studio" -srcfolder "$STAGE" -ov -format UDZO "$PWD/dist/Sermon-Studio-macOS-arm64.dmg"
echo "Installer ready: $PWD/dist/Sermon-Studio-macOS-arm64.dmg"
if [[ -z "${CI:-}" ]]; then open -R "$PWD/dist/Sermon-Studio-macOS-arm64.dmg"; fi
