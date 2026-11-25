import SwiftUI
import AppKit
import AVKit
import WhisperKit
import Sparkle

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
}
@main struct StudioApp: App {
    var body: some Scene { WindowGroup("Sermon Studio 3.0 Preview") { StudioView() }.defaultSize(width: 1100, height: 720) }
}
struct StudioView: View {
    @AppStorage("defaultModel") private var model = "small.en"
    @AppStorage("exportFolder") private var folder = ""
    @State private var page: String? = "New sermon"
    @StateObject private var updates = StudioUpdates()
    @State private var audio: URL?
    @State private var player = AVPlayer()
    @State private var title = ""
    @State private var original = ""
    @State private var cleaned = ""
    @State private var useCleaned = false
    @State private var status = "Choose an audio recording."
    @State private var busy = false
    @State private var library: [Sermon] = []
    @State private var selectedID: String?
    @State private var task: Task<Void, Never>?
    private let models = ["base.en", "small.en", "medium.en", "large-v3-turbo"]
    private var archiveURL: URL { FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("SermonStudio/archive.json") }
    var body: some View {
        NavigationSplitView {
            List(["New sermon", "Transcript editor", "Sermon library", "Settings"], id: \.self, selection: $page) { Text($0).tag($0) }
        } detail: {
            VStack(alignment: .leading, spacing: 16) {
                Text(page ?? "New sermon").font(.largeTitle.bold())
                if page == "New sermon" {
                    Button("Choose audio recording…", action: chooseAudio)
                    Text(audio?.lastPathComponent ?? "No recording selected")
                    modelPicker
                    Button("Transcribe locally", action: start).disabled(audio == nil || busy)
                    if busy { ProgressView(); Button("Cancel") { task?.cancel() } }
                } else if page == "Transcript editor" {
                    TextField("Sermon title", text: $title)
                    Picker("Transcript", selection: $useCleaned) { Text("Original").tag(false); Text("Cleaned").tag(true) }.pickerStyle(.segmented)
                    TextEditor(text: useCleaned ? $cleaned : $original).font(.system(size: 17))
                    HStack { Button("Save to library", action: save); Button("Export TXT") { export("txt") }; Button("Export Markdown") { export("md") } }
                } else if page == "Sermon library" {
                    Button("Import web archive…", action: importArchive)
                    List(library) { item in Button(item.date + "  " + item.title) { selectedID = item.id; title = item.title; original = item.original; cleaned = item.cleaned; audio = nil; player.replaceCurrentItem(with: nil); page = "Transcript editor" } }
                } else {
                    modelPicker
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
                if audio != nil { VideoPlayer(player: player).frame(height: 65) }
                Text(status).foregroundStyle(.secondary).textSelection(.enabled)
            }.padding(24)
        }.onAppear { if let data = try? Data(contentsOf: archiveURL), let items = try? JSONDecoder().decode([Sermon].self, from: data) { library = items } }
    }
    var modelPicker: some View { Picker("Default model", selection: $model) { ForEach(models, id: \.self) { Text($0).tag($0) } }.disabled(busy) }
    func chooseAudio() {
        let p = NSOpenPanel(); p.canChooseDirectories = false; p.allowsMultipleSelection = false
        if p.runModal() == .OK, let url = p.url { audio = url; player.replaceCurrentItem(with: AVPlayerItem(url: url)); title = url.deletingPathExtension().lastPathComponent; selectedID = nil }
    }
    func chooseFolder() {
        let p = NSOpenPanel(); p.canChooseDirectories = true; p.canChooseFiles = false
        if p.runModal() == .OK, let url = p.url { folder = url.path }
    }
    func start() {
        guard let audio else { return }; let chosen = model; busy = true; status = "Preparing model (first use may require a download)…"
        task = Task { @MainActor in
            defer { busy = false }
            do {
                let kit = try await WhisperKit(WhisperKitConfig(model: chosen))
                try Task.checkCancellation()
                status = "Transcribing locally…"
                let results: [TranscriptionResult] = try await kit.transcribe(audioPath: audio.path)
                try Task.checkCancellation()
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
        let date = library.first(where: { $0.id == id })?.date ?? String(ISO8601DateFormatter().string(from: Date()).prefix(10))
        let item = Sermon(id: id, title: title, date: date, original: original, cleaned: cleaned)
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
        do { try (useCleaned ? cleaned : original).write(to: url, atomically: true, encoding: .utf8); status = "Exported to " + url.path } catch { status = error.localizedDescription }
    }
}
