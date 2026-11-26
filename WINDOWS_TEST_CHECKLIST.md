# Sermon Studio — Windows Manual Acceptance Checklist

Use the portable ZIP from the Windows workflow (Actions → *Sermon Studio Windows test build* → artifact `Sermon-Studio-Windows-x64-portable`). Extract the **whole** ZIP and run `SermonStudio.exe`.

**Before you start:** copy `%LOCALAPPDATA%\SermonStudio` somewhere safe so you can restore your real library afterwards.

**Prepare:**
- Recordings of about 30, 60 and 120 minutes (MP3 or M4A) and one short WAV.
- Word (or LibreOffice) and a PDF reader.
- Optional: Ollama installed.

Mark each item **Pass/Fail** and note the build tag shown under *Settings › About*.

## A. Startup, layout and settings persistence
- [ ] A1. The app starts without a crash. *New sermon* is the first sidebar item. The navy-and-gold design and serif headings are intact.
- [ ] A2. In Settings, choose offline model **small.en**, default generator **Ollama (local)**, Ollama model `llama3.2:3b` (or another installed model), appearance **Dark**, editor size **Large**, and turn on *Check for updates on startup*. Drag the transcript/outline divider. Close and reopen the app: **every** choice is kept. The model must not revert to `base.en`.
- [ ] A3. Under *Advanced engine paths*, enter an invalid path. A "File not found … not saved" message appears and the path is not kept after restart. Enter a valid custom `ffmpeg.exe` path: it is kept after restart. Clear the field: it falls back to the bundled engine.
- [ ] A4. Open `%LOCALAPPDATA%\SermonStudio\editor-size.txt` in Notepad, type `999`, save and start the app. It starts with the default size and no error.
- [ ] A5. Start a new sermon (generator shows the default), switch the outline generator to the other option, then start another new sermon. The default is selected again.

## B. Saving, switching and closing (data protection)
- [ ] B1. *New sermon* → type a title and some text, do **not** save, click *New sermon*. A prompt offers **Save sermon / Discard draft / Cancel**. Check that each option does what it says.
- [ ] B2. With an unsaved draft, click a sermon in the library. The same prompt appears; after *Save sermon* the draft is in the library and the clicked sermon opens.
- [ ] B3. Open a saved sermon, type, and within 1 second close the window with the **X**. Reopen: the last words are present.
- [ ] B4. Type in an unsaved new sermon and close with **X** (and once with **Alt+F4**). The close prompt appears. *Keep working* leaves the window open; *Save and close* saves the sermon.
- [ ] B5. Failed save: open a saved sermon, then in File Explorer make `%LOCALAPPDATA%\SermonStudio\sermons.json` read-only (Properties › Read-only). Type in the editor. The footer shows **Save failed** and the text stays in the editor. Clicking another sermon or *New* is refused with an explanation. Close the window: choose *Save recovery copy and close*, and a `Recovery-….json` appears in `Documents\Sermon Studio`. Clear Read-only, reopen the app, use *Restore library* with that file: the sermon comes back.
- [ ] B6. Unreadable library: close the app, open `sermons.json` in Notepad, delete its last 20 characters and save. Start the app. A message says the library could not be loaded and names the preserved copy (`sermons.json.unreadable-…`). Typing in a sermon does not overwrite `sermons.json` (check its modified time). *Restore library* with a backup file works and the unreadable copy remains.
- [ ] B7. After editing during a session, `sermons.json.session-backup.json` exists and contains the library as it was before the session.
- [ ] B8. Rename, Duplicate and Delete in the library. Delete creates `sermons.json.recovery-….json` first. Deleting the open sermon clears the editors.
- [ ] B9. Backup → Restore the same file: "No new sermons to restore". Restore a backup from another PC: only new sermons are added and existing sermons are unchanged.

## C. Audio and transcription
- [ ] C1. Import the 30-minute recording. Playback, ±15 s, speed 0.75–2.0×, the seek slider and waveform clicks work. The waveform appears within a few seconds.
- [ ] C2. Transcribe the 30-minute recording with the bundled engine. Progress shows FFmpeg preparation, then Whisper. When it finishes, the transcript and *Timestamp view* appear, and the status says **saved with timestamps** (for a saved sermon).
- [ ] C3. Transcribe again on the same sermon. A **Replace transcript?** confirmation appears.
- [ ] C4. Start transcribing the 120-minute file, then click *Cancel*. Task Manager shows no `whisper-cli.exe` or `ffmpeg.exe` within a few seconds.
- [ ] C5. Start a transcription and close the window → choose *Cancel work and close*. No `whisper-cli.exe` or `ffmpeg.exe` remains in Task Manager.
- [ ] C6. During transcription, switching sermons, *New sermon* and *Restore* are refused with a message.
- [ ] C7. In *Timestamp view*, with *Follow audio* on, the active segment highlights and scrolls during playback, and clicking a timestamp seeks there.
- [ ] C8. Transcribe the 60- and 120-minute recordings. Record the time taken and peak memory (Task Manager) here: ________. The UI stays responsive while typing in the outline.
- [ ] C9. After transcriptions finish, `%TEMP%` contains no `sermon-<32 hex>.wav` or `sermon-wave-….pcm` files.

