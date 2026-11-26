import AppKit
import SwiftUI
import UniformTypeIdentifiers
import SermonCore

enum Page: String, CaseIterable, Identifiable {
    case newSermon = "New Sermon", dashboard = "Dashboard", transcript = "Transcript Editor", outlines = "Sermon Outlines", library = "Sermon Library", settings = "Settings"
    var id: String { rawValue }
    var symbol: String {
        switch self {
        case .newSermon: return "plus.circle"
        case .dashboard: return "square.grid.2x2"
        case .transcript: return "doc.text"
        case .outlines: return "text.book.closed"
        case .library: return "books.vertical"
        case .settings: return "gearshape"
        }
    }
}

enum SaveState: Equatable {
    case unsaved, pending, saved(Date), failed(String)
    var label: String {
        switch self {
        case .unsaved: return "Not yet in library"
        case .pending: return "Saving…"
        case .saved(let date): return "Saved " + date.formatted(date: .omitted, time: .shortened)
        case .failed: return "Save failed"
        }
    }
}

/// Application state. Every path that leaves the open sermon (open, new, delete, restore, quit) saves first and
/// refuses to continue if saving failed, so edits are never silently discarded.
@MainActor final class AppModel: ObservableObject {
    static let shared = AppModel()

    let settings = SettingsStore()
    let store = LibraryStore(url: LibraryStore.defaultURL)
    let guardian = WorkspaceGuard()
    let audio = AudioController()
    let transcriber = TranscriptionService()
    let outlineEditor = RichEditorController()
    private let ollama = OllamaClient()

    @Published var page: Page = .newSermon
    @Published private(set) var library: [SermonRecord] = []
    @Published var draft = SermonRecord() { didSet { if !loading { draftChanged() } } }
    @Published private(set) var selectedID: String?
    @Published var editingCleaned = false
    @Published var status = "Welcome to Sermon Studio."
    @Published private(set) var saveState: SaveState = .unsaved
    @Published var focusMode = false

    @Published var generator: OutlineGeneratorKind = .original
    @Published private(set) var generating = false
    @Published private(set) var outlineSource = ""
    @Published var ollamaStatus = "Optional. Ollama runs AI outlines entirely on this Mac."
    @Published private(set) var ollamaBusy = false

    @Published var fontSize: Double { didSet { settings.fontSize = fontSize; outlineEditor.baseSize = settings.fontSize } }
    @Published var appearance: AppearanceMode { didSet { settings.appearance = appearance } }
    @Published var defaultModel: String { didSet { settings.model = defaultModel } }
    @Published var defaultGenerator: OutlineGeneratorKind { didSet { settings.outlineGenerator = defaultGenerator } }
    @Published var ollamaModel: String
    @Published var biblicalAssistance: Bool { didSet { settings.biblicalAssistance = biblicalAssistance } }
    @Published var vocabulary: String { didSet { settings.vocabulary = vocabulary } }
    @Published var workspaceSplit: Double { didSet { settings.workspaceSplit = workspaceSplit } }
    @Published var outlineMode: String { didSet { settings.outlineMode = outlineMode } }
    @Published var followPlayback: Bool { didSet { settings.followPlayback = followPlayback } }
    @Published var exportFolder: URL?
    @Published var lastExport: URL?

    private var loading = false
    private var autosaveTask: Task<Void, Never>?
    private var generationTask: Task<Void, Never>?
    private var ollamaSetupTask: Task<Void, Never>?
    private var transcriptionTask: Task<Void, Never>?

    static let defaultVocabulary = "Jesus Christ, Holy Spirit, Messiah, Pharisees, Sadducees, Gethsemane, Golgotha, Nazareth, Galilee, Bethlehem, Jerusalem, Melchizedek, Nebuchadnezzar, Habakkuk, Zephaniah, Thessalonians, Philippians, Colossians, Deuteronomy, Leviticus, Ecclesiastes, propitiation, sanctification, justification, atonement, covenant, hallelujah, hosanna"

