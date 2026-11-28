# Sermon Studio for macOS

Native SwiftUI app for macOS 14 or later on Apple Silicon. Sermons, recordings and AI processing stay on your Mac.

## What it does
- **Transcription:** offline WhisperKit models from Tiny to Large v3 Turbo. Models run on the Neural Engine, are processed in 10‑minute sections, and include model management, progress, cancellation with partial-transcript recovery, and biblical vocabulary prompts.
- **Recording playback:** play and pause, ±15 s skips, playback speed, an interactive waveform, timestamp navigation and follow-playback highlighting.
- **Transcript editing:** original and cleaned versions with comparison, find and replace with match counts, word counts, native undo and spell checking.
- **Outlines:** a built-in offline generator, or optional local Ollama. The visual rich-text editor also has Markdown source and reading modes. The workspace shows transcript and outline side by side in an adjustable split, with source-passage lookup.
- **Scripture:** detects references, links them to esv.org, and shows previews from the public-domain King James Version (labelled as such).
- **Library:** metadata, search, filters, sorting, recent sermons, statistics, rename, duplicate and delete (with a recovery archive), backup, and restore from Mac, Windows or web archives.
- **Exports:** TXT, Markdown, RTF, DOCX and paginated PDF (transcript and outline), plus SRT and VTT subtitles. Also bulk export, printing and sharing. Existing files are never overwritten silently.

## Project layout
- `Sources/SermonCore` contains the platform-independent logic: archive format, persistence, Markdown, Scripture, subtitles, exports, outline generation and settings. It has no UI dependencies.
- `Sources/SermonStudio` contains the SwiftUI and AppKit app.
- `Tests/SermonCoreTests` contains the XCTest suite, plus Windows, web and legacy-Mac archive fixtures under `Fixtures/`.

## Build and test
On an Apple Silicon Mac with Xcode or the command line tools installed:

```
cd macos && swift test        # run the automated tests
bash macos/build-local.sh     # from the repository root: build, package, ad-hoc sign, create DMG and ZIP
```

The build log is written to `macos/dist/build.log`. Codemagic and `release.sh` run `swift test` first, and a failing test stops the release.

## Data locations
- Library: `~/Library/Application Support/SermonStudio/archive.json`. A `.session-backup.json` copy is taken before the first save of each session. Unreadable files are preserved as `.unreadable-…`, and deletions write a `.recovery-…json` archive first.
- Whisper models: `~/Documents/huggingface/models/argmaxinc/whisperkit-coreml/`.
- Settings: the standard preferences for `com.sermonstudio.mac`. These survive updates.

## Updates
Sparkle 2.7.1 with an EdDSA-signed appcast at `https://jrcrosby91-tech.github.io/Sermon-Studio/appcast.xml`. Updates install only with your approval. Builds are ad-hoc signed and are not notarized. Keep the Sparkle private key out of the repository.
