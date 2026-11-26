# Sermon Studio — Windows Stabilization Report (Pass 3)

Branch: `stabilization/windows-pass-3` · Base: `main` @ `56f60ed` · Pull request: #1 (draft)
Date: 2026-11-26

This pass independently reviewed the Windows (WinUI 3, .NET 10) application, verified the claims of the two previous stabilization passes, corrected defects, extracted testable logic and expanded the regression suite. macOS and web code were not changed.

## How things were verified

| Evidence | What it covers |
|---|---|
| **Local execution (Linux, .NET SDK 10.0.115)** | `dotnet run --project windows/ArchiveTests/ArchiveTests.csproj -c Release`: **202 checks passed, 0 failed**. A deliberately failing check was injected into a copy of the suite and the runner exited with code 1. Performance measurements of the portable code (below). |
| **GitHub Actions on `windows-2022` (CI)** | Runs 45762586213, 45762992789 and 45763042327 all succeeded. The last one built the branch head `e52588a`: WinUI app compiled and published (`dotnet publish` with nullable warnings CS8600–CS8625 promoted to errors, **0 compiler warnings**), `SermonStudio.exe` and `.pri` present, **202 checks passed** on Windows, Whisper and FFmpeg downloaded and their SHA-256 matched the published digests, portable ZIP artifact produced (236.6 MB). |
| **Not verified (no Windows desktop available)** | Anything that needs the running UI: dialogs, closing behaviour, rich-text editor, playback, waveform, transcription with real recordings, model download, Ollama, PDF rendering output, display scaling, high contrast and screen readers. These are listed in `WINDOWS_TEST_CHECKLIST.md`. |

NuGet is not reachable from the review environment, so the WinUI project itself could only be compiled in CI. A syntax-level compile of all sources was also run locally (no syntax errors; only the expected missing-WinUI-type errors).

## 1. Issues discovered

### Critical
| # | Issue | Status |
|---|---|---|
| C1 | **Closing the window could silently lose work.** The `Closed` handler saved only if the 900 ms autosave timer happened to be running. A save that failed at close was ignored, and a new sermon that had never been saved was discarded without a prompt. | Fixed |
| C2 | **Unsaved new sermons were discarded silently** by *New sermon* and by opening any sermon from the library or the recent-sermons list. | Fixed |
| C3 | **Line breaks from WinUI `TextBox` (reported as `\r`) were removed before splitting on `\n`** in the visual-editor loader, PDF, RTF and DOCX writers. When the text uses carriage returns, the whole outline collapses into one line in the visual editor, and the next autosave stores it that way. Exports would lose all paragraphs. *(Root cause from code reading and documented TextBox behaviour; runtime confirmation is checklist item E3.)* | Fixed |
| C4 | **Visual editor deleted heading text that starts with `#`** (`## #1 priority` was stored back as `## 1 priority`) because it used `TrimStart('#',' ')`. | Fixed |

### High
| # | Issue | Status |
|---|---|---|
| H1 | Exports overwrote existing files without warning: *Export outline* and *Export documents* reused the title as the file name, and PDF, backup and Mac-archive names collided when created within the same second. | Fixed |
| H2 | Opening a second `ContentDialog` throws in WinUI. Inside `async void` handlers (Compare, Timestamps, Rename/Duplicate/Delete, update prompt) that exception went unhandled and could crash the app. | Fixed |
| H3 | Closing during transcription left `whisper-cli`/`ffmpeg` running: the process was killed only in an `await` continuation that does not run once the window is gone. | Fixed |
| H4 | Ollama generation could not be cancelled, shared a 5-minute timeout, followed the system proxy (so a configured proxy could receive the transcript), and did not validate the model name. | Fixed |
| H5 | Whisper model downloads had no integrity check beyond the length, and the UI was updated for every 128 KB chunk (about 12,000 updates for a 1.6 GB model). | Fixed (SHA-256 when published by Hugging Face) |
| H6 | When the library was unreadable, writes were correctly blocked, but *Restore* was blocked too, leaving no way to recover inside the app. | Fixed |
| H7 | The visual editor re-serialised the entire rich document to Markdown on every keystroke (several COM calls per character), and every view switch rewrote untouched lines (for example `* ` bullets became `- `). | Fixed |
| H8 | The Ollama model and custom Whisper/FFmpeg/model paths were not saved, so they reset on every launch. Appearance and update preferences were written non-atomically, and failures were ignored. | Fixed |
| H9 | Library items were announced to screen readers as `SermonStudio.SermonRecord`. | Fixed |

