import SwiftUI
import AppKit
import UniformTypeIdentifiers
import WhisperKit
import Sparkle
import AVKit

@MainActor final class StudioUpdates: ObservableObject {
    let controller: SPUStandardUpdaterController?
    init() {
        if Bundle.main.object(forInfoDictionaryKey: "SUPublicEDKey") != nil {
            controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)
        } else { controller = nil }
    }
}

struct Sermon: Codable, Identifiable {
    var id = UUID().uuidString
    var title: String
    var date: String
    var original: String
    var cleaned: String
    var segments: [SermonSegment]? = nil
}
struct SermonSegment: Codable, Identifiable {
    var id = UUID()
    var start: Double
    var end: Double
    var text: String
    var confidence: Double?
}
@main struct StudioApp: App {
    var body: some Scene { WindowGroup("Sermon Studio") { StudioView() }.defaultSize(width: 1100, height: 720) }
}
struct StudioView: View {
    @AppStorage("defaultModel") private var model = "small.en"
    @AppStorage("exportFolder") private var folder = ""
    @State private var page: String? = "New sermon"
    @StateObject private var updates = StudioUpdates()
    @State private var audio: URL?
    @State private var title = ""
    @State private var original = ""
    @State private var cleaned = ""
    @State private var useCleaned = false
    @State private var status = "Choose an audio recording."
    @State private var busy = false
    @State private var library: [Sermon] = []
    @State private var selectedID: String?
    @State private var task: Task<Void, Never>?
    @State private var search = ""
    @State private var findText = ""
    @State private var replacement = ""
    @State private var compare = false
    @State private var showOutline = false
    @AppStorage("editorFontSize") private var fontSize = 18.0
    @AppStorage("appearanceMode") private var appearanceMode = "system"
    private var preferredAppearance: ColorScheme? {
        switch appearanceMode {
        case "light": return .light
        case "dark": return .dark
        default: return nil
        }
    }
    @State private var sermonDate = Date()
    @State private var segments: [SermonSegment] = []
    @State private var player: AVPlayer?
    @AppStorage("biblicalAssistance") private var biblicalAssistance = true
    @State private var contextVocabulary = ""
    private var activeText: String { useCleaned ? cleaned : original }
    private var references: [String] {
        let books = "Genesis|Exodus|Leviticus|Numbers|Deuteronomy|Joshua|Judges|Ruth|1 Samuel|2 Samuel|1 Kings|2 Kings|1 Chronicles|2 Chronicles|Ezra|Nehemiah|Esther|Job|Psalms?|Proverbs|Ecclesiastes|Song of Solomon|Isaiah|Jeremiah|Lamentations|Ezekiel|Daniel|Hosea|Joel|Amos|Obadiah|Jonah|Micah|Nahum|Habakkuk|Zephaniah|Haggai|Zechariah|Malachi|Matthew|Mark|Luke|John|Acts|Romans|1 Corinthians|2 Corinthians|Galatians|Ephesians|Philippians|Colossians|1 Thessalonians|2 Thessalonians|1 Timothy|2 Timothy|Titus|Philemon|Hebrews|James|1 Peter|2 Peter|1 John|2 John|3 John|Jude|Revelation"
        let pattern = "\\b(?:" + books + ")\\s+\\d+(?::\\d+(?:[-–]\\d+)?)?\\b"
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return [] }
        let ns = activeText as NSString
        var seen = Set<String>()
        return regex.matches(in: activeText, range: NSRange(location: 0, length: ns.length))
            .map { ns.substring(with: $0.range) }
            .filter { seen.insert($0.lowercased()).inserted }
    }
    private var outline: [(heading: String, detail: String)] {
        let sentences = activeText
            .replacingOccurrences(of: "\n", with: " ")
            .components(separatedBy: CharacterSet(charactersIn: ".!?"))
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { $0.count >= 20 }
        guard !sentences.isEmpty else { return [] }
        let count = min(5, sentences.count)
        let sectionSize = max(1, Int(ceil(Double(sentences.count) / Double(count))))
        return stride(from: 0, to: sentences.count, by: sectionSize).prefix(count).enumerated().map { index, offset in
            let section = Array(sentences[offset..<min(offset + sectionSize, sentences.count)])
            let first = section[0]
            let heading = first.count > 72 ? String(first.prefix(69)) + "…" : first
            let detail = section.dropFirst().prefix(2).joined(separator: ". ")
            return (heading: "Point \(index + 1): " + heading, detail: detail)
        }
    }
    private let models = ["base.en", "small.en", "medium.en", "large-v3-v20240930_626MB"]
    private func resolvedModel(_ selection: String) -> String {
        switch selection {
        case "large-v3-turbo", "large-v3-v20240930_626MB": return "large-v3-v20240930_626MB"
        default: return selection
        }
    }
    private var archiveURL: URL { FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("SermonStudio/archive.json") }
    var body: some View {
        NavigationSplitView {
            List(["Dashboard", "New sermon", "Transcript editor", "Sermon library", "Settings"], id: \.self, selection: $page) { Text($0).tag($0) }
        } detail: {
            VStack(alignment: .leading, spacing: 16) {
                Text(page ?? "New sermon").font(.largeTitle.bold())
                if page == "Dashboard" {
                    Text("\(library.count) saved sermons").font(.title2)
                    Text("Recent sermons").font(.headline)
                    ForEach(library.prefix(5)) { item in
                        Button(item.date + "  " + item.title) { openSermon(item) }
                    }
                    Button("Start a new sermon") { page = "New sermon" }
                } else if page == "New sermon" {
                    Button("Choose audio recording…", action: chooseAudio).disabled(busy)
                    Text(audio?.lastPathComponent ?? "No recording selected")
                    modelPicker
                    DisclosureGroup("Advanced transcription settings") {
                        Toggle("Biblical vocabulary assistance (experimental)", isOn: $biblicalAssistance)
                        TextField("Names and vocabulary", text: $contextVocabulary)
                        Text("Vocabulary is saved for reference; this WhisperKit version does not apply prompt hints during transcription.").font(.caption)
                    }
                    if let player {
                        HStack {
                            Button("Play recording") { player.play() }
                            Button("Pause recording") { player.pause() }
                        }
                    }
                    Button("Transcribe locally", action: start).disabled(audio == nil || busy)
                    if busy { ProgressView(); Button("Cancel") { task?.cancel() } }
                } else if page == "Transcript editor" {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 16) {
                        TextField("Sermon title", text: $title)
                        DatePicker("Sermon date", selection: $sermonDate, displayedComponents: .date)
                        Picker("Transcript", selection: $useCleaned) { Text("Original").tag(false); Text("Cleaned").tag(true) }.pickerStyle(.segmented)
                        HStack {
                            TextField("Find", text: $findText)
                            TextField("Replace", text: $replacement)
                            Button("Replace all", action: replaceAll).disabled(findText.isEmpty)
                            Button("Compare") { compare.toggle() }
                            Button("Clean text") { cleaned = clean(original) }
                        }
                        if compare {
                            HStack {
                                ScrollView { Text(original).textSelection(.enabled) }
                                Divider()
                                ScrollView { Text(cleaned).textSelection(.enabled) }
                            }.frame(height: 160)
                        }
                        TextEditor(text: useCleaned ? $cleaned : $original)
                            .font(.system(size: fontSize))
                            .frame(minHeight: 360)
                        if let player {
                            HStack {
                                Button("Play") { player.play() }
                                Button("Pause") { player.pause() }
                            }
                        }
                        DisclosureGroup("Detected Bible references (ESV)") {
                            if references.isEmpty { Text("No explicit references detected.") }
                            ForEach(references, id: \.self) { ref in
                                if let encoded = ref.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed),
                                   let url = URL(string: "https://www.esv.org/" + encoded + "/") {
                                    Link(ref, destination: url)
                                }
                            }
                            Text("Verify references against scripture.").font(.caption)
                        }
                        DisclosureGroup("Draft sermon outline", isExpanded: $showOutline) {
                            VStack(alignment: .leading, spacing: 10) {
                                ForEach(Array(outline.enumerated()), id: \.offset) { entry in
                                    Text(entry.element.heading).font(.headline)
                                    if !entry.element.detail.isEmpty { Text(String(entry.element.detail.prefix(240))).font(.callout).foregroundStyle(.secondary) }
                                }
                                Text("Draft outline derived from transcript passages. Review before using.").font(.caption).foregroundStyle(.secondary)
                                Button("Collapse outline") { showOutline = false }
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.vertical, 6)
                        }
                        DisclosureGroup("Timestamps and transcript review") {
                            if segments.isEmpty { Text("No timestamp data available.") }
                            ForEach(segments) { segment in
                                HStack {
                                    Button(String(format: "%02d:%02d", Int(segment.start) / 60, Int(segment.start) % 60)) {
                                        player?.seek(to: CMTime(seconds: segment.start, preferredTimescale: 600))
                                        player?.play()
                                    }.disabled(player == nil)
                                    Text(segment.text).lineLimit(2)
                                    if let score = segment.confidence, score < -1 {
                                        Image(systemName: "exclamationmark.triangle").help("Review: low model log probability")
                                    }
                                }
                            }
                        }
                        HStack { Button("Save to library", action: save); Button("Export DOCX") { export("docx") }; Button("Print / PDF") { printTranscript() } }
                        HStack { Button("Export TXT") { export("txt") }; Button("Export Markdown") { export("md") }; Button("Export RTF") { export("rtf") }; Button("Export SRT") { export("srt") }; Button("Export VTT") { export("vtt") } }
                        }
                    }
                } else if page == "Sermon library" {
                    Button("Import web archive…", action: importArchive)
                    Button("Backup library…", action: backupArchive)
                    TextField("Search sermons", text: $search)
                    List(library.filter { search.isEmpty || ($0.title + " " + $0.date + " " + $0.original).localizedCaseInsensitiveContains(search) }) { item in
                        Button(item.date + "  " + item.title) { openSermon(item) }
                    }
                } else {
                    modelPicker
                    HStack {
                        Text("Editor font size")
                        Slider(value: $fontSize, in: 16...24, step: 2)
                        Text("\(Int(fontSize)) pt")
                    }
                    Picker("Appearance", selection: $appearanceMode) {
                        Text("Automatic (System)").tag("system")
                        Text("Light").tag("light")
                        Text("Dark").tag("dark")
                    }
                    Button("Choose default export folder…", action: chooseFolder)
                    Text(folder.isEmpty ? "Ask when exporting" : folder)
                    Button("Reset export folder") { folder = "" }
                    Divider()
                    Text("Software updates").font(.headline)
                    if let controller = updates.controller {
                        Button("Check for updates…") { controller.checkForUpdates(nil) }.disabled(busy)
                        Toggle("Automatically check for updates", isOn: Binding(get: { controller.updater.automaticallyChecksForUpdates }, set: { controller.updater.automaticallyChecksForUpdates = $0 }))
                        Text("Updates are installed only with your approval. Save edits and finish transcription before installing.").font(.caption)
                    } else { Text("Signed update feed is not configured for this build.").foregroundStyle(.secondary) }
                }
                Spacer()
                Text(status).foregroundStyle(.secondary).textSelection(.enabled)
            }.padding(24)
        }.preferredColorScheme(preferredAppearance).onAppear { if let data = try? Data(contentsOf: archiveURL), let items = try? JSONDecoder().decode([Sermon].self, from: data) { library = items } }
    }
    var modelPicker: some View { Picker("Default model", selection: $model) { ForEach(models, id: \.self) { Text($0).tag($0) } }.disabled(busy) }
    func chooseAudio() {
        let p = NSOpenPanel()
        p.canChooseDirectories = false
        p.canChooseFiles = true
        p.allowsMultipleSelection = false
        p.allowedContentTypes = [.audio]
        p.begin { response in
            guard response == .OK, let url = p.url else { return }
            DispatchQueue.main.async {
                guard FileManager.default.isReadableFile(atPath: url.path) else {
                    status = "Cannot read the selected recording."
                    return
                }
                audio = url
                player = AVPlayer(url: url)
                title = url.deletingPathExtension().lastPathComponent
                selectedID = nil
                status = "Recording selected. Ready to transcribe."
            }
        }
    }
    func chooseFolder() {
        let p = NSOpenPanel(); p.canChooseDirectories = true; p.canChooseFiles = false
        if p.runModal() == .OK, let url = p.url { folder = url.path }
    }
    func start() {
        guard let audio else { return }; let chosen = model; busy = true; status = "Preparing model (first use may download and compile it; this can take several minutes)…"
        task = Task { @MainActor in
            defer { busy = false }
            do {
                let kit = try await WhisperKit(WhisperKitConfig(model: resolvedModel(chosen)))
                try Task.checkCancellation()
                status = "Transcribing locally…"
                let results: [TranscriptionResult] = try await kit.transcribe(audioPath: audio.path)
                try Task.checkCancellation()
                segments = results.flatMap { $0.segments }.map { SermonSegment(start: Double($0.start), end: Double($0.end), text: $0.text, confidence: Double($0.avgLogprob)) }
                original = results.map { $0.text }.joined(separator: " ")
                cleaned = original.replacingOccurrences(of: "\\b(um|uh|erm)\\b[ ,]*", with: "", options: [.regularExpression, .caseInsensitive])
                selectedID = nil; page = "Transcript editor"; save()
            } catch { status = "Transcription stopped: " + error.localizedDescription }
        }
    }
    func persist() throws {
        try FileManager.default.createDirectory(at: archiveURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONEncoder().encode(library).write(to: archiveURL, options: .atomic)
    }
    func save() {
        let id = selectedID ?? UUID().uuidString
        let date = String(ISO8601DateFormatter().string(from: sermonDate).prefix(10))
        let item = Sermon(id: id, title: title, date: date, original: original, cleaned: cleaned, segments: segments)
        library.removeAll { $0.id == id }; library.insert(item, at: 0); selectedID = id
        do { try persist(); status = "Saved to library." } catch { status = error.localizedDescription }
    }
    func importArchive() {
        let p = NSOpenPanel()
        guard p.runModal() == .OK, let url = p.url else { return }
        do { let items = try JSONDecoder().decode([Sermon].self, from: Data(contentsOf: url)); library.append(contentsOf: items.filter { item in !library.contains { $0.id == item.id } }); try persist(); status = "Archive imported." } catch { status = error.localizedDescription }
    }
    func export(_ ext: String) {
        let name = (title.isEmpty ? "Sermon" : title).replacingOccurrences(of: "[/:]", with: "_", options: .regularExpression)
        var url: URL
        if folder.isEmpty { let p = NSSavePanel(); p.nameFieldStringValue = name + "." + ext; guard p.runModal() == .OK, let chosen = p.url else { return }; url = chosen }
        else { let dir = URL(fileURLWithPath: folder); url = dir.appendingPathComponent(name + "." + ext); var n = 1; while FileManager.default.fileExists(atPath: url.path) { url = dir.appendingPathComponent(name + " (" + String(n) + ")." + ext); n += 1 } }
        do {
            let text = useCleaned ? cleaned : original
            if ext == "srt" || ext == "vtt" {
                guard !segments.isEmpty else { status = "No timestamps available."; return }
                func stamp(_ t: Double) -> String {
                    let ms = max(0, Int(t * 1000))
                    return String(format: "%02d:%02d:%02d", ms / 3600000, (ms / 60000) % 60, (ms / 1000) % 60) + (ext == "srt" ? "," : ".") + String(format: "%03d", ms % 1000)
                }
                let entries = segments.enumerated().map { i, segment in
                    (ext == "srt" ? String(i + 1) + "\n" : "") + stamp(segment.start) + " --> " + stamp(segment.end) + "\n" + segment.text
                }
                try ((ext == "vtt" ? "WEBVTT\n\n" : "") + entries.joined(separator: "\n\n")).write(to: url, atomically: true, encoding: .utf8)
            } else if ext == "docx" {
                try exportDOCX(to: url, text: text)
            } else if ext == "rtf" {
                let attributed = NSAttributedString(string: text)
                let data = try attributed.data(from: NSRange(location: 0, length: attributed.length), documentAttributes: [.documentType: NSAttributedString.DocumentType.rtf])
                try data.write(to: url, options: .atomic)
            } else {
                try text.write(to: url, atomically: true, encoding: .utf8)
            }
            status = "Exported to " + url.path
        } catch { status = error.localizedDescription }
    }
    private func exportDOCX(to destination: URL, text: String) throws {
        let fm = FileManager.default
        let temp = fm.temporaryDirectory.appendingPathComponent("sermon-docx-" + UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: temp.appendingPathComponent("word"), withIntermediateDirectories: true)
        try fm.createDirectory(at: temp.appendingPathComponent("_rels"), withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: temp) }
        func xml(_ value: String) -> String {
            value.replacingOccurrences(of: "&", with: "&amp;")
                .replacingOccurrences(of: "<", with: "&lt;")
                .replacingOccurrences(of: ">", with: "&gt;")
                .replacingOccurrences(of: "\"", with: "&quot;")
                .replacingOccurrences(of: "'", with: "&apos;")
        }
        func paragraph(_ value: String, heading: Bool = false) -> String {
            let run = "<w:r><w:rPr>" + (heading ? "<w:b/><w:sz w:val=\"32\"/>" : "") + "</w:rPr><w:t xml:space=\"preserve\">" + xml(value) + "</w:t></w:r>"
            return "<w:p>" + run + "</w:p>"
        }
        let lines = text.components(separatedBy: .newlines)
        let body = paragraph(title.isEmpty ? "Sermon" : title, heading: true)
            + paragraph(DateFormatter.localizedString(from: sermonDate, dateStyle: .long, timeStyle: .none))
            + "<w:p/>"
            + lines.map { paragraph($0) }.joined()
        let document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
            + body + "<w:sectPr/></w:body></w:document>"
        let types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"
        let rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"
        try document.write(to: temp.appendingPathComponent("word/document.xml"), atomically: true, encoding: .utf8)
        try types.write(to: temp.appendingPathComponent("[Content_Types].xml"), atomically: true, encoding: .utf8)
        try rels.write(to: temp.appendingPathComponent("_rels/.rels"), atomically: true, encoding: .utf8)
        let archive = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".docx")
        defer { try? fm.removeItem(at: archive) }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        process.arguments = ["-q", "-r", archive.path, "[Content_Types].xml", "_rels", "word"]
        process.currentDirectoryURL = temp
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            throw NSError(domain: "SermonStudio.DOCX", code: Int(process.terminationStatus), userInfo: [NSLocalizedDescriptionKey: "Could not create Word document."])
        }
        if fm.fileExists(atPath: destination.path) { try fm.removeItem(at: destination) }
        try fm.moveItem(at: archive, to: destination)
    }
    func printTranscript() {
        let view = NSTextView(frame: NSRect(x: 0, y: 0, width: 540, height: 800))
        view.string = title + "\n\n" + activeText
        view.font = NSFont.systemFont(ofSize: fontSize)
        view.isEditable = false
        NSPrintOperation(view: view).run()
    }
    func openSermon(_ item: Sermon) {
        selectedID = item.id; title = item.title; original = item.original; cleaned = item.cleaned
        sermonDate = ISO8601DateFormatter().date(from: item.date + "T12:00:00Z") ?? Date()
        segments = item.segments ?? []; audio = nil; player = nil; page = "Transcript editor"
    }
    func clean(_ text: String) -> String {
        text.replacingOccurrences(of: "\\b(um|uh|erm)\\b[ ,]*", with: "", options: [.regularExpression, .caseInsensitive])
    }
    func replaceAll() {
        guard !findText.isEmpty else { return }
        if useCleaned { cleaned = cleaned.replacingOccurrences(of: findText, with: replacement) }
        else { original = original.replacingOccurrences(of: findText, with: replacement) }
    }
    func backupArchive() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "SermonStudio-backup.json"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try JSONEncoder().encode(library).write(to: url, options: .atomic); status = "Archive backup exported." }
        catch { status = "Backup failed: " + error.localizedDescription }
    }

}