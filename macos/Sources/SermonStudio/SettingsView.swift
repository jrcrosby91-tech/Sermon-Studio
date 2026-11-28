import AppKit
import SwiftUI
import SermonCore

struct SettingsView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var transcriber: TranscriptionService

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Card(title: "Appearance and accessibility") { appearance }
                Card(title: "Offline transcription") { transcription }
                Card(title: "Outline generation") { outlines }
                Card(title: "Storage and backup") { storage }
                Card(title: "Sermon utilities") { utilities }
                Card(title: "Updates and about") { about }
            }
            .padding(20)
            .frame(maxWidth: 900, alignment: .leading)
        }
    }

    private var appearance: some View {
        VStack(alignment: .leading, spacing: 12) {
            Picker("Appearance", selection: $model.appearance) {
                Text("System").tag(AppearanceMode.system); Text("Light").tag(AppearanceMode.light); Text("Dark").tag(AppearanceMode.dark)
            }
            .pickerStyle(.segmented).frame(maxWidth: 360)
            Text("Editor text size").font(.subheadline)
            HStack(alignment: .lastTextBaseline, spacing: 10) {
                ForEach(Array(SettingsStore.fontSizes.enumerated()), id: \.offset) { index, size in
                    let names = ["Smallest", "Small", "Standard", "Large", "Largest"]
                    Button { model.fontSize = size } label: {
                        Text("A").font(.custom("Georgia", size: 12 + Double(index) * 5)).frame(minWidth: 34, minHeight: 40)
                    }
                    .buttonStyle(.plain)
                    .padding(4)
                    .background(model.fontSize == size ? Theme.gold.opacity(0.3) : Color.clear, in: RoundedRectangle(cornerRadius: 6))
                    .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(model.fontSize == size ? Theme.gold : Color.secondary.opacity(0.3)))
                    .accessibilityLabel(names[index] + " editor text, \(Int(size)) points")
                    .accessibilityAddTraits(model.fontSize == size ? .isSelected : [])
                }
            }
            Text("For by grace you have been saved through faith. — Ephesians 2:8")
                .font(.custom("Georgia", size: model.fontSize))
                .padding(10).frame(maxWidth: .infinity, alignment: .leading)
                .background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
                .accessibilityLabel("Preview of editor text at \(Int(model.fontSize)) points")
        }
    }

    private var transcription: some View {
        VStack(alignment: .leading, spacing: 10) {
            Picker("Default model", selection: $model.defaultModel) {
                ForEach(ModelCatalog.all) { Text($0.name + " — " + $0.detail).tag($0.id) }
            }
            .frame(maxWidth: 420)
            ForEach(ModelCatalog.all) { info in
                HStack {
                    VStack(alignment: .leading) {
                        Text(info.name).font(.headline)
                        Text(info.detail + " · " + info.approximateSize).font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    switch transcriber.state(info.id) {
                    case .installed(let bytes):
                        Label("Installed · " + ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file), systemImage: "checkmark.seal.fill").foregroundStyle(.green)
                        Button("Delete") { deleteModel(info) }.disabled(transcriber.isTranscribing || transcriber.isDownloading)
                    case .incomplete:
                        Label("Incomplete", systemImage: "exclamationmark.triangle").foregroundStyle(.orange)
                        Button("Delete") { deleteModel(info) }.disabled(transcriber.isDownloading)
                    case .missing:
                        if transcriber.downloadingModel == info.id {
                            ProgressView(value: transcriber.downloadFraction).frame(width: 140)
                            Text("\(Int(transcriber.downloadFraction * 100))%").monospacedDigit()
                            Button("Cancel") { transcriber.cancelDownload() }
                        } else {
                            Button("Download") { transcriber.startDownload(info.id) { model.status = $0 } }.disabled(transcriber.isDownloading)
                        }
                    }
                }
                .padding(.vertical, 2)
            }
            HStack {
                Button("Open Models Folder") {
                    try? FileManager.default.createDirectory(at: transcriber.modelsFolder, withIntermediateDirectories: true)
                    NSWorkspace.shared.open(transcriber.modelsFolder)
                }
                Button("Recheck Models") { transcriber.refresh() }
            }
            DisclosureGroup("Advanced transcription settings") {
                Toggle("Biblical vocabulary assistance", isOn: $model.biblicalAssistance)
                TextField("Names and vocabulary", text: $model.vocabulary, axis: .vertical).lineLimit(2...6).textFieldStyle(.roundedBorder)
                Button("Restore Default Vocabulary") { model.vocabulary = AppModel.defaultVocabulary }
                Text("Models are compiled for Apple Silicon and run on the Neural Engine. Recordings are processed in 10-minute sections to limit memory use.").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func deleteModel(_ info: WhisperModelInfo) {
        let alert = NSAlert()
        alert.messageText = "Delete the " + info.name + " model?"
        alert.informativeText = "It can be downloaded again later."
        alert.addButton(withTitle: "Delete"); alert.addButton(withTitle: "Cancel")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do { try transcriber.delete(info.id); model.status = info.name + " model deleted." } catch { model.status = "Could not delete the model: " + error.localizedDescription }
    }

    private var outlines: some View {
        VStack(alignment: .leading, spacing: 10) {
            Picker("Default generator", selection: $model.defaultGenerator) {
                Text("Original (offline, built in)").tag(OutlineGeneratorKind.original)
                Text("Ollama (local AI)").tag(OutlineGeneratorKind.ollama)
            }
            .frame(maxWidth: 420)
            Text("Each sermon starts with the default; you can switch generators for one sermon in the outline workspace.").font(.caption).foregroundStyle(.secondary)
            HStack {
                TextField("Ollama model", text: $model.ollamaModel).textFieldStyle(.roundedBorder).frame(maxWidth: 260).onSubmit { model.commitOllamaModel() }
                Button("Set Up Ollama") { model.commitOllamaModel(); model.setupOllama() }.disabled(model.ollamaBusy)
                if model.ollamaBusy { ProgressView().controlSize(.small); Button("Cancel") { model.cancelOllamaSetup() } }
            }
            Text(model.ollamaStatus).font(.callout).foregroundStyle(.secondary)
            Text("Ollama only connects to 127.0.0.1 on this Mac. Cloud models are not allowed. The original generator needs no installation or internet.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }

    private var storage: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Library: ").bold() + Text(model.store.url.path).font(.caption.monospaced())
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([model.store.url]) }
            }
            HStack {
                Text("Default export folder: ").bold() + Text(model.exportFolder?.path ?? "Ask each time")
                Button("Choose…") { model.chooseExportFolder() }
                if model.exportFolder != nil { Button("Ask Each Time") { model.resetExportFolder() } }
            }
            Text("With a default folder, exports are given new names (“Title (2)”) instead of replacing files.").font(.caption).foregroundStyle(.secondary)
            HStack {
                Button("Back Up Library…") { model.backup() }
                Button("Restore or Import Archive…") { model.restore() }
                Button("Export All Sermons…") { model.bulkExport(model.library, label: "Library") }
            }
            Text("Archives open in Sermon Studio on Mac, Windows and the web. Restoring never overwrites a sermon already in your library. A backup of the library as it was when the app opened is kept at " + model.store.sessionBackupURL.lastPathComponent + ".")
                .font(.caption).foregroundStyle(.secondary)
        }
    }

    private var utilities: some View {
        HStack {
            Button("Transcript Comparison") { model.page = .transcript }
            Button("Timestamps") { model.page = .transcript }
            Button("Export Transcript as SRT") { model.export(.srt, outline: false) }
            Button("Export Outline as Word") { model.export(.docx, outline: true) }
        }
    }

    private var about: some View {
        VStack(alignment: .leading, spacing: 8) {
            let info = Bundle.main.infoDictionary ?? [:]
            Text("Version \(info["CFBundleShortVersionString"] as? String ?? "development") (build \(info["CFBundleVersion"] as? String ?? "–"))")
            UpdateControls()
            Text("Sermon Studio keeps sermons, recordings and AI processing on this Mac. Scripture previews request only the reference from bible-api.com.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }
}
