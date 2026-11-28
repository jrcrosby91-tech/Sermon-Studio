#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:?Usage: release.sh VERSION BUILD_NUMBER}"
BUILD="${2:?Provide increasing build number}"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$BUILD" =~ ^[1-9][0-9]*$ ]]
command -v gh >/dev/null || { echo "Install GitHub CLI and authenticate with gh auth login"; exit 1; }
gh auth status
[[ -z "$(git status --porcelain --untracked-files=no)" ]] || { echo "Commit tracked changes first"; exit 1; }
git fetch origin main
[[ "$(git rev-parse HEAD)" == "$(git rev-parse origin/main)" ]] || { echo "Build from current main"; exit 1; }
TAG="mac-v$VERSION"
if gh release view "$TAG" >/dev/null 2>&1; then echo "Release already exists"; exit 1; fi
python3 - "$BUILD" <<'PY'
import sys, xml.etree.ElementTree as E
ns='{http://www.andymatuschak.org/xml-namespaces/sparkle}'
for item in E.parse('appcast.xml').iter('item'):
    for enclosure in item.findall('enclosure'):
        if int(enclosure.get(ns+'version','0')) >= int(sys.argv[1]):
            raise SystemExit('Build number must increase')
PY
# Never publish a build whose automated tests fail.
(cd macos && swift test --parallel)
CI=1 SERMON_VERSION="$VERSION" SERMON_BUILD_NUMBER="$BUILD" bash macos/build-local.sh
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
TOOLS="${SPARKLE_TOOLS:-}"
if [[ -z "$TOOLS" ]]; then
  curl -fLsS https://github.com/sparkle-project/Sparkle/releases/download/2.7.1/Sparkle-2.7.1.tar.xz -o "$TMP/sparkle.tar.xz"
  tar -xf "$TMP/sparkle.tar.xz" -C "$TMP"
  TOOLS="$(dirname "$(find "$TMP" -type f -name sign_update -print -quit)")"
fi
ZIP="$PWD/macos/dist/Sermon-Studio-macOS-arm64.zip"
if [[ -n "${SPARKLE_PRIVATE_KEY:-}" ]]; then
  SIGNATURE="$(printf '%s' "$SPARKLE_PRIVATE_KEY" | "$TOOLS/sign_update" --ed-key-file - "$ZIP")"
else
  SIGNATURE="$("$TOOLS/sign_update" "$ZIP")"
fi
python3 - "$VERSION" "$BUILD" "$SIGNATURE" "$ZIP" <<'PY'
import sys,re,os,xml.etree.ElementTree as E
v,b,s,z=sys.argv[1:]
sig=re.search(r'sparkle:edSignature="([^"]+)"',s)
if not sig: raise SystemExit('Missing update signature')
ns='http://www.andymatuschak.org/xml-namespaces/sparkle'
E.register_namespace('sparkle',ns)
tree=E.parse('appcast.xml')
channel=tree.getroot().find('channel')
item=E.Element('item')
E.SubElement(item,'title').text='Sermon Studio '+v
E.SubElement(item,'enclosure',{'url':'https://github.com/jrcrosby91-tech/Sermon-Studio/releases/download/mac-v'+v+'/Sermon-Studio-macOS-arm64.zip','{'+ns+'}version':b,'{'+ns+'}shortVersionString':v,'{'+ns+'}edSignature':sig.group(1),'length':str(os.path.getsize(z)),'type':'application/octet-stream'})
channel.append(item)
tree.write('appcast.xml',encoding='utf-8',xml_declaration=True)
PY
gh release create "$TAG" "$ZIP" macos/dist/Sermon-Studio-macOS-arm64.dmg --target "$(git rev-parse HEAD)" --title "Sermon Studio $VERSION" --generate-notes
git add appcast.xml
git commit -m "Publish signed macOS update $VERSION"
git push origin HEAD:main
