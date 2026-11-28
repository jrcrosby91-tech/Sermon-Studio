# Sermon Studio for macOS — Stabilization Report

Branch: `macos/parity-pass` · Base: `main` @ `32ca991` · Prepared version: 3.3.0 (build 9) · **Not published**

## How things were verified

| Evidence | Result |
|---|---|
| **GitHub Actions `macos-15-intel`** (macOS 15.7, Swift 6.1.2), job *test* | Whole package compiled (`swift build --build-tests`: core, app, tests, WhisperKit, Sparkle) with **no compiler warnings in the app or core code**. **46 XCTest cases, 0 failures.** Startup smoke test: the debug app was launched against a seeded Windows `sermons.json` and was still running after 25 s. The real `AppModel` reported `library=2 blocked=false`, with the Windows titles and the speaker field. The archive file was byte-identical after launch. |
| **GitHub Actions `macos-15-intel`**, job *package-check* (run 45820611512, commit `f9b701d`) | Release arm64 bundle cross-built with `build-local.sh`. **Verified:** arm64-only binary; `SermonStudio.icns` present and `CFBundleIconFile` set; `Sparkle.framework` embedded; `@executable_path/../Frameworks` rpath; `codesign --verify --strict` reports "valid on disk" and "satisfies its Designated Requirement"; version 3.3.0 / build 9; `com.sermonstudio.mac`; unchanged Sparkle feed URL and public key; automatic installation off. DMG and ZIP created. |
| **GitHub Actions `macos-15` (Apple Silicon)**, job *package* | Queued repeatedly and was not started. GitHub reported "jobs targeting macOS arm64 runners may experience longer queue times" and "not acquired by Runner … even after multiple attempts". **Launch of the arm64 release bundle is therefore unverified.** |
| **Not verified** | Interactive UI behaviour, real transcription, model downloads, Ollama, playback, printing, VoiceOver and Sparkle update installation. These need a Mac; see `MACOS_TEST_CHECKLIST.md`. |
| **Codemagic** | Not triggered (no API access from this environment). The configuration now runs `swift test` before building in both workflows. |

## 1. Bugs discovered, by severity

### Critical
| # | Bug | Fixed |
|---|---|---|
| C1 | **An unreadable or undecodable archive was silently replaced.** The library was loaded with `try? JSONDecoder().decode`; on any failure it became empty, and the next save wrote a one-sermon archive over the user's entire library. | ✅ |
| C2 | **Windows and web archives could not be imported, and metadata was destroyed.** The strict `Codable` model rejected PascalCase keys, string segment IDs and null text, so one bad record failed the whole import. It also had no metadata or unknown-field support, so the first save of an imported library permanently dropped speaker, scripture, series, collection, tags, favourites, dates, SRT/VTT and web `chunks`. | ✅ |
| C3 | **Transcript edits were never saved automatically.** Only outlines of library sermons autosaved. Opening another sermon, choosing a recording or quitting discarded transcript and title edits without warning. | ✅ |
| C4 | **Choosing a recording mixed two sermons.** It reset the selected ID but kept the previous sermon's transcript, outline and timestamps in the editor, so the next save created a new sermon containing another sermon's content. | ✅ |
| C5 | **Two windows could overwrite each other's library.** With `WindowGroup`, ⌘N opened a second window with its own copy of the library; saving in either replaced the other's changes on disk. | ✅ (single `Window` scene, one shared model) |

### High
| # | Bug | Fixed |
|---|---|---|
| H1 | A delayed Ollama result was written into whichever sermon was open when it finished, then autosaved. There was no check for a sermon switch or for later edits. | ✅ |
| H2 | PDF export rendered into a fixed 540×800 view, so long outlines were clipped to one page. | ✅ |
| H3 | Quitting within 650 ms of an outline edit lost it, and there was no quit protection for unsaved or failed work. | ✅ |
| H4 | A failed save left the in-memory library different from the disk, with no rollback and only a status message. | ✅ |
| H5 | Transcription loaded the whole recording into memory (about 460 MB of samples for 2 hours, plus the model). Progress was a spinner, cancellation was not honoured by the engine, and partial work was lost. | ✅ |
| H6 | The GitHub *Build native macOS preview* artifact did not embed `Sparkle.framework` or the icon, so the app it produced could not launch. | ✅ (uses `build-local.sh`) |
| H7 | Releases had no automated tests at all. | ✅ |