    private init() {
        fontSize = settings.fontSize
        appearance = settings.appearance
        defaultModel = settings.model
        defaultGenerator = settings.outlineGenerator
        ollamaModel = settings.ollamaModel
        biblicalAssistance = settings.biblicalAssistance
        vocabulary = settings.vocabulary.isEmpty ? Self.defaultVocabulary : settings.vocabulary
        workspaceSplit = settings.workspaceSplit
        outlineMode = settings.outlineMode
        followPlayback = settings.followPlayback
        generator = settings.outlineGenerator
        outlineEditor.baseSize = settings.fontSize
        exportFolder = resolveExportFolder()
        store.load()
        library = store.records
        if let error = store.loadError {
            status = "Your library could not be read (" + error + "). It has not been changed"
                + (store.preservedCopy.map { "; a copy was kept at " + $0.path } ?? "")
                + ". Saving is blocked. Use Sermon Library › Restore to load a backup."
            saveState = .failed("Library unreadable")
        }
        Task.detached(priority: .background) { TranscriptionService.cleanupTemporaryAudio() }
    }

    // MARK: Editing state

    var isNewDraft: Bool { selectedID == nil }
    var activeTranscript: String { editingCleaned ? draft.cleaned : draft.original }
    var draftHasContent: Bool {
        !draft.original.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !draft.cleaned.isEmpty || !draft.outline.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            || !draft.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private func draftChanged() {
        if case .failed = saveState { } else { saveState = isNewDraft ? .unsaved : .pending }
        guard !isNewDraft else { return }
        autosaveTask?.cancel()
        autosaveTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 900_000_000)
            guard !Task.isCancelled else { return }
            self?.save()
        }
    }

    private func load(_ record: SermonRecord?, id: String?) {
        loading = true
        draft = record ?? SermonRecord(date: ArchiveDates.day(Date()))
        selectedID = id
        editingCleaned = false
        outlineSource = draft.outline.isEmpty ? "" : activeTranscript
        generator = defaultGenerator
        loading = false
        saveState = id == nil ? .unsaved : .saved(record?.lastEdited ?? Date())
    }

    /// Saves the open sermon (adding it to the library if new). Returns false, keeping the editor text, if it fails.
    @discardableResult
    func save() -> Bool {
        autosaveTask?.cancel()
        autosaveTask = nil
        outlineEditor.flush()
        var record = draft
        record.title = record.title.trimmingCharacters(in: .whitespacesAndNewlines)
        record.original = OutlineMarkdown.normalizeNewlines(record.original)
        record.cleaned = OutlineMarkdown.normalizeNewlines(record.cleaned)
        record.outline = OutlineMarkdown.normalizeNewlines(record.outline)
        if record.date.isEmpty { record.date = ArchiveDates.day(Date()) }
        record.lastEdited = Date()
        let error = store.transaction { records in
            if let index = records.firstIndex(where: { $0.id == record.id }) { records[index] = record } else { records.insert(record, at: 0) }
        }
        if let error {
            saveState = .failed(error.localizedDescription)
            status = "Save failed: " + error.localizedDescription + " Your text is still in the editor."
            return false
        }
        library = store.records
        loading = true
        draft.lastEdited = record.lastEdited
        loading = false
        selectedID = record.id
        saveState = .saved(Date())
        status = "Saved to your library."
        return true
    }

    /// Saves pending edits. Returns false when the open sermon has unsaved changes that could not be written.
    func flushPendingSave() -> Bool {
        outlineEditor.flush()
        if case .failed = saveState, !isNewDraft { return save() }
        if !isNewDraft, saveState == .pending || autosaveTask != nil { return save() }
        return true
    }

    /// Asks what to do with a new sermon that was never saved. Returns true when it is safe to leave it.
    func confirmLeavingDraft(_ action: String) -> Bool {
        outlineEditor.flush()
        guard isNewDraft, draftHasContent else { return true }
        let alert = NSAlert()
        alert.messageText = "Save this new sermon before " + action + "?"
        alert.informativeText = "It has not been added to your library yet."
        alert.addButton(withTitle: "Save Sermon")
        alert.addButton(withTitle: "Discard")
        alert.addButton(withTitle: "Cancel")
        switch alert.runModal() {
        case .alertFirstButtonReturn: return save()
        case .alertSecondButtonReturn: return true
        default: return false
        }
    }

    private func canLeaveCurrentSermon(_ action: String) -> Bool {
        if transcriber.isTranscribing { status = "Finish or cancel the transcription first."; return false }
        guard flushPendingSave() else { status = "The open sermon could not be saved, so it was kept open. Fix the problem and save again."; return false }
        return confirmLeavingDraft(action)
    }

    private func leaveCurrentSermon() {
        guardian.advance()
        generationTask?.cancel()
        audio.unload()
    }

    func newSermon() {
        guard canLeaveCurrentSermon("starting a new one") else { return }
        leaveCurrentSermon()
        load(nil, id: nil)
        page = .newSermon
        status = "New sermon. Choose a recording or start writing."
    }

    func open(_ id: String) {
        guard id != selectedID else { page = .transcript; return }
        guard let record = library.first(where: { $0.id == id }) else { return }
        guard canLeaveCurrentSermon("opening another sermon") else { return }
        leaveCurrentSermon()
        load(record, id: id)
        if let error = store.transaction({ records in
            if let index = records.firstIndex(where: { $0.id == id }) { records[index].lastOpened = Date() }
        }) { status = "Opened. Recent history could not be saved: " + error.localizedDescription }
        else { status = "Opened “" + record.displayTitle + "”." }
        library = store.records
        page = .transcript
    }

    // MARK: Library management

    func rename(_ id: String) {
        guard let record = library.first(where: { $0.id == id }) else { return }
        guard let name = prompt("Rename sermon", detail: "Enter a new title.", value: record.title, button: "Rename") else { return }
        if id == selectedID { draft.title = name; save(); return }
        if let error = store.transaction({ records in
            if let index = records.firstIndex(where: { $0.id == id }) { records[index].title = name; records[index].lastEdited = Date() }
        }) { status = "Rename failed: " + error.localizedDescription } else { status = "Renamed." }
        library = store.records
    }

    func duplicate(_ id: String) {
        guard flushPendingSave(), var copy = library.first(where: { $0.id == id }) else { return }
        copy.id = UUID().uuidString
        copy.title = copy.displayTitle + " (copy)"
        copy.lastOpened = nil
        copy.lastEdited = Date()
        copy.segments = copy.segments?.map { var s = $0; s.id = UUID().uuidString; return s }
        if let error = store.transaction({ $0.insert(copy, at: 0) }) { status = "Duplicate failed: " + error.localizedDescription }
        else { status = "Duplicated as “" + copy.title + "”." }
        library = store.records
    }

    func delete(_ ids: [String]) {
        let targets = library.filter { ids.contains($0.id) }
        guard !targets.isEmpty else { return }
        if transcriber.isTranscribing && ids.contains(selectedID ?? "") { status = "Finish the transcription first."; return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = targets.count == 1 ? "Delete “" + targets[0].displayTitle + "”?" : "Delete \(targets.count) sermons?"
        alert.informativeText = "A recovery archive of your library is saved first, so this can be undone with Restore."
        alert.addButton(withTitle: "Delete")
        alert.addButton(withTitle: "Cancel")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        if !(ids.contains(selectedID ?? "")) { guard flushPendingSave() else { status = "Save the open sermon first."; return } }
        let recovery: URL
        do { recovery = try store.writeRecoveryArchive(store.records, label: "recovery") }
        catch { status = "Nothing was deleted: the recovery archive could not be written (" + error.localizedDescription + ")."; return }
        if let error = store.transaction({ $0.removeAll { ids.contains($0.id) } }) { status = "Delete failed: " + error.localizedDescription; return }
        library = store.records
        if let selectedID, ids.contains(selectedID) { autosaveTask?.cancel(); leaveCurrentSermon(); load(nil, id: nil) }
        status = "Deleted. Recovery archive: " + recovery.lastPathComponent
    }

    func toggleFavorite(_ id: String) {
        if id == selectedID { draft.favorite.toggle(); return }
        if let error = store.transaction({ records in if let i = records.firstIndex(where: { $0.id == id }) { records[i].favorite.toggle() } }) { status = error.localizedDescription }
        library = store.records
    }

    func prompt(_ title: String, detail: String, value: String, button: String) -> String? {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = detail
        let field = NSTextField(string: value)
        field.frame = NSRect(x: 0, y: 0, width: 320, height: 24)
        alert.accessoryView = field
        alert.addButton(withTitle: button)
        alert.addButton(withTitle: "Cancel")
        alert.window.initialFirstResponder = field
        guard alert.runModal() == .alertFirstButtonReturn else { return nil }
        let text = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        return text.isEmpty ? nil : text
    }

    // MARK: Backup and restore

    func backup(_ records: [SermonRecord]? = nil, suggestedName: String = "SermonStudio-backup") {
        guard flushPendingSave() else { status = "Save the open sermon before backing up."; return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = suggestedName + "-" + ArchiveDates.day(Date()) + ".json"
        panel.message = "The archive opens in Sermon Studio on Mac, Windows and the web."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let items = records ?? store.records
            try AtomicFile.write(ArchiveCodec.encodeLibrary(items), to: url)
            status = "Archive of \(items.count) sermons saved to " + url.path
        } catch { status = "Backup failed: " + error.localizedDescription }
    }

    func restore() {
        guard canLeaveCurrentSermon("restoring") else { return }
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        panel.message = "Choose a Sermon Studio archive from Mac, Windows or the web."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let plan = try ArchiveCodec.planRestore(existing: store.records, data: Data(contentsOf: url))
            var additions = plan.additions
            if !plan.conflicts.isEmpty {
                let alert = NSAlert()
                alert.messageText = "\(plan.conflicts.count) sermon(s) in this archive are already in your library."
                alert.informativeText = "Existing sermons are never overwritten. You can skip them or import them as separate copies."
                alert.addButton(withTitle: "Skip Existing")
                alert.addButton(withTitle: "Import as Copies")
                alert.addButton(withTitle: "Cancel")
                switch alert.runModal() {
                case .alertFirstButtonReturn: break
                case .alertSecondButtonReturn: additions += ArchiveCodec.copiesWithNewIdentifiers(plan.conflicts)
                default: return
                }
            }
            guard !additions.isEmpty else { status = "Nothing to restore: every sermon is already in your library."; return }
            var unblocked = false
            if store.isWriteBlocked {
                guard store.unblockAfterPreservedFailure() else { status = "Restore is blocked because the unreadable library could not be preserved."; return }
                unblocked = true
            }
            if let error = store.transaction({ $0.append(contentsOf: additions) }) {
                status = "Restore failed: " + error.localizedDescription + " Your library was not changed."
                return
            }
            library = store.records
            if unblocked { saveState = isNewDraft ? .unsaved : .saved(Date()) }
            status = "Restored \(additions.count) sermon(s)" + (plan.conflicts.isEmpty ? "." : "; \(plan.conflicts.count) already present.")
        } catch { status = "Restore failed: " + error.localizedDescription }
    }

    /// Writes the open sermon to a standalone archive (used when quitting after a failed save).
    func writeRecoveryCopy() -> URL? {
        outlineEditor.flush()
        do {
            let folder = try exportDirectory()
            let url = try ExportNaming.reserveNewFile(in: folder, base: "Recovery-" + ExportNaming.sanitize(draft.displayTitle), extension: ".json")
            try AtomicFile.write(ArchiveCodec.encodeLibrary([draft]), to: url)
            return url
        } catch { status = "Recovery copy failed: " + error.localizedDescription; return nil }
    }

    // MARK: Quit

    func shouldTerminate() -> NSApplication.TerminateReply {
        outlineEditor.flush()
        if !isNewDraft, saveState == .pending || autosaveTask != nil { _ = save() }
        var problems: [String] = []
        if case .failed = saveState, !isNewDraft { problems.append("The open sermon could not be saved.") }
        if isNewDraft && draftHasContent { problems.append("The new sermon has not been saved to your library.") }
        if transcriber.isTranscribing { problems.append("A transcription is still running.") }
        if generating { problems.append("An outline is still being generated.") }
        if transcriber.isDownloading { problems.append("A model is still downloading.") }
        guard !problems.isEmpty else { return .terminateNow }
        let alert = NSAlert()
        alert.messageText = "Quit Sermon Studio?"
        alert.informativeText = problems.joined(separator: " ")
        let failed: Bool = { if case .failed = saveState { return true }; return false }()
        let primary = failed ? "Save Recovery Copy and Quit" : (isNewDraft && draftHasContent) ? "Save and Quit" : "Quit"
        alert.addButton(withTitle: primary)
        alert.addButton(withTitle: "Keep Working")
        if failed || (isNewDraft && draftHasContent) { alert.addButton(withTitle: "Quit Without Saving") }
        switch alert.runModal() {
        case .alertFirstButtonReturn:
            if failed { guard writeRecoveryCopy() != nil else { return .terminateCancel } }
            else if isNewDraft && draftHasContent { guard save() else { return .terminateCancel } }
        case .alertSecondButtonReturn: return .terminateCancel
        default: break
        }
        transcriber.cancelTranscription()
        transcriber.cancelDownload()
        generationTask?.cancel()
        audio.unload()
        return .terminateNow
    }

    // MARK: Audio and transcription

    func chooseAudio() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.audio, .movie, .mpeg4Movie, .quickTimeMovie]
        panel.message = "Choose a sermon recording."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        importAudio(url)
    }

    /// Loads a recording for the open sermon (also used by drag and drop).
    func importAudio(_ url: URL) {
        if transcriber.isTranscribing { status = "Finish or cancel the transcription first."; return }
        let ticket = guardian.capture()
        Task {
            do {
                try await audio.load(url)
                guard guardian.isCurrent(ticket) else { audio.unload(); return }
                if draft.title.trimmingCharacters(in: .whitespaces).isEmpty { draft.title = url.deletingPathExtension().lastPathComponent }
                status = "Recording loaded: " + url.lastPathComponent + ". Ready to transcribe."
            } catch { status = error.localizedDescription }
        }
    }

    func transcribe() {
        guard let url = audio.url, !transcriber.isTranscribing else { return }
        if !draft.original.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            let alert = NSAlert()
            alert.messageText = "Replace the current transcript?"
            alert.informativeText = "Transcribing replaces this sermon's transcript and timestamps. The outline is kept."
            alert.addButton(withTitle: "Transcribe and Replace")
            alert.addButton(withTitle: "Cancel")
            guard alert.runModal() == .alertFirstButtonReturn else { return }
        }
        let ticket = guardian.capture(url.path)
        let model = defaultModel
        let words = biblicalAssistance ? vocabulary : nil
        transcriptionTask = Task {
            do {
                let output = try await transcriber.transcribe(url, model: model, vocabulary: words) { [weak self] message in self?.status = message }
                apply(output, ticket: ticket, url: url, partial: false)
            } catch let cancelled as TranscriptionCancelled {
                guard guardian.isCurrent(ticket, audio.url?.path ?? "") else { return }
                if cancelled.partial.text.isEmpty { status = "Transcription canceled."; return }
                let alert = NSAlert()
                alert.messageText = "Keep the partial transcript?"
                alert.informativeText = "Transcription was stopped at " + TranscriptTools.timestamp(cancelled.partial.segments.last?.end ?? 0) + "."
                alert.addButton(withTitle: "Keep Partial Transcript")
                alert.addButton(withTitle: "Discard")
                if alert.runModal() == .alertFirstButtonReturn { apply(cancelled.partial, ticket: ticket, url: url, partial: true) }
                else { status = "Transcription canceled." }
            } catch {
                status = "Transcription failed: " + error.localizedDescription
            }
        }
    }

    private func apply(_ output: TranscriptionOutput, ticket: WorkspaceGuard.Ticket, url: URL, partial: Bool) {
        // Never place a transcript into a different sermon than the one it was started for.
        guard guardian.isCurrent(ticket, audio.url?.path ?? "") else { status = "Transcription result discarded because the open sermon changed."; return }
        draft.original = output.text + (partial ? "\n\n[Transcription stopped early]" : "")
        draft.cleaned = ""
        draft.segments = output.segments
        draft.srt = Subtitles.make(output.segments, vtt: false)
        draft.vtt = Subtitles.make(output.segments, vtt: true)
        if draft.title.trimmingCharacters(in: .whitespaces).isEmpty { draft.title = url.deletingPathExtension().lastPathComponent }
        editingCleaned = false
        page = .transcript
        if save() { status = partial ? "Partial transcript saved with timestamps." : "Transcription complete and saved with timestamps." }
    }

    func cancelTranscription() { transcriber.cancelTranscription(); status = "Stopping after the current section…" }

    func cleanTranscript() {
        draft.cleaned = TranscriptTools.clean(draft.original)
        editingCleaned = true
        status = "Cleaned version created. The original transcript is unchanged."
    }

    // MARK: Outline generation

    func generateOutline() {
        outlineEditor.flush()
        let transcript = activeTranscript.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !transcript.isEmpty else { status = "Add or transcribe a sermon before generating an outline."; return }
        guard !generating else { return }
        if !draft.outline.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            let alert = NSAlert()
            alert.messageText = "Replace the current outline?"
            alert.informativeText = "The generated outline replaces what is in the outline editor."
            alert.addButton(withTitle: "Replace")
            alert.addButton(withTitle: "Cancel")
            guard alert.runModal() == .alertFirstButtonReturn else { return }
        }
        if generator == .original {
            guard let outline = OriginalOutlineGenerator.generate(title: draft.title, transcript: transcript) else { status = "Not enough transcript content to build an outline."; return }
            draft.outline = outline
            outlineSource = transcript
            status = "Outline generated offline. Review all sections before sharing."
            return
        }
        let model = ollamaModel.trimmingCharacters(in: .whitespaces)
        guard SettingsStore.isValidOllamaModel(model) else { status = OllamaError.invalidModel.localizedDescription; return }
        let ticket = guardian.capture(transcript, draft.outline)
        let title = draft.title
        let client = ollama
        generating = true
        status = "Generating outline locally with " + model + "…"
        generationTask = Task {
            defer { generating = false; generationTask = nil }
            do {
                guard await client.isRunning() else { throw OllamaError.notRunning }
                let chunks = OllamaOutlinePrompts.chunks(transcript)
                var notes: [String] = []
                for (index, chunk) in chunks.enumerated() {
                    try Task.checkCancellation()
                    status = "Reading passage \(index + 1) of \(chunks.count) locally…"
                    notes.append(try await withTimeout(seconds: 600) { try await client.generate(model: model, prompt: OllamaOutlinePrompts.passage(chunk, index: index, count: chunks.count)) })
                }
                try Task.checkCancellation()
                status = "Writing the structured outline locally…"
                let collected = notes
                let result = try await withTimeout(seconds: 600) {
                    try await client.generate(model: model, prompt: OllamaOutlinePrompts.outline(title: title, references: ScriptureReferences.distinct(transcript), notes: collected))
                }
                try Task.checkCancellation()
                outlineEditor.flush()
                guard guardian.isCurrent(ticket, activeTranscript.trimmingCharacters(in: .whitespacesAndNewlines), draft.outline) else {
                    status = "Outline result discarded because the sermon or outline changed while it was generating."
                    return
                }
                draft.outline = result
                outlineSource = transcript
                status = "AI outline generated on this Mac. Verify themes and Bible references before sharing."
            } catch is CancellationError {
                status = "Outline generation canceled. Your outline was not changed."
            } catch {
                status = error.localizedDescription + " The original generator works without Ollama."
            }
        }
    }

    func cancelGeneration() { generationTask?.cancel() }

    private func withTimeout<T: Sendable>(seconds: Double, _ work: @escaping @Sendable () async throws -> T) async throws -> T {
        try await withThrowingTaskGroup(of: T.self) { group in
            group.addTask { try await work() }
            group.addTask {
                try await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                throw NSError(domain: "SermonStudio", code: 408, userInfo: [NSLocalizedDescriptionKey: "The local model took too long to respond."])
            }
            let result = try await group.next()!
            group.cancelAll()
            return result
        }
    }

    func setupOllama() {
        let model = ollamaModel.trimmingCharacters(in: .whitespaces)
        guard SettingsStore.isValidOllamaModel(model) else { ollamaStatus = OllamaError.invalidModel.localizedDescription; return }
        settings.ollamaModel = model
        ollamaBusy = true
        let ollama = self.ollama
        ollamaSetupTask = Task {
            defer { ollamaBusy = false; ollamaSetupTask = nil }
            do {
                if !(await ollama.isRunning()) {
                    let paths = ["/Applications/Ollama.app", FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications/Ollama.app").path]
                    guard let app = paths.first(where: { FileManager.default.fileExists(atPath: $0) }) else {
                        NSWorkspace.shared.open(URL(string: "https://ollama.com/download/mac")!)
                        ollamaStatus = "Ollama is not installed. The official download page has opened; install it, then choose Set Up Ollama again."
                        return
                    }
                    ollamaStatus = "Starting Ollama…"
                    NSWorkspace.shared.open(URL(fileURLWithPath: app))
                    var ready = false
                    for _ in 0..<30 {
                        try Task.checkCancellation()
                        if await ollama.isRunning() { ready = true; break }
                        try await Task.sleep(nanoseconds: 1_000_000_000)
                    }
                    guard ready else { ollamaStatus = "Ollama did not start. Open it from Applications, then try again."; return }
                }
                if try await ollama.hasModel(model) { ollamaStatus = "Ready: " + model + " is installed locally."; return }
                ollamaStatus = "Downloading " + model + "…"
                try await ollama.pull(model) { state, fraction in
                    Task { @MainActor in AppModel.shared.ollamaStatus = state.capitalized + (fraction.map { String(format: " · %.0f%%", $0 * 100) } ?? "") }
                }
                ollamaStatus = "Ready: " + model + " is installed locally."
            } catch is CancellationError {
                ollamaStatus = "Ollama setup canceled."
            } catch {
                ollamaStatus = "Ollama setup failed: " + error.localizedDescription
            }
        }
    }

    func cancelOllamaSetup() { ollamaSetupTask?.cancel() }

    func commitOllamaModel() {
        if SettingsStore.isValidOllamaModel(ollamaModel) { settings.ollamaModel = ollamaModel.trimmingCharacters(in: .whitespaces) }
        else { ollamaStatus = OllamaError.invalidModel.localizedDescription }
    }

    // MARK: Export folder

    private func resolveExportFolder() -> URL? {
        if let bookmark = settings.exportBookmark {
            var stale = false
            if let url = try? URL(resolvingBookmarkData: bookmark, options: [.withSecurityScope], relativeTo: nil, bookmarkDataIsStale: &stale) {
                if stale, let fresh = try? url.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil) { settings.exportBookmark = fresh }
                return url
            }
        }
        let path = settings.exportFolderPath
        return path.isEmpty ? nil : URL(fileURLWithPath: path)
    }

    func chooseExportFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = "Use Folder"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        settings.exportFolderPath = url.path
        // A security-scoped bookmark keeps access working if the app is ever sandboxed or the folder is renamed.
        settings.exportBookmark = try? url.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)
        exportFolder = url
    }

    func resetExportFolder() {
        settings.exportFolderPath = ""
        settings.exportBookmark = nil
        exportFolder = nil
    }

    /// The default export folder, or Documents/Sermon Studio.
    func exportDirectory() throws -> URL {
        if let exportFolder {
            _ = exportFolder.startAccessingSecurityScopedResource()
            if FileManager.default.fileExists(atPath: exportFolder.path) { return exportFolder }
        }
        let folder = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("Sermon Studio")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder
    }
}
