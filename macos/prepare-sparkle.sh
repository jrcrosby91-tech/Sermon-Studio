#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p .codemagic-sparkle
archive="$(mktemp)"
trap 'rm -f "$archive"' EXIT
curl --fail --location --retry 5 --retry-all-errors --connect-timeout 30 --output "$archive" https://github.com/sparkle-project/Sparkle/releases/download/2.7.1/Sparkle-for-Swift-Package-Manager.zip
unzip -t "$archive" >/dev/null
unzip -q "$archive" -d .codemagic-sparkle
python3 - <<'PY'
from pathlib import Path
base = Path(".codemagic-sparkle")
frameworks = list(base.rglob("Sparkle.xcframework"))
if len(frameworks) != 1:
    raise SystemExit(f"Expected one Sparkle.xcframework, found {len(frameworks)}")
framework = frameworks[0].relative_to(base).as_posix()
(base / "Package.swift").write_text(
    '// swift-tools-version: 5.9\nimport PackageDescription\n'
    'let package = Package(name: "Sparkle", platforms: [.macOS(.v10_13)], '
    'products: [.library(name: "Sparkle", targets: ["Sparkle"])], '
    f'targets: [.binaryTarget(name: "Sparkle", path: "{framework}")])\n'
)
manifest = Path("Package.swift")
source = manifest.read_text()
old = '.package(url: "https://github.com/sparkle-project/Sparkle.git", exact: "2.7.1")'
new = '.package(name: "Sparkle", path: ".codemagic-sparkle")'
if old not in source:
    raise SystemExit("Unexpected Sparkle dependency declaration")
manifest.write_text(source.replace(old, new))
print("Using verified local Sparkle framework.")
PY
