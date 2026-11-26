# Sermon Studio for Windows — native WinUI 3

Native Windows 10/11 desktop project using C#/.NET 10 and Windows App SDK WinUI 3.

## Current development functionality
- Native navigation shell and split transcript/outline editor
- Original transcript-based outline generator, completely offline
- Optional local Ollama outline generation at 127.0.0.1:11434 (requires separate Ollama installation and model download)
- Sermon library saved as local JSON, including loading existing entries
- Automatic saving of transcript/outline edits for previously saved sermons (debounced)
- Markdown outline export to Documents/Sermon Studio
- Light/dark-aware accent resources

## Remaining for macOS feature parity
- Whisper-based offline audio transcription, recording import, model selection, progress and cancellation
- Full transcript editing tools (cleaning, find/replace, compare, timestamps, audio playback)
- Scripture detection and linked references
- DOCX/PDF/RTF/SRT/VTT and full transcript exports
- Resizable workspace panels and finished dashboard/library screens
- Archive backup/restore and cross-platform data migration
- Automatic updates, code signing and Windows installer
- Full visual polish and accessibility testing

## Builds
GitHub Actions: `.github/workflows/windows-build.yml`. A passing run publishes a portable Windows x64 ZIP with `SermonStudio.exe` and supporting files. The Windows workflow compiles the app; the latest feature changes have not yet been verified by a successful run.

**Do not distribute as a complete Windows release.**