### Medium
- **M1. Sermon dates could shift by a day.** They were saved as the UTC date, so a date picked in the evening in the Americas became the next day.
- **M2. Exports could overwrite files.** Without a default folder, file names were only sanitised for `/` and `:`. The save panel does confirm before replacing.
- **M3. RTF and DOCX dropped formatting.** Outline formatting was lost, and DOCX depended on running `/usr/bin/zip`.
- **M4. Scripture detection was limited.** No abbreviations or roman numerals, no chapter validation, it searched only the active transcript, and there was no preview.
- **M5. Model management was hidden.** Downloads happened invisibly inside the first transcription, with no progress, cancel, installed status or deletion.
- **M6. Biblical vocabulary did nothing.** It was saved but never passed to Whisper.
- **M7. Stored settings were never validated.** Bad values could reach the UI.
- **M8. Library search was narrow.** It covered only title, date and the original transcript.
- **M9. Opening a sermon did not stop the previous recording** before releasing the player.

### Low
- **L1.** No keyboard shortcuts or menu commands beyond the defaults.
- **L2.** No VoiceOver labels for media controls.
- **L3.** Increase-contrast was not considered.
- **L4.** No cleanup of temporary conversion files.

## 2. Root causes
- **No separation between data and UI.** All logic lived in one SwiftUI view's `@State`, so there was no single owner of the library and no testable save, load or restore path.
- **Optimistic error handling.** `try?` around loading, and status-only handling of save errors, turned failures into data loss.
- **A schema written for one platform.** The synthesized `Codable` model matched only the macOS archive, so other platforms' fields and capitalisation were rejected or silently discarded.
- **Edits not tied to persistence.** Autosave covered only the outline, and navigation never flushed or confirmed pending work.
- **No identity check for asynchronous work.** Generation and transcription results were applied to whatever sermon was current when they finished.

## 3. Corrections implemented
- **New `SermonCore` target** (pure Foundation/Core Text, unit-tested):
  - Lenient, case-insensitive record decoding that preserves unknown fields.
  - Strict archive validation.
  - `AtomicFile`: temp file, then `fsync`, then `rename`.
  - `LibraryStore`: unreadable files preserved with writes blocked; a session backup; transactions that restore the previous state on failure; recovery archives.
  - Restore planning that never overwrites existing sermons and offers explicit "import as copies".
  - Validated `SettingsStore` on the existing UserDefaults keys.
  - Ports of the Windows Markdown, Scripture, subtitle and export-naming rules.
  - Dependency-free DOCX (own ZIP writer, with hyperlinks) and RTF.
  - Paginated Core Text PDF.
  - Original outline generator.
  - Local-only Ollama client with proxies disabled, model-name validation and timeouts.
  - `WorkspaceGuard` revision tickets.
- **`AppModel`:** a single owner of the library and the open sermon.
  - Autosave 0.9 s after edits for library sermons.
  - Every path that leaves the open sermon (open, new, delete, restore, quit) flushes the visual editor, saves, and stops if the save fails.
  - Unsaved new sermons get Save / Discard / Cancel prompts.
  - `applicationShouldTerminate` offers a recovery copy when a save has failed.
  - Asynchronous results are checked against the ticket captured when they started.
  - The recording is unloaded whenever the open sermon changes.
- **Transcription:**
  - 10-minute sections with WhisperKit's chunked audio loader, so memory stays bounded.
  - Progress per Whisper window.
  - Cancellation through WhisperKit's early-stop callback, offering to keep the partial transcript.
  - Vocabulary prompt tokens.
  - AVFoundation conversion for containers WhisperKit can't read directly, and clear errors for undecodable files.
  - Stale temporary files cleaned at startup.