### Medium
| # | Issue | Status |
|---|---|---|
| M1 | Atomic writes used a fixed `.tmp` name and no flush-to-disk, and kept no previous version. | Fixed: unique temp + write-through + `Flush(true)` + rename; once-per-session backup |
| M2 | Failed-save rollback replaced every record object with a deserialised copy. This invalidated objects held by the UI and serialised the entire library twice per save. | Fixed: in-place transactional rollback |
| M3 | *New sermon* reset the loaded recording **before** checking whether the pending save succeeded. | Fixed |
| M4 | Transcription silently replaced an existing transcript. Its result was saved only when the autosave timer fired. | Fixed: confirm, then save immediately |
| M5 | The built-in generator replaced an existing outline without asking. Programmatic replacement also clears TextBox undo. | Fixed: confirm |
| M6 | The visual editor ignored the editor text-size preference (fixed 17/19/23 pt) and detected headings by absolute size. | Fixed: sizes relative to the editor size |
| M7 | Scripture detection in the transcript covered 23 books, reported `1 John 3:16` as `John 3:16`, and missed Samuel, Kings, Daniel, Timothy, Peter and others. | Fixed |
| M8 | The Mac-compatible archive could not be decoded by the macOS app when a segment id was not a UUID (Swift decodes `SermonSegment.id` as `UUID`). Non-finite timestamps made the export throw. | Fixed |
| M9 | CI downloaded FFmpeg from a rolling URL, and whisper.cpp, without any checksum verification. | Fixed |
| M10 | Stale temporary audio (about 230 MB for a 2-hour WAV) remained in `%TEMP%` after a crash. | Fixed: cleaned at startup |
| M11 | PDF layout ran on the UI thread, which freezes the window for long transcripts. | Fixed: background thread |
| M12 | The single-file library is rewritten on every autosave (see the performance section). | **Open** — architectural |

### Low
L1. DOCX export threw on lone UTF-16 surrogates. L2. File names did not handle reserved device names (`CON`, `LPT1`), trailing dots or spaces, control characters or long titles. L3. `startup.log` grew without limit. L4. Library date filters used culture-dependent parsing. L5. The update installer used `First()` (which throws if the asset is missing) and had no package-size cap. L6. RTF output dropped all outline formatting. L7. The waveform used 8 kHz PCM where 4 kHz is enough. *All fixed.*

## 2. Root causes of significant bugs

- **C1/C2 (silent loss on close, New or switch):** The save logic assumed every change passes through the autosave timer. Close, New and library selection only flushed that timer. Two cases had no code path at all: a new sermon that is never autosaved (`_selectedId == null`), and a save that had already failed.
- **C3/C4 (formatting and content loss):** Markdown handling was copied into four places (visual loader, reading view, DOCX, PDF), each with its own regex and trimming rules. Each assumed `\n` line endings and used `Trim('*')` / `TrimStart('#')`, which strip content characters as well as markers.
- **H2 (crashes):** WinUI permits one `ContentDialog` at a time, and `ShowAsync` throws otherwise. In `async void` handlers this becomes an unhandled exception.
- **H3 (orphaned engines):** Cancellation relied on `await` continuations on the UI thread, which never run during process shutdown.
- **H7 (editor work and rewrites):** The rich-to-Markdown conversion was the only sync mechanism, so it had to run on every keystroke. Converting rich text to Markdown is lossy for Markdown spellings the visual model cannot represent.
- **M2 (rollback):** Rollback was implemented by re-deserialising a full snapshot, rather than restoring the affected records in place.

