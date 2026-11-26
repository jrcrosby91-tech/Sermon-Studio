# Sermon Studio for macOS — Manual Acceptance Checklist

Use a build of the `macos/parity-pass` branch, either from the Codemagic *macOS test build* artifact or from `bash macos/build-local.sh` on an Apple Silicon Mac.

**Before you start:** back up `~/Library/Application Support/SermonStudio/` (your library) so you can restore it afterwards.

**You will need:**
- A short WAV file, and recordings of about 30, 60 and 120 minutes (MP3 or M4A).
- One video file with sound (MOV or MP4).
- One file macOS cannot play (for example `.ogg`).
- A Windows `sermons.json` (or the Windows *Mac-compatible archive*) and a web-app backup.
- Word and Preview.

Record **Pass/Fail** and the version and build shown in Settings › Updates and about.

## A. Installation, startup and updates
- [ ] A1. The app opens from the DMG and Applications. Its icon (open Bible, steeple, cross and blue waveform) appears correctly in Finder, the Dock, Spotlight and ⌘-Tab, and is sharp on Retina.
- [ ] A2. First launch with an existing 3.2.0 library: all sermons, transcripts, outlines and timestamps appear. Your settings are kept: model, font size, appearance, Ollama model and export folder.
- [ ] A3. *New Sermon* is the first sidebar item. ⌘1–⌘6 switch pages. The sidebar toggle and native full screen (⌃⌘F) work.
- [ ] A4. Updates: from an installed 3.2.0, *Check for Updates…* offers 3.3.0 once it is published. It installs only after you approve. Afterwards the library, settings and downloaded models are all present.
- [ ] A5. Unreadable library: quit, add a few characters to the end of `archive.json` so it is invalid, then relaunch. A message says the library could not be read, a `archive.json.unreadable-…` copy exists, and typing does **not** change `archive.json` (check its modified time). *Restore* with a backup works and the unreadable copy remains.

## B. Saving, switching and quitting (data protection)
- [ ] B1. Edit a library sermon's transcript and outline, then quit with ⌘Q within one second. Reopen: the edits are present.
- [ ] B2. Start a new sermon and type a title and text. Open a library sermon: you are offered **Save Sermon / Discard / Cancel**, and each does what it says. Repeat with *New Sermon* (⌘N) and with ⌘Q.
- [ ] B3. Failed save: in Finder, lock `archive.json` (Get Info › Locked), or make the folder read-only. Edit a sermon. The header shows **Save failed** and the text stays. Opening another sermon is refused with an explanation. ⌘Q offers *Save Recovery Copy and Quit*, and a `Recovery-….json` appears in Documents/Sermon Studio (or your export folder). Unlock the file, restore the recovery file, and check the sermon.
- [ ] B4. Delete a sermon. A confirmation appears, then `archive.json.recovery-….json` is written before the deletion. Deleting the open sermon clears the editors. Multi-select delete works with the Delete key.
- [ ] B5. Rename, Duplicate and Toggle Favorite work from the context menu and the buttons. A duplicate gets new IDs, so changing it doesn't affect the original.
- [ ] B6. After an editing session, `archive.json.session-backup.json` contains the library as it was before that session.

## C. Transcription
- [ ] C1. Settings › Offline transcription lists five models with Installed/Download status. Downloading shows a percentage. *Cancel* stops it and removes the partial folder. *Delete* removes a model. *Open Models Folder* opens Finder.
- [ ] C2. Choose small.en, quit and relaunch: it is still selected.
- [ ] C3. Drag the short WAV onto the window. It loads, the title fills from the file name, the waveform appears, and *Transcribe on This Mac* produces a transcript with timestamps that is saved automatically.
- [ ] C4. Transcribe again on the same sermon: a **Replace the current transcript?** prompt appears.
- [ ] C5. Start the 120-minute recording, wait for at least one section, then press *Cancel*. You're offered **Keep Partial Transcript**, and keeping it saves the text with "[Transcription stopped early]".
- [ ] C6. Transcribe the 60- and 120-minute recordings. Record the duration and peak memory (Activity Monitor) here: __________. The app stays responsive, and memory does not grow in proportion to recording length.
- [ ] C7. The video file is accepted, and its audio is transcribed. The `.ogg` file shows a clear "cannot be decoded" message.
- [ ] C8. Biblical vocabulary: with assistance on, names such as *Melchizedek* and *Habakkuk* are spelled correctly more often than with it off. Compare one sample both ways.
- [ ] C9. While transcribing, opening another sermon, New and Restore are refused with a message.

## D. Audio playback
- [ ] D1. Play/Pause (button and ⌥Space), ±15 s, the scrubber, clicking or dragging the waveform, elapsed and total time, and speeds 0.75×–2× (pitch stays natural) all work.
- [ ] D2. In the Transcript Editor's Timestamps panel, clicking a time seeks and selects the matching transcript text. With *Follow playback* on, the active segment is highlighted and scrolls into view.
- [ ] D3. Low-confidence segments show a warning triangle.
- [ ] D4. Open another sermon while audio is playing: playback stops and the recording is no longer shown.

## E. Transcript editing
- [ ] E1. *Clean Transcript* creates a cleaned version without filler words. The original is unchanged (check *Compare*). Editing the cleaned version doesn't change the original.
- [ ] E2. Find with and without *Match case*. Counts update ("2 of 5"), previous/next wrap around, and matches are highlighted. *Replace* replaces only the current match; *Replace All* reports the count. ⌘Z undoes typing in the editor.
- [ ] E3. The word count updates after typing pauses. Spell-check underlines appear.
- [ ] E4. Typing in a 120-minute transcript stays smooth, with no visible lag per keystroke.
- [ ] E5. Focus mode (⇧⌘F) hides the sidebar, header and side panels; pressing ⇧⌘F again restores them.