## D. Model download
- [ ] D1. Choose a model that is not downloaded (for example `tiny.en`) → *Download model*. Progress shows "X of Y MB" and the window stays responsive.
- [ ] D2. Cancel a download partway. Status shows "canceled" and no `.part` file remains in the models folder.
- [ ] D3. Complete a download. Status says **"downloaded and verified (SHA-256)"**. If it says "checksum unavailable" or the download fails integrity, report it — Hugging Face's checksum header may have changed.

## E. Outline editing and formatting
- [ ] E1. Paste this into **Markdown** mode, switch to the visual editor, then to Reading view, then back to Markdown. The text is **identical** character for character:
  ```
  ## Introduction
  **Grace** is *unearned*, and ***both*** works.
  ### Point one
  > For God so loved the world — John 3:16
  * star bullet
  - dash bullet
  1. numbered
  ## #1 priority
  5 * 3 = 15
  ```
- [ ] E2. In the visual editor, edit only the "Point one" line, then switch to Markdown. Only that line changed (the `* star bullet` line is still `*`).
- [ ] E3. In Markdown mode, type three lines pressing Enter between them, switch to the visual editor: still three separate lines. Export to PDF and DOCX: three paragraphs.
- [ ] E4. Heading, Subheading, Normal, Bold, Italic, Bullets, Numbered and Scripture buttons work. Undo/Redo work. Ctrl+B and Ctrl+I work in the visual editor.
- [ ] E5. Type quickly in the visual editor for about 10 seconds, then immediately press Ctrl+S (or switch sermons). Nothing typed is lost after reopening.
- [ ] E6. Change the editor text size in Settings while an outline with headings is open. Headings stay headings and scale.
- [ ] E7. Find/Previous/Next/Replace/Replace all with and without *Match case*; the match count updates. *Find source* locates selected outline text in the transcript.
- [ ] E8. Focus mode hides the sidebar and toolbars; Esc exits.
- [ ] E9. Built-in generator with an existing outline asks **Replace outline?** first.
- [ ] E10. Ollama: *Set up / check Ollama* reaches "Ready". Generate, then click *Cancel generation* mid-way: the outline is unchanged. Generate again and switch sermons before it finishes: the result is discarded ("sermon changed"). Model name `x:cloud` is rejected.

## F. Scripture
- [ ] F1. The *Scripture references* page lists `1 John 4:8`, `2 Corinthians 5:17-21`, `Psalm 23` and `Jude 1:3` from text containing "1 John 4:8, 2 Cor 5:17-21, Ps 23, Jude 3", and does **not** list "John 4:8".
- [ ] F2. In Reading view, references are gold links. Hovering shows the KJV passage when online, and an "unavailable" message when offline. Clicking opens BibleGateway KJV. No ESV key is requested anywhere.

## G. Exports
- [ ] G1. *Export › Outline • Markdown* twice with the same title. The second file is `<Title>-Outline (2).md` and the first is unchanged.
- [ ] G2. *Word, RTF and transcript files* twice. The second set gets a new shared name and nothing is overwritten. The DOCX and RTF outlines open in Word with headings, bold, italic, bold-italic, bullets, numbering and an indented italic quotation.
- [ ] G3. PDF export of a 120-minute transcript completes without freezing the window. The PDF shows formatting and Unicode (é, ü, ✝, “quotes”).
- [ ] G4. A title of `CON: Faith/Hope?` exports successfully with a safe file name.
- [ ] G5. An empty sermon exports PDF/DOCX without errors.
- [ ] G6. SRT/VTT exports play in VLC alongside the recording with matching timings.
- [ ] G7. *Export all sermons* and *Export filtered sermons* create a new `Archive-…` folder with `sermons.json` and per-sermon files.
- [ ] G8. *Export Mac-compatible archive*, then import it with *Import web archive…* in the macOS app (if available). All sermons appear with transcripts, outlines and timestamps.

## H. Interface and accessibility
- [ ] H1. Resize the window from full width down to about 800 px wide. Toolbars wrap and nothing overlaps or gets cut off.
- [ ] H2. F11 toggles full screen.
- [ ] H3. Display scaling at 100%, 125%, 150% and 200% (Settings › Display): text is crisp and the layout is usable at each.
- [ ] H4. Light, Dark and System themes; switch the Windows theme while the app is open (System mode follows).
- [ ] H5. Windows high-contrast theme (Settings › Accessibility › Contrast themes): text is readable and no custom colours hide content. Note: *Generate outline* and *Transcribe* keep gold/navy colours (known limitation).
- [ ] H6. Keyboard only: Tab reaches every control in a sensible order. Ctrl+S, Ctrl+F, Ctrl+L, F11 and Esc work.
- [ ] H7. Narrator: library items are read as sermon titles (not "SermonStudio.SermonRecord"). Editor, font-size choices and navigation buttons have meaningful names.

## I. Updates
- [ ] I1. *Check now* on an older test build finds the newer `windows-test-*` build, and installs only after you approve.
- [ ] I2. After the update relaunches, the library, settings and downloaded models are all present.
- [ ] I3. With unplugged network, *Check now* fails gracefully with a message.

## J. Logs and privacy
- [ ] J1. `%LOCALAPPDATA%\SermonStudio\startup.log` contains no sermon text, titles or transcripts.
- [ ] J2. With Ollama selected, Resource Monitor › Network shows generation traffic only to 127.0.0.1.

**Result:** ______ of ______ items passed. Tester / date / build tag: ______________________