## 3. Code changes implemented

**Portable, tested core (`windows/SermonStudio/Core/`)** — compiled into both the app and the test project from the same files:
- `AtomicFile` — crash-safe replace, stale temp cleanup, stale temp-audio cleanup.
- `LibraryFile` — strict load with preservation of unreadable files, once-per-session backup (`sermons.json.session-backup.json`), and in-place transactional rollback.
- `PreferenceStore` — validated, atomic preferences (existing file names kept for upgrade compatibility), plus Ollama model and engine-path persistence.
- `ExportPaths` — Windows-safe names and no-overwrite reservation (`Title (2).md`, …) with one shared name per multi-file export.
- `OutlineMarkdown` — one formatting model for the visual editor, reading view and exports. Markers must be flanked by non-whitespace, `\*` escapes a literal asterisk, all line-ending styles are handled, and `MergeVisualEdit` keeps the original Markdown of every line the user did not edit.
- `ScriptureReferences` — all 66 books, numbered books (1/2/3, I/II/III, First/Second/Third), common abbreviations, verse and cross-chapter ranges, chapter-count validation, and public-domain KJV links and previews.
- `Subtitles` — tolerant SRT/WebVTT parser (CRLF, short timestamps, cue settings, malformed cues) and exact-millisecond writer.
- `UpdateRules`, `DownloadIntegrity` — build comparison, URL/digest/size checks, zip-entry validation (absolute paths, `..`, drive letters, alternate data streams), and Hugging Face SHA-256 parsing.
- `DocumentWriters` — DOCX and RTF with headings, bold, italic, bold-italic, bullets, numbered lists, indented scripture quotations and full Unicode.
- `WorkspaceGuard` — revision tickets so delayed results are never applied to another sermon.
- `ArchiveRules` — strict library parsing, restore planning (never overwrites existing sermons), Mac-compatible serialisation, and an accessible `ToString()`.

**App integration:**
- Close protection on `AppWindow.Closing`: save, then prompt with *Save recovery copy*, *Save and close*, *Close without saving* or *Keep working*.
- Draft prompts on New and library switching.
- `Save()` now returns success, and navigation is blocked after a failed save until the user retries.
- Emergency recovery from the unhandled-exception handler.
- Debounced visual editor (350 ms), flushed before every save, switch, export, generation and view change.
- Ollama: cancel button, 20-minute limit, loopback-only client without a proxy.
- Model download: SHA-256 and size verification, flush to disk, progress throttled to 4 updates per second.
- Updates: streaming hash, size cap, safe extraction, and the open sermon is saved before relaunch.
- Exports: shared unique names, formatted RTF, and a background PDF build.
- Every `ContentDialog` goes through a safe helper.
- Engine processes are killed on cancel and at shutdown.
- Log rotation.

**CI:** whisper.cpp is verified against both GitHub's published digest and a pinned SHA-256. FFmpeg is verified against gyan.dev's published `.sha256`. Nullable warnings fail the publish. The test failure message is clearer. The release job still `needs:` the test job.

## 4. Files modified

`.github/workflows/windows-build.yml`, `windows/.gitignore` (new), `windows/ArchiveTests/ArchiveTests.csproj`, `windows/ArchiveTests/Program.cs`, `windows/SermonStudio/App.xaml.cs`, `ArchiveRules.cs`, `Compatibility.cs`, `Interface.cs`, `MainWindow.xaml.cs`, `Parity.cs`, `Tools.cs`, and new `Core/AtomicFile.cs`, `Core/DocumentWriters.cs`, `Core/DownloadIntegrity.cs`, `Core/ExportPaths.cs`, `Core/LibraryFile.cs`, `Core/OutlineMarkdown.cs`, `Core/PreferenceStore.cs`, `Core/ScriptureReferences.cs`, `Core/Subtitles.cs`, `Core/UpdateRules.cs`, `Core/WorkspaceGuard.cs`, plus this report and `WINDOWS_TEST_CHECKLIST.md`. The `.csproj`, XAML files, `app.manifest` and the PRI packaging fix are unchanged.

