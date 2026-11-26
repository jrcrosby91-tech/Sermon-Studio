# Sermon Studio for Windows — native WinUI 3 prototype

This is the first **Windows-native** implementation, using C#/.NET 10 and Windows App SDK WinUI 3.

Current prototype: Windows navigation, split transcript/outline editor, original offline extractive outline generation, local JSON sermon storage, and light/dark-aware accent resources.

**Not yet implemented:** Whisper transcription, Ollama requests, loading/selecting saved library entries, document exports, update installer, automatic outline saving, and feature parity with macOS. Navigation entries are scaffolding and do not yet switch to fully implemented screens.

## Build (Windows 11)

Install Visual Studio 2022 with Windows App SDK / WinUI workload and the .NET 10 SDK. Open `windows/SermonStudio/SermonStudio.csproj` and build for x64. Windows App SDK runtime prerequisites apply. This has not yet been compiled in a Windows CI runner.

Do not distribute this prototype as a completed Windows release.