## F. Outlines and rich-text editing
- [ ] F1. The default generator in Settings is applied to new sermons. Switching the generator in the outline workspace affects only the current sermon.
- [ ] F2. The Original generator works offline (Wi-Fi off) and asks before replacing an existing outline.
- [ ] F3. Ollama: *Set Up Ollama* detects, starts or links to the installer, downloads the model with progress, then shows "Ready". Generation can be cancelled. Switching sermons mid-generation discards the result ("discarded because the sermon … changed"). A model name containing `cloud` is rejected.
- [ ] F4. Paste this into **Markdown** mode, switch to **Visual**, then **Reading**, then back to Markdown. The text is identical character for character:
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
- [ ] F5. In Visual mode, edit only the "Point one" line and switch to Markdown. Only that line changed; `* star bullet` still uses `*`.
- [ ] F6. Heading, Subheading, Normal, Bold (⌘B), Italic (⌘I), Bullets, Numbered and Scripture quotation buttons work, as do Undo and Redo. Pressing Return after a heading continues in body text.
- [ ] F7. Type quickly in Visual mode, then within half a second press ⌘S, switch pages, or quit. Nothing is lost.
- [ ] F8. Drag the workspace divider, relaunch: the proportion is kept. The divider can be adjusted with VoiceOver.
- [ ] F9. *Find Source* with a phrase from the outline selects and scrolls to it in the transcript pane.
- [ ] F10. Change the editor text size in Settings while the visual editor shows headings. Headings stay headings and scale with the text.

## G. Scripture
- [ ] G1. Text containing "1 John 4:8, 2 Cor 5:17-21, Ps 23, Jude 3, Eph. 2:8-9" lists `1 John 4:8`, `2 Corinthians 5:17-21`, `Psalm 23`, `Jude 1:3` and `Ephesians 2:8-9`, and does **not** list "John 4:8".
- [ ] G2. Reference links open esv.org. *KJV* opens a preview labelled "King James Version (public domain)" when online, and shows an "unavailable offline" message without internet. No ESV API key is requested anywhere.
- [ ] G3. References in the outline Reading view are links. In exported DOCX, RTF and PDF they are clickable.

## H. Library, settings and backup
- [ ] H1. Search finds words that appear only in a transcript, only in an outline, and only in a tag.
- [ ] H2. Filters (Favorites, Speaker, Tag, Series, Collection, From/To) and all four sort orders work. *Clear Filters* resets them. The statistics line updates.
- [ ] H3. The Dashboard shows counts and recently opened sermons; clicking one opens it.
- [ ] H4. Settings persistence: change the appearance, font size (A buttons), default model, default generator, Ollama model, workspace split, outline mode, follow playback and export folder. Quit and relaunch: all are kept.
- [ ] H5. Choose an export folder, rename the folder in Finder, and relaunch: exports still go to it (the bookmark resolves).
- [ ] H6. *Back Up Library…* writes a JSON file. *Restore* from the same file reports that every sermon is already present. Restoring an archive with a conflicting sermon offers *Skip Existing* or *Import as Copies* and never overwrites.

## I. Cross-platform archive transfer
- [ ] I1. Restore a Windows `sermons.json`. Speaker, primary scripture, series, collection, tags, favourite, last edited, outline formatting and timestamps all appear.
- [ ] I2. Restore a web-app backup. Timestamps (from `chunks`) appear, and web-only fields are kept when you back up again (search the new backup for them).
- [ ] I3. Back up on the Mac and restore that file on Windows (Settings › Restore library) and in the web app. Sermons, metadata and timestamps arrive intact.
- [ ] I4. Restore the Windows *Mac-compatible archive*: all sermons appear.

## J. Document exports
- [ ] J1. Transcript exports TXT, MD, RTF, DOCX, PDF, SRT and VTT, and outline exports TXT, MD, RTF, DOCX and PDF, all succeed. They open in TextEdit, Word, Preview and VLC.
- [ ] J2. The outline DOCX and RTF open in Word with headings, bold, italic, bold-italic, bullets, numbering and indented italic quotations.
- [ ] J3. A PDF of a 120-minute transcript, and of a long outline, has many pages. The last paragraph is present; nothing is clipped.
- [ ] J4. With a default export folder set, exporting twice creates `Title (2).ext` and never replaces a file. Without one, the save panel asks before replacing.
- [ ] J5. A title like `CON: Faith/Hope?` exports with a safe file name. An empty sermon exports without errors. Unicode survives in every format (é, ü, ✝, 恩典, 🙏🏽).
- [ ] J6. Print Outline (⌘P) and Print Transcript (⇧⌘P) paginate correctly; *Save as PDF* in the print dialog works.
- [ ] J7. *Export All* and *Export Shown* create a new folder with `sermons.json` and per-sermon files. *Show in Finder* and *Share* work for the last export.

## K. Accessibility
- [ ] K1. VoiceOver reads sidebar items, the save status, library rows (title, date, details), the play/skip buttons, the waveform (with value), the font-size buttons ("Large editor text, 21 points") and the format buttons.
- [ ] K2. Keyboard only: you can Tab through every control. Shortcuts work without the mouse.
- [ ] K3. Light, Dark and System appearance; *Increase contrast*; *Reduce transparency*. Text is readable everywhere and primary buttons switch to the system style in increase-contrast mode.
- [ ] K4. Window resized down to the minimum (900×600): nothing overlaps or is cut off.

**Result:** ____ of ____ passed · Tester/date/build: ______________________