- **Interface:**
  - NSTextView editors with undo, spell check and find highlighting.
  - Debounced Scripture and word-count analysis off the main thread.
  - Visual-editor conversion debounced (0.35 s) and published outside SwiftUI view updates.
- **Build:**
  - `swift test` gates Codemagic (both workflows) and `release.sh`.
  - The GitHub workflow compiles, tests, smoke-launches and verifies the bundle.
  - Version bumped to 3.3.0 (9), after the last published 3.2.0 (8).

## 4. Files modified
- **New:**
  - Core: `macos/Sources/SermonCore/{SermonRecord,Archive,Settings,LibraryTools,OutlineMarkdown,Scripture,Subtitles,Exports,PDFExport,Outlines}.swift`
  - App: `macos/Sources/SermonStudio/{AppModel,Audio,Transcription,TextEditors,Views,SettingsView,ExportService,Theme}.swift`
  - Tests: `macos/Tests/SermonCoreTests/{ArchiveTests,ContentTests}.swift` and 4 fixtures
  - Reports: `MACOS_PARITY_REPORT.md`, `MACOS_STABILIZATION_REPORT.md`, `MACOS_TEST_CHECKLIST.md`
- **Rewritten:** `macos/Sources/SermonStudio/Studio.swift`
- **Modified:** `macos/Package.swift`, `macos/Info.plist`, `macos/README.md`, `macos/build-local.sh`, `macos/release.sh`, `codemagic.yaml`, `.github/workflows/macos.yml`
- **Unchanged:** Windows and web code, `appcast.xml`, `prepare-sparkle.sh`, `publish-codemagic.sh`, `macos-release.yml`, signing and Sparkle keys, bundle identifier and data locations.

## 5. Security and data-integrity findings
- An unreadable archive can no longer be replaced, and restore is the only way to start over. It keeps the preserved copy.
- Every save is atomic, flushed to disk, and rolled back in memory if it fails. Deletion writes a recovery archive first, and the first save of each session keeps a backup of the previous library.
- **Restore never overwrites.** Conflicting sermons are skipped or imported with new identifiers only after explicit choice. Duplicate identifiers inside a backup are rejected.
- **Unknown fields are preserved** in sermons and segments, so data written by Windows, the web app or a future version survives a round trip through the Mac.
  - Note: Windows does not preserve unknown fields, so Mac/web-only data is dropped if a library passes through Windows.
- **Ollama:** connections go only to `127.0.0.1:11434` through an ephemeral session with proxies disabled. Model names are validated, and cloud model names are rejected.
- **Scripture previews** send only the reference to `bible-api.com` (KJV, public domain, labelled as such). Links open esv.org. No ESV text is bundled, scraped or required. A future ESV API key should go in the Keychain; the single integration point is `ScriptureReferences.link/previewURL`.
- **No new entitlements, permissions or background services.** The export folder is stored with a security-scoped bookmark (and the path as a fallback).
- **Logging:** the app writes no sermon text to logs. The CI smoke-test line prints titles only, and only when `SERMON_STUDIO_SMOKE_TEST=1`.
- **Unchanged:** no secrets are in the repository, Sparkle EdDSA verification is unchanged, and automatic installation stays disabled.
- **Remaining concerns:**
  - Builds are ad-hoc signed, not Developer ID-signed or notarized (as before), so Gatekeeper warns on first launch.
  - The library is a single JSON file; very large libraries make each autosave slower.
  - WhisperKit's model and tokenizer downloads come from Hugging Face over HTTPS. Integrity is checked structurally (all Core ML components present and non-empty), not by checksum.

