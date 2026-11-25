Native 3.0 preview. Requires macOS 14+ and Apple Silicon. Run swift build in this directory or use the GitHub Actions macOS workflow. Initial model download requires internet. Import web JSON backups through Sermon library. TXT and Markdown exports only in this preview. Ad-hoc signed, not notarized. Existing web app remains separate.

## Local build
On an Apple Silicon Mac, install Xcode or command line tools (`xcode-select --install`). Clone the repository, then run `bash macos/build-local.sh` from its root. The script compiles, packages, ad-hoc signs and creates `macos/dist/Sermon-Studio-macOS-arm64.dmg`. Build output is saved to `macos/dist/build.log`. To rebuild, run `git pull --ff-only` then rerun the script. No automatic installation or archive changes are performed.