## 5. Automated tests added or improved

The suite grew from 11 checks to **202 checks in 17 sections**. Every section runs even if an earlier one fails, all failures are listed, and the process exits with code 1 on any failure. The project links production source files only and sets `TreatWarningsAsErrors`.

| Requested area | Section(s) |
|---|---|
| Settings persistence | Settings persistence (simulated restart, legacy files with CRLF/BOM, invalid values, cloud-model rejection, path traversal, failed writes, custom engine paths) |
| Atomic archive writes | Atomic writes |
| Failed-save recovery | Library load, session backup and failed-save recovery |
| Backup merging | Restore and backup merging |
| Duplicate identifiers | Duplicate identifiers; Restore and backup merging |
| Legacy archive migration | Library parsing and legacy migration; Mac-compatible archive |
| Markdown round trips | Markdown inline formatting; Markdown block structure; Visual editor round trips |
| Scripture parsing | Scripture reference parsing |
| Subtitle parsing | Subtitle parsing |
| Export filename sanitisation | Export filename sanitization; Document exports (DOCX/RTF content, long and empty documents) |
| Update version comparison | Update version comparison and package safety (zip-slip cases) |
| Invalid or malformed input | Present in most sections (truncated JSON, non-array roots, malformed subtitles, bad digests, bad paths) |
| Async isolation | Asynchronous operation isolation |

## 6. Actual test execution results

- Local (Linux): `202 checks passed, 0 failed.` A deliberately failing check makes the runner exit with code 1 (verified).
- CI Windows (run 45763042327, commit `e52588a`): `202 checks passed, 0 failed.`
- The original 11 checks remain in the suite, rewritten only to use the shared runner, and pass.

## 7. Compilation and publishing results

- CI `dotnet publish … -warnaserror:CS8600,…,CS8625 --runtime win-x64 --self-contained true …` succeeded with no C# warnings on all three runs of this branch.
- `SermonStudio.exe` and `SermonStudio.pri` were present in the published output, so the PRI packaging fix is preserved.
- Both bundled engines were found (`whisper-cli.exe`, `ffmpeg.exe`) and their checksums verified:
  - whisper-bin-x64.zip `0d2eca29…8156a9`
  - ffmpeg-release-essentials.zip `b9bbbb3a…d658fd`
- Artifact `Sermon-Studio-Windows-x64-portable` (236.6 MB) uploaded. No release was published: pull-request runs skip the release job.

## 8. Remaining warnings and known limitations

- **Library scalability (M12, measured):** in synthetic tests (about 140 words a minute, Whisper cues every 4 s), one 120-minute sermon is 744 KB of JSON because the transcript is stored in `Original`, `Srt`, `Vtt` and `Segments`. A 200-sermon library is 84.5 MB; saving took 0.63 s and loading 1.1 s on the Linux review container. Each autosave rewrites the whole file on the UI thread. This is acceptable for small libraries and a visible pause for large ones. Recommended next step: derive SRT/VTT from `Segments`, or store one file per sermon. This needs coordinated macOS/web format work, so it was not changed here.
- Library search scans every transcript on each keystroke (fine for small libraries).
- *Replace all*, the built-in generator and Ollama results set text programmatically, which clears TextBox undo history (the user is now asked first for outline replacement).
- The visual editor cannot represent italic text immediately followed by bold text with no character in between. Such runs are stored with escaped markers rather than lost.
- Two accent buttons (*Generate outline*, *Transcribe*) use fixed gold/navy colours that do not follow high-contrast themes.
- GitHub Actions warns that `actions/*@v4` run on Node 20 (deprecated). Not upgraded, so as not to risk CI with untested major versions.
- `System.Security.Cryptography.Pkcs` 8.0.2 is referenced explicitly but not used by app code. It was left in place in case a dependency needs it.

