#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${GH_TOKEN:?Set GH_TOKEN in Codemagic release environment group}"
: "${SPARKLE_PRIVATE_KEY:?Set SPARKLE_PRIVATE_KEY in Codemagic release environment group}"
: "${SERMON_VERSION:?Set SERMON_VERSION in release workflow}"
: "${SERMON_BUILD_NUMBER:?Set SERMON_BUILD_NUMBER in release workflow}"
REPO="jrcrosby91-tech/Sermon-Studio"
TAG="mac-v$SERMON_VERSION"
ZIP="$PWD/macos/dist/Sermon-Studio-macOS-arm64.zip"
DMG="$PWD/macos/dist/Sermon-Studio-macOS-arm64.dmg"
test -s "$ZIP" && test -s "$DMG"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Obtain the official Sparkle signing utility, separate from the SwiftPM framework archive.
curl --fail --location --retry 5 --retry-all-errors --output "$TMP/sparkle.tar.xz" \
  https://github.com/sparkle-project/Sparkle/releases/download/2.7.1/Sparkle-2.7.1.tar.xz
tar -xf "$TMP/sparkle.tar.xz" -C "$TMP"
SIGN_TOOL="$(find "$TMP" -type f -name sign_update -print -quit)"
test -n "$SIGN_TOOL" || { echo "Sparkle sign_update tool not found"; exit 1; }
chmod +x "$SIGN_TOOL"
SIGNATURE="$(printf '%s' "$SPARKLE_PRIVATE_KEY" | "$SIGN_TOOL" --ed-key-file - "$ZIP")"
printf '%s\n' "$SIGNATURE" | grep -q 'sparkle:edSignature=' || { echo "Missing Sparkle EdDSA signature"; exit 1; }

# Read the latest published appcast so no existing updates are lost.
gh api "repos/$REPO/contents/appcast.xml" > "$TMP/appcast-api.json"
python3 - "$TMP/appcast-api.json" "$TMP/appcast.xml" <<'PY'
import sys, json, base64
obj=json.load(open(sys.argv[1]))
open(sys.argv[2],"wb").write(base64.b64decode(obj["content"]))
PY

python3 - "$TMP/appcast.xml" "$SERMON_VERSION" "$SERMON_BUILD_NUMBER" "$SIGNATURE" "$ZIP" <<'PY'
import os,re,sys,xml.etree.ElementTree as ET
path,version,build,signature,zip_path=sys.argv[1:]
ns='http://www.andymatuschak.org/xml-namespaces/sparkle'
key='{'+ns+'}'
match=re.search(r'sparkle:edSignature="([^"]+)"',signature)
if not match: raise SystemExit("Sparkle signature output missing edSignature")
ET.register_namespace('sparkle',ns)
tree=ET.parse(path)
channel=tree.getroot().find('channel')
if channel is None: raise SystemExit("Invalid appcast channel")
for item in channel.findall('item'):
    for enclosure in item.findall('enclosure'):
        if int(enclosure.get(key+'version','0')) >= int(build):
            raise SystemExit("Build number must exceed all existing appcast entries")
item=ET.SubElement(channel,'item')
ET.SubElement(item,'title').text='Sermon Studio '+version
ET.SubElement(item,'enclosure',{
    'url':f'https://github.com/jrcrosby91-tech/Sermon-Studio/releases/download/mac-v{version}/Sermon-Studio-macOS-arm64.zip',
    key+'version':build,
    key+'shortVersionString':version,
    key+'edSignature':match.group(1),
    'length':str(os.path.getsize(zip_path)),
    'type':'application/octet-stream'
})
tree.write(path,encoding='utf-8',xml_declaration=True)
PY

# Publish artifacts first; never advertise an update before its ZIP is available.
gh release create "$TAG" "$ZIP" "$DMG" -R "$REPO" --target "${CM_COMMIT:-main}" \
  --title "Sermon Studio $SERMON_VERSION" --generate-notes

# Publish appcast through the authenticated GitHub Contents API.
python3 - "$TMP/appcast.xml" "$TMP/appcast-api.json" "$TMP/update.json" "$SERMON_VERSION" <<'PY'
import sys,json,base64
content,original,dest,version=sys.argv[1:]
sha=json.load(open(original))['sha']
payload={'message':f'Publish signed macOS update {version}',
         'sha':sha,'branch':'main',
         'content':base64.b64encode(open(content,'rb').read()).decode('ascii')}
json.dump(payload,open(dest,'w'))
PY
gh api -X PUT "repos/$REPO/contents/appcast.xml" --input "$TMP/update.json" --jq '.commit.sha'
echo "Published signed Sparkle update $SERMON_VERSION (build $SERMON_BUILD_NUMBER)"