## 6. Automated test results
- **Local:** no Swift toolchain is available in the review environment, so nothing ran locally.
- **CI (`macos-15-intel`, run 45820059554, commit `abafeb9`):** `Executed 46 tests, with 0 failures`.
  - Archive compatibility: Windows PascalCase with .NET dates and default dates; Windows Mac-export; web `chunks` and unknown fields; legacy macOS 3.2; output readable by old Mac versions and Windows; lossless round trip; strict parsing; duplicate and missing IDs; lenient types.
  - Restore: never overwrites, rejects invalid backups, accepts every platform.
  - Persistence: atomic replace, failed write leaves the destination intact, stale-temp cleanup, unreadable archive preserved and writes blocked, session backup and restart reload, exact rollback, recovery archive.
  - Settings: defaults, restart persistence, legacy and invalid values, model integrity check.
  - Markdown inline, block and visual round trips; UTF-16 offsets; line-level merge.
  - Scripture detection, ranges, links and previews.
  - Subtitle parsing and writing.
  - Export filename sanitisation and no-overwrite naming; DOCX validated with `unzip` and `XMLDocument`; ZIP CRC; RTF; plain text; **1,500-line PDF paginated across many pages with first and last lines present**; empty PDF.
  - Original outline generator; Ollama chunking and prompts; library filters, sorts and statistics; transcript tools; asynchronous isolation.
- **Gating:** a failing test fails the GitHub job, the Codemagic workflows, and `release.sh` before anything is built or published. This was confirmed in CI: the first test run failed on a real defect (namespaced Ollama model names), which was then fixed.

## 7. Compilation results
- Full package compiles with Swift 6.1.2 (Swift 5 language mode, tools-version 5.9) on macOS 15.7. There are zero compiler warnings in `SermonCore`, `SermonStudio` and the tests; dependency warnings were not counted.
- One type-checker timeout in an early commit was fixed by splitting a SwiftUI view (commit `2aed720`).
- The arm64 release bundle was cross-built on Intel and its structure, signature and metadata verified (run 45820611512). It has not been built or launched on Apple Silicon by this pass.

## 8. Known limitations
See also `MACOS_PARITY_REPORT.md` §6.
- No hands-on UI verification yet.
- The arm64 release bundle has not been launched in CI.
- No XCUITest coverage.
- The first use of each Whisper model needs internet to fetch its tokenizer.
- PDFs have no outline bookmarks.
- The recording is not remembered with the sermon.
- No Intel build.

## 9. Commit identifiers
| Commit | Summary |
|---|---|
| `bb3b091` | Add SermonCore: cross-platform archive, persistence and content logic with XCTest suite |
| `09b13e2` | Rebuild the macOS app on SermonCore with Windows feature parity and safe data handling |
| `9d11475` | Run macOS compile and tests on an Intel runner; keep Apple Silicon packaging separate |
| `2aed720` | Split New Sermon view expressions so the Swift type checker completes |
| `b7710ed` | Allow namespaced Ollama model names; log every macOS test result in CI |
| `ae0b4c4` | Keep long transcripts responsive and avoid state changes during view updates |
| `68333853` | Add a macOS startup smoke test that also checks the archive is not rewritten on launch |
| `abafeb9` | Report loaded library summary in the CI startup smoke test |
| `f9b701d` | Verify the arm64 app bundle in CI on Intel runners when Apple Silicon capacity is unavailable |
| *(this commit)* | Add macOS parity report, stabilization report and acceptance checklist |

## 10. Release-readiness assessment
**Ready for a TestFlight-style internal test build after the checklist, not for publishing to the Sparkle feed yet.**

- **In place:** the code compiles cleanly; the core logic is covered by 46 passing tests; the app starts and loads a Windows library without modifying it; the critical data-loss paths (C1–C5) are fixed with code-level protection.
- **Blocking a stable 3.3.0 release:**
  1. Run `MACOS_TEST_CHECKLIST.md` sections A–F and I on an Apple Silicon Mac. Interactive behaviour has not been exercised by hand.
  2. Build and launch the arm64 bundle (Codemagic *macOS test build* or `build-local.sh`).
  3. Test a Sparkle update from 3.2.0 to 3.3.0 on a Mac that holds a real library.
- **Do not trigger the Codemagic *macos-release* workflow until those pass.** Doing so publishes 3.3.0 to every installed copy through the appcast.