## 9. Features requiring manual Windows testing

See `WINDOWS_TEST_CHECKLIST.md`. Highest priority:
1. Close prompts (unsaved draft, failed save, running transcription).
2. Visual ⇄ Markdown ⇄ Reading switching, including TextBox line-ending behaviour (checklist E3).
3. Model download showing "verified (SHA-256)".
4. Transcription of 30/60/120-minute recordings.
5. Exports opening correctly in Word and a PDF reader.
6. Update from an older test build.
7. Display scaling, high contrast and Narrator.

## 10. Remaining security and data-integrity concerns

- **Model checksum source:** the expected SHA-256 comes from Hugging Face's `X-Linked-Etag` response header. If that header is missing, the download is accepted after a size check and the status says "checksum unavailable". If Hugging Face ever changes the header's meaning, downloads would fail closed. Verify this once on Windows (checklist D3).
- **Updates are unsigned.** The package digest comes from the same GitHub release as the package, so a compromised repository or account could publish a malicious build that passes verification. Recommended: Authenticode signing and a pinned signing key.
- **FFmpeg licensing:** the gyan.dev "essentials" build is GPL-licensed. Redistributing it requires including its licence and offering the corresponding source. The bundle includes a source note, but the licence text should be copied from the archive into `engines/ffmpeg`, and a source offer should be confirmed. whisper.cpp (MIT) licence is included.
- **Scripture previews** contact `bible-api.com` (KJV, public domain) on hover, and links open BibleGateway KJV. Only the reference is sent, never sermon text. No ESV text is bundled, scraped or required. `ScriptureReferences.PassageLink`/`PreviewUrl` are the single place to add a licensed ESV provider once the API application is approved.
- **Ollama** is contacted only on `127.0.0.1:11434`, without a proxy. Model names containing "cloud" are rejected, and setup starts `ollama serve` bound to loopback with `OLLAMA_NO_CLOUD=1`.
- **External processes** receive arguments through `ArgumentList` (no shell parsing), and audio paths are always absolute picker paths.
- **Logs** contain events and exception types only. Library-load errors include the JSON parser's location message, but no sermon text.
- **Old update folders** under `%LOCALAPPDATA%\SermonStudio\versions` are never removed.
- **Interoperability:** the Windows `sermons.json` (PascalCase) is not readable by the macOS app; use *Export Mac-compatible archive*. The Mac app drops Windows-only metadata (speaker, tags, series, collection, favourites) on import. Mac decoding was verified against the Swift model by schema reasoning and tests, not by running the macOS app.

## 11. Commit identifiers

| Commit | Summary |
|---|---|
| `d8d263b` | Ignore Windows build output folders |
| `9904e6c` | Extract portable archive, settings, export and formatting logic with regression tests |
| `a63a4bc` | Harden saving, shutdown, exports, settings, editor sync and updates in the Windows app |
| `f6d4dbf` | Verify bundled Whisper and FFmpeg checksums and enforce nullable warnings in CI |
| `db4746f` | Prevent dialog crashes, recover unsaved work on fatal errors and clean stale temp audio |
| `e52588a` | Stop Whisper and FFmpeg immediately on cancel or shutdown |
| *(this commit)* | Add stabilization report and Windows acceptance checklist |

## 12. Release-readiness assessment

**Ready to publish as the next Windows *test* prerelease once the manual checklist sections A–E pass on a real Windows PC. Not yet ready for general (non-test) release.**

- Compilation, packaging, bundled engines and the regression suite are green in CI. The highest-risk data-loss paths (close, New, switch, failed save, unreadable archive, export overwrite) now have code-level protection, and the portable parts have automated tests.
- What still blocks a general release: none of the UI behaviour has been exercised on Windows in this pass; updates are unsigned; the single-file library slows down as it grows; FFmpeg licence compliance needs confirming.
- Merging this branch into `main` will trigger the workflow's release job and publish a new `windows-test-*` prerelease. Merge when you want that build distributed.
