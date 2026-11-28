# Sermon Studio — macOS Feature Parity Report

Branch: `macos/parity-pass` · Base: `main` @ `32ca991` (includes Windows stabilization pass 3) · Version prepared: **3.3.0 (build 9)**, not published.

The reference was the current Windows source (`windows/SermonStudio`, including the stabilization changes merged in PR #1). The starting point was the macOS app as of `8cf2203`: a single 757-line SwiftUI file.

Legend: ✅ equivalent · 🟡 partial · ❌ missing · 🍎 macOS-specific alternative · 🔑 needs external approval or dependency

## 1. Initial parity matrix (before this pass)

| Area | Windows | macOS before | Status |
|---|---|---|---|
| Archive format | PascalCase library, case-insensitive reader, Mac-compatible export | camelCase only; strict decoder failed on Windows/web archives; dropped metadata and unknown fields | ❌ |
| Sermon metadata (speaker, scripture, series, collection, tags, favorite, edited/opened) | Yes | Title and date only | ❌ |
| Atomic saves, failed-save rollback, unreadable-archive protection | Yes | `.atomic` write only; unreadable archive was silently replaced | ❌ |
| Save before switch, New and quit | Yes, with prompts | Transcript edits never autosaved; switching or quitting discarded them | ❌ |
| Library search, filters, sort, recents, stats | Yes | Search over title/date/transcript only | 🟡 |
| Rename, duplicate, delete with recovery backup | Yes | None | ❌ |
| Backup / restore / merge | Yes, never overwrites | Import appended non-duplicates; no validation | 🟡 |
| Bulk and filtered export | Yes | None | ❌ |
| Offline transcription | whisper.cpp + FFmpeg | WhisperKit (native, Neural Engine) | 🍎 |
| Model selection, download progress, cancel, installed indicators | Yes | Picker only; download hidden inside first transcription | 🟡 |
| Transcription progress and cancel | Yes | Spinner; cancel not honoured by the engine | 🟡 |
| Playback: play/pause, ±15 s, seek, speed, elapsed/total | Yes | Play/pause only | 🟡 |
| Waveform, timestamp navigation, active highlight, follow | Yes | Timestamp buttons only | 🟡 |
| Original/cleaned, compare, clean | Yes | Yes | ✅ |
| Find previous/next, match case, counts, replace one/all | Yes | Replace all only | 🟡 |
| Word counts, font size, focus mode | Yes | Font size only | 🟡 |
| Original outline generator | Yes | Yes | ✅ |
| Ollama generator, per-sermon override, guided setup | Yes | Yes (no stale-result protection) | 🟡 |
| Visual rich-text outline editor, Markdown and reading modes | Yes | Plain `TextEditor` | ❌ |
| Split transcript/outline workspace with persistent proportion | Yes | `HSplitView`, not persisted | 🟡 |
| Source passage lookup | Yes | Yes (switches page) | 🟡 |
| Scripture detection (full canon, numbered books, abbreviations, ranges) | Yes | Full names, no abbreviations, ranges partial | 🟡 |
| Scripture links / previews | BibleGateway KJV + KJV preview | esv.org links, no preview | 🟡 |
| Exports TXT/MD/RTF/DOCX/PDF/SRT/VTT with formatting | Yes | All formats, but plain text only; PDF clipped to one page | 🟡 |
| No-overwrite export naming | Yes | Only with a default folder | 🟡 |
| Settings: appearance, A-size picker, models, generator, storage, updates | Yes | Partial, single page | 🟡 |
| Automatic updates | Custom GitHub updater | Sparkle (signed appcast) | 🍎 ✅ |
| Automated tests gating release | Yes | No tests | ❌ |

## 2. Missing features identified
The library, metadata, restore validation and the data-protection paths were missing or unsafe. Also missing:
- Model management.
- Playback controls and waveform.
- Find and replace.
- Visual outline editor.
- Formatted and paginated exports.
- Scripture abbreviations and previews.
- Settings organisation.
- A native settings window.
- Menu commands.
- Drag and drop.
- Tests.

## 3. Features implemented
- **`SermonCore` library** (no UI dependencies, fully unit-tested): a cross-platform archive model and the same formatting, scripture, subtitle, export-naming, library-query and settings rules as Windows.
- **Library management:**
  - Metadata editor.
  - Search across titles, metadata, transcripts and outlines.
  - Favourites, speaker, tag, series, collection and date-range filters, with suggestions.
  - Four sort orders, recently opened sermons, statistics and previews.
  - Rename, duplicate, multi-select delete with a recovery archive, and context menus.
  - Bulk export of all sermons or the filtered set.
  - Backup, and restore from Mac, Windows or web archives.
- **Transcript editor:**
  - Native `NSTextView` with undo and spell check.
  - Original and cleaned versions with comparison.
  - Find previous/next with match case, counts and highlighting; replace one or all.
  - Word count.
  - Scripture panel with KJV previews.
  - Timestamp list with low-confidence markers, active-segment highlighting and follow-playback.
- **Audio:**
  - Play/pause, ±15 s, scrubber, elapsed/total and speed (0.75–2×, pitch-preserving).
  - Interactive waveform (computed in bounded chunks).
  - Drag-and-drop import.
  - Unplayable files rejected with a clear message.
- **Transcription:**
  - Five WhisperKit models with installed and incomplete indicators.
  - Download with progress and cancellation; deletion.
  - Open the models folder.
  - Transcription in 10-minute sections, with progress and cancellation that offers to keep the partial transcript.
  - Biblical vocabulary passed to Whisper as prompt tokens (previously saved but unused).
  - Video and other containers converted with AVFoundation.
- **Outlines:**
  - Visual rich-text editor (headings, subheadings, bold, italic, bold-italic, bullets, numbering, scripture quotations, undo/redo).
  - Markdown source mode and formatted reading mode with Scripture links.
  - Lossless switching between modes.
  - Adjustable, persistent split workspace.
  - Source lookup that selects and scrolls to the passage.
  - Per-sermon generator override.
  - Cancellable, time-limited Ollama generation with stale-result protection.
  - Confirmation before replacing an outline.
- **Exports:**
  - Transcript: TXT, MD, RTF, DOCX, PDF, SRT and VTT. Outline: TXT, MD, RTF, DOCX and PDF.
  - Headings, emphasis, lists, quotations, Unicode and Scripture hyperlinks (DOCX, RTF, PDF).
  - Paginated PDF.
  - Native printing.
  - Share sheet and Show in Finder for the last export.
  - Default export folder saved with a security-scoped bookmark.
  - No silent overwrites.
- **Settings** (sidebar page and the standard ⌘, window):
  - Appearance (System, Light, Dark).
  - A-size font picker with live preview.
  - Model management and advanced transcription.
  - Default generator, Ollama model, setup and status.
  - Library location, export folder, backup and restore.
  - Utilities.
  - Version and build, plus Sparkle update controls.
- **Native macOS:**
  - Menu commands: ⌘N new sermon, ⌘O open recording, ⌘S save, ⌘P / ⇧⌘P print, ⇧⌘B backup, ⌘1–⌘6 pages, ⇧⌘F focus mode, ⇧⌘T transcribe, ⇧⌘G generate, ⌥Space play.
  - Sidebar toggle, native full screen, single-window scene, quit protection.
  - VoiceOver labels and adjustable actions for the waveform and divider.
  - Increase-contrast-aware primary buttons.

## 4. Existing features improved
- **Sermon dates** use the local calendar. Before, a sermon dated in the evening in the Americas could be saved as the next day.
- **Whisper models** downloaded by earlier versions are found in the same location.
- **Settings** keep the same UserDefaults keys, so preferences survive the update.
- **ESV website links** are preserved.
- **Sparkle** configuration, public key and appcast are unchanged.
- **The GitHub macOS workflow** now builds through `build-local.sh`. Its earlier artifact omitted `Sparkle.framework` and the icon, so that app could not launch.

## 5. Platform-specific alternatives
| Windows | macOS |
|---|---|
| whisper.cpp + FFmpeg executables | WhisperKit Core ML (Neural Engine), AVFoundation for decoding and conversion; no bundled executables |
| Custom updater with GitHub digest | Sparkle 2.7.1 with EdDSA-signed appcast |
| Files under `%LOCALAPPDATA%` | `~/Library/Application Support/SermonStudio`, UserDefaults |
| RichEditBox | NSTextView via `NSViewRepresentable` |
| ContentDialog | NSAlert, NSSavePanel/NSOpenPanel |
| PDFsharp/MigraDoc | Core Text (paginated) and NSPrintOperation |
| Hover passage tooltip | KJV preview popover from the Scripture panel |

## 6. Remaining limitations
- Licensed ESV previews 🔑 wait for API approval. The integration point is `ScriptureReferences.link/previewURL`; a future API key belongs in the Keychain, and no key is stored or required today.
- PDF Scripture links are clickable, but in-document heading bookmarks are not generated.
- Opening a sermon does not reopen its recording, because the audio path is not stored in the shared archive. This is the same as Windows.
- WhisperKit downloads its tokenizer on a model's first load, so the first transcription with a new model needs internet.
- No XCUITest UI automation: SwiftPM has no UI-test host. Instead a CI startup smoke test launches the real app against a Windows archive.
- No Intel build. The app has always been Apple Silicon-only.
- The library is one JSON file rewritten on each save (same design as Windows); very large libraries make saves slower.

## 7. Final parity matrix

| Area | Status |
|---|---|
| Archive compatibility (Windows, web, legacy Mac; unknown fields preserved) | ✅ (exceeds Windows, which drops unknown fields) |
| Metadata, library management, filters, sort, recents, stats | ✅ |
| Data protection (atomic, rollback, unreadable archive, switch/new/quit, recovery archives) | ✅ |
| Backup, restore, merge, bulk/filtered export, cross-platform archive | ✅ |
| Transcription, models, progress, cancel, vocabulary | 🍎 ✅ (adds partial-transcript recovery) |
| Playback, waveform, timestamps, follow mode | ✅ |
| Transcript editing tools | ✅ |
| Outline generators and editor modes | ✅ |
| Scripture detection, links, previews | ✅ (esv.org links; KJV previews clearly labelled) · 🔑 ESV text |
| Exports and printing | ✅ |
| Settings organisation and persistence | ✅ |
| Updates | 🍎 ✅ |
| Automated tests gating release | ✅ (46 XCTest cases; Codemagic and `release.sh` stop on failure) |
| Runtime UI behaviour | Compiled and launch-tested in CI; **interactive behaviour not yet verified by hand** (see `MACOS_TEST_CHECKLIST.md`) |
