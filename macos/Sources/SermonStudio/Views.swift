import AppKit
import SwiftUI
import UniformTypeIdentifiers
import SermonCore

struct StudioView: View {
    @ObservedObject var model: AppModel
    @State private var columns: NavigationSplitViewVisibility = .all
    @State private var previousPage: Page = .newSermon

    var body: some View {
        NavigationSplitView(columnVisibility: $columns) {
            List(Page.allCases, selection: Binding<Page?>(get: { model.page }, set: { if let page = $0 { select(page) } })) { page in
                Label(page.rawValue, systemImage: page.symbol).tag(page)
            }
            .listStyle(.sidebar)
            .navigationSplitViewColumnWidth(min: 190, ideal: 210)
            .safeAreaInset(edge: .bottom) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Sermon Studio").font(.system(.headline, design: .serif)).foregroundStyle(Theme.gold)
                    Text("\(model.library.count) sermons").font(.caption).foregroundStyle(.secondary)
                }
                .padding(12).frame(maxWidth: .infinity, alignment: .leading)
            }
        } detail: {
            VStack(spacing: 0) {
                if !model.focusMode { header }
                Group {
                    switch model.page {
                    case .newSermon: NewSermonView(model: model, audio: model.audio, transcriber: model.transcriber)
                    case .dashboard: DashboardView(model: model)
                    case .transcript: TranscriptView(model: model, audio: model.audio)
                    case .outlines: OutlineView(model: model)
                    case .library: LibraryView(model: model)
                    case .settings: SettingsView(model: model, transcriber: model.transcriber)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                statusBar
            }
            .onDrop(of: [.fileURL], isTargeted: nil) { providers in
                guard let provider = providers.first else { return false }
                _ = provider.loadObject(ofClass: URL.self) { url, _ in
                    guard let url else { return }
                    DispatchQueue.main.async {
                        if url.pathExtension.lowercased() == "json" { model.status = "To import an archive, use Sermon Library › Restore." }
                        else { model.importAudio(url); model.page = .newSermon }
                    }
                }
                return true
            }
        }
        .tint(Theme.accent)
        .onChange(of: model.focusMode) { _, focus in withAnimation { columns = focus ? .detailOnly : .all } }
        .onAppear { applyAppearance() }
        .onChange(of: model.appearance) { _, _ in applyAppearance() }
    }

    private func select(_ page: Page) {
        // Choosing New Sermon while a saved sermon is open starts a fresh one (after saving).
        if page == .newSermon && !model.isNewDraft { model.newSermon(); return }
        model.outlineEditor.flush()
        model.page = page
    }

    private func applyAppearance() {
        switch model.appearance {
        case .system: NSApp.appearance = nil
        case .light: NSApp.appearance = NSAppearance(named: .aqua)
        case .dark: NSApp.appearance = NSAppearance(named: .darkAqua)
        }
    }

    private var header: some View {
        HStack(spacing: 12) {
            Image(systemName: model.page.symbol)
                .font(.title2).foregroundStyle(Theme.gold)
                .frame(width: 40, height: 40)
                .background(Theme.navy.opacity(0.9), in: RoundedRectangle(cornerRadius: 10))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(model.page.rawValue).font(.system(.title, design: .serif).bold()).foregroundStyle(Theme.accent)
                Text(model.isNewDraft ? (model.draft.title.isEmpty ? "New sermon" : model.draft.title + " · not yet saved") : model.draft.displayTitle)
                    .font(.callout).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            SaveIndicator(state: model.saveState, isNew: model.isNewDraft)
            Button { model.save() } label: { Label("Save", systemImage: "square.and.arrow.down") }
                .help("Save to library (⌘S)")
        }
        .padding(.horizontal, 20).padding(.vertical, 12)
        .overlay(alignment: .bottom) { Rectangle().fill(Theme.gold.opacity(0.35)).frame(height: 1) }
    }

    private var statusBar: some View {
        HStack(spacing: 10) {
            TranscriptionProgressBadge(transcriber: model.transcriber)
            Text(model.status).lineLimit(2).textSelection(.enabled).foregroundStyle(.secondary)
            Spacer()
            if let url = model.lastExport {
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([url]) }
                ShareLink(item: url) { Label("Share", systemImage: "square.and.arrow.up") }
            }
        }
        .font(.callout)
        .padding(.horizontal, 16).padding(.vertical, 8)
        .background(.bar)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Status")
    }
}

struct TranscriptionProgressBadge: View {
    @ObservedObject var transcriber: TranscriptionService
    var body: some View {
        if transcriber.isTranscribing { ProgressView(value: transcriber.progress).frame(width: 120).accessibilityLabel("Transcription progress") }
        else if transcriber.downloadingModel != nil { ProgressView(value: transcriber.downloadFraction).frame(width: 120).accessibilityLabel("Model download progress") }
    }
}

struct SaveIndicator: View {
    var state: SaveState
    var isNew: Bool
    var body: some View {
        let failed: Bool = { if case .failed = state { return true }; return false }()
        Label(state.label, systemImage: failed ? "exclamationmark.triangle.fill" : isNew ? "circle.dashed" : state == .pending ? "arrow.triangle.2.circlepath" : "checkmark.circle")
            .font(.caption)
            .foregroundStyle(failed ? Color.red : Color.secondary)
            .help(failed ? "Saving failed. Your text is still here; fix the problem and save again." : "")
            .accessibilityLabel("Save status: " + state.label)
    }
}

struct MetadataEditor: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 8) {
            GridRow {
                TextField("Title", text: $model.draft.title).font(.system(.title3, design: .serif)).gridCellColumns(2)
                DatePicker("Date", selection: Binding(get: { ArchiveDates.dayDate(model.draft.date) ?? Date() }, set: { model.draft.date = ArchiveDates.day($0) }), displayedComponents: .date)
                Toggle(isOn: $model.draft.favorite) { Label("Favorite", systemImage: model.draft.favorite ? "star.fill" : "star") }
            }
            GridRow {
                TextField("Speaker", text: $model.draft.speaker)
                TextField("Primary scripture", text: $model.draft.primaryScripture)
                TextField("Series", text: $model.draft.series)
                TextField("Collection", text: $model.draft.collection)
            }
            GridRow { TextField("Tags (comma separated)", text: $model.draft.tags).gridCellColumns(4) }
        }
        .textFieldStyle(.roundedBorder)
    }
}

// MARK: New sermon

struct NewSermonView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var audio: AudioController
    @ObservedObject var transcriber: TranscriptionService
    @State private var dropTargeted = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Card(title: "1. Recording") { recording }
                Card(title: "2. Offline transcription") { transcription }
                Card(title: "3. Sermon details") { MetadataEditor(model: model) }
            }
            .padding(20)
        }
    }

    private var dropZone: some View {
        VStack(spacing: 10) {
            Image(systemName: "waveform.badge.plus").font(.system(size: 34)).foregroundStyle(Theme.gold)
            Text(audio.url == nil ? "Drop a sermon recording here or choose one." : "Recording ready.").font(.headline)
            HStack {
                Button("Choose Recording…") { model.chooseAudio() }
                Button("Write Without a Recording") { model.page = .transcript }
            }
        }
        .frame(maxWidth: .infinity)
        .padding(18)
    }

    private var dropBorder: some View {
        let color: Color = dropTargeted ? Theme.gold : Color.secondary.opacity(0.4)
        return RoundedRectangle(cornerRadius: 10).strokeBorder(color, style: StrokeStyle(lineWidth: 1.5, dash: [6]))
    }

    @ViewBuilder private var recording: some View {
        dropZone
            .background(dropBorder)
            .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
                _ = providers.first?.loadObject(ofClass: URL.self) { url, _ in
                    if let url { DispatchQueue.main.async { model.importAudio(url) } }
                }
                return true
            }
        if audio.url != nil { PlayerBar(audio: audio, accent: Theme.gold) }
    }

    private func modelLabel(_ info: WhisperModelInfo) -> String {
        let installed = transcriber.state(info.id).isInstalled
        let suffix: String = installed ? "  ✓ installed" : "  (" + info.approximateSize + " download)"
        return info.name + " — " + info.detail + suffix
    }

    @ViewBuilder private var transcription: some View {
        Picker("Quality", selection: $model.defaultModel) {
            ForEach(ModelCatalog.all) { info in Text(modelLabel(info)).tag(info.id) }
        }
        .disabled(transcriber.isTranscribing)
        DisclosureGroup("Biblical vocabulary assistance") {
            Toggle("Guide spelling of biblical names and terms", isOn: $model.biblicalAssistance)
            TextField("Names and vocabulary", text: $model.vocabulary, axis: .vertical).lineLimit(2...5).textFieldStyle(.roundedBorder)
            Text("These words are given to Whisper as a prompt. Keep the list short and specific to this sermon for best results.").font(.caption).foregroundStyle(.secondary)
        }
        HStack {
            Button(transcriber.isTranscribing ? "Transcribing…" : "Transcribe on This Mac") { model.transcribe() }
                .buttonStyle(ProminentStyle())
                .disabled(audio.url == nil || transcriber.isTranscribing)
            if transcriber.isTranscribing {
                ProgressView(value: transcriber.progress).frame(maxWidth: 260)
                Button("Cancel") { model.cancelTranscription() }
            }
        }
        Text("Audio never leaves your Mac. The first use of a model downloads it once; transcription then works offline using the Neural Engine.")
            .font(.caption).foregroundStyle(.secondary)
    }
}

// MARK: Dashboard

struct DashboardView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        let stats = LibraryQuery.stats(model.library)
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                HStack(spacing: 12) {
                    stat("Sermons", stats.sermons, "books.vertical")
                    stat("Outlines", stats.outlines, "text.book.closed")
                    stat("Favorites", stats.favorites, "star")
                    stat("Series", stats.series, "square.stack")
                }
                HStack(spacing: 12) {
                    Button { model.newSermon() } label: { Label("New Sermon", systemImage: "plus") }.buttonStyle(ProminentStyle())
                    Button { model.page = .library } label: { Label("Open Library", systemImage: "books.vertical") }
                    Button { model.page = .outlines } label: { Label("Outline Workspace", systemImage: "text.book.closed") }
                }
                Card(title: "Recently opened") {
                    let recent = LibraryQuery.recent(model.library)
                    if recent.isEmpty { Text("Sermons you open appear here.").foregroundStyle(.secondary) }
                    ForEach(recent) { record in
                        Button { model.open(record.id) } label: {
                            HStack { Text(record.displayTitle).font(.system(.body, design: .serif)); Spacer(); Text(record.date).foregroundStyle(.secondary) }
                        }.buttonStyle(.plain)
                        Divider()
                    }
                }
            }
            .padding(20)
        }
    }

    private func stat(_ title: String, _ value: Int, _ symbol: String) -> some View {
        Card(title: nil) {
            Label(title, systemImage: symbol).font(.caption).foregroundStyle(.secondary)
            Text("\(value)").font(.system(size: 30, weight: .semibold, design: .serif)).foregroundStyle(Theme.accent)
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: Transcript editor

struct TranscriptView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var audio: AudioController
    @State private var findText = ""
    @State private var replaceText = ""
    @State private var matchCase = false
    @State private var current = -1
    @State private var focus: TextFocusRequest?
    @State private var selection = NSRange(location: 0, length: 0)
    @State private var compare = false
    @State private var showDetails = true
    @State private var preview: ScriptureReference?
    @FocusState private var findFocused: Bool

    private var text: Binding<String> { model.editingCleaned ? $model.draft.cleaned : $model.draft.original }
    private var matches: [NSRange] { TranscriptTools.matches(in: text.wrappedValue, of: findText, matchCase: matchCase) }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if !model.focusMode {
                DisclosureGroup("Sermon details", isExpanded: $showDetails) { MetadataEditor(model: model).padding(.top, 6) }
                toolbar
                findBar
            }
            if compare {
                HStack(spacing: 8) {
                    VStack(alignment: .leading) { Text("Original").font(.caption.bold()); ScrollView { Text(model.draft.original).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading) } }
                    Divider()
                    VStack(alignment: .leading) { Text("Cleaned").font(.caption.bold()); ScrollView { Text(model.draft.cleaned.isEmpty ? "No cleaned version yet." : model.draft.cleaned).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading) } }
                }
                .frame(height: 170).padding(8).background(Theme.panel, in: RoundedRectangle(cornerRadius: 8))
            }
            HStack(alignment: .top, spacing: 12) {
                PlainTextEditor(text: text, fontSize: model.fontSize, highlights: matches, focus: focus,
                                accessibilityLabel: model.editingCleaned ? "Cleaned transcript" : "Original transcript") { selection = $0 }
                    .clipShape(RoundedRectangle(cornerRadius: 8))
                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.secondary.opacity(0.2)))
                if !model.focusMode { sidePanel.frame(width: 300) }
            }
            if audio.url != nil && !model.focusMode { PlayerBar(audio: audio, accent: Theme.gold) }
        }
        .padding(16)
        .onChange(of: findText) { _, _ in current = -1 }
    }

    private var toolbar: some View {
        HStack(spacing: 10) {
            Picker("Version", selection: $model.editingCleaned) { Text("Original").tag(false); Text("Cleaned").tag(true) }
                .pickerStyle(.segmented).frame(width: 200).labelsHidden()
            Button("Clean Transcript") { model.cleanTranscript() }.help("Creates a cleaned copy without filler words. The original is never changed.")
            Toggle("Compare", isOn: $compare).toggleStyle(.button)
            Spacer()
            Text("\(TranscriptTools.wordCount(text.wrappedValue)) words").foregroundStyle(.secondary).monospacedDigit()
            Menu("Export") {
                ForEach(ExportFormat.transcript) { format in Button(format.rawValue) { model.export(format, outline: false) } }
                Divider()
                Button("Print…") { model.printDocument(outline: false) }
            }.frame(width: 90)
        }
    }

    private var findBar: some View {
        HStack(spacing: 8) {
            TextField("Find", text: $findText).textFieldStyle(.roundedBorder).frame(maxWidth: 220).focused($findFocused).onSubmit { step(false) }
            Toggle("Match case", isOn: $matchCase).toggleStyle(.checkbox)
            Button { step(true) } label: { Image(systemName: "chevron.up") }.help("Previous match").accessibilityLabel("Previous match").disabled(matches.isEmpty)
            Button { step(false) } label: { Image(systemName: "chevron.down") }.help("Next match").accessibilityLabel("Next match").disabled(matches.isEmpty)
            Text(findText.isEmpty ? "" : matches.isEmpty ? "No matches" : (current >= 0 ? "\(current + 1) of \(matches.count)" : "\(matches.count) matches"))
                .font(.caption).foregroundStyle(.secondary).frame(width: 90, alignment: .leading)
            TextField("Replace with", text: $replaceText).textFieldStyle(.roundedBorder).frame(maxWidth: 200)
            Button("Replace") { replaceCurrent() }.disabled(matches.isEmpty)
            Button("Replace All") {
                let result = TranscriptTools.replaceAll(in: text.wrappedValue, of: findText, with: replaceText, matchCase: matchCase)
                text.wrappedValue = result.text
                model.status = "Replaced \(result.count) occurrence(s). Use Edit › Undo in the editor to reverse individual edits."
            }.disabled(matches.isEmpty)
        }
    }

    private func step(_ backwards: Bool) {
        let ranges = matches
        let anchor = backwards ? selection.location : selection.location + selection.length
        guard let index = TranscriptTools.nextMatch(ranges, after: anchor, backwards: backwards) else { return }
        current = index
        focus = TextFocusRequest(range: ranges[index])
    }

    private func replaceCurrent() {
        let ranges = matches
        guard let index = ranges.firstIndex(where: { $0 == selection }) ?? (current >= 0 && current < ranges.count ? current : nil) else { step(false); return }
        text.wrappedValue = TranscriptTools.replace(in: text.wrappedValue, range: ranges[index], with: replaceText)
        let next = TranscriptTools.matches(in: text.wrappedValue, of: findText, matchCase: matchCase)
        if let nextIndex = TranscriptTools.nextMatch(next, after: ranges[index].location + (replaceText as NSString).length, backwards: false) {
            current = nextIndex; focus = TextFocusRequest(range: next[nextIndex])
        } else { current = -1 }
    }

    private var sidePanel: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Card(title: "Scripture references") {
                    let references = uniqueReferences
                    if references.isEmpty { Text("No references detected yet.").font(.callout).foregroundStyle(.secondary) }
                    ForEach(references, id: \.canonical) { reference in
                        HStack {
                            Link(reference.canonical, destination: ScriptureReferences.link(reference)).help("Open on esv.org")
                            Spacer()
                            Button("KJV") { preview = reference }.buttonStyle(.borderless).help("Preview the King James Version text")
                                .accessibilityLabel("Preview " + reference.canonical + " in the King James Version")
                        }
                    }
                    Text("Links open esv.org. Previews show the public-domain King James Version.").font(.caption2).foregroundStyle(.secondary)
                }
                .popover(item: $preview) { reference in PassagePreview(reference: reference) }
                Card(title: "Timestamps") {
                    let segments = model.draft.segments ?? []
                    if segments.isEmpty { Text("Transcribe a recording to get timestamps.").font(.callout).foregroundStyle(.secondary) }
                    else {
                        Toggle("Follow playback", isOn: $model.followPlayback).disabled(audio.url == nil)
                        TimestampList(segments: segments, audio: audio, follow: model.followPlayback) { segment in
                            if audio.url != nil { audio.seek(to: segment.start) }
                            else { model.status = "Load the original recording on the New Sermon page to play from a timestamp." }
                            if let range = TranscriptTools.locate(segment.text, in: text.wrappedValue) { focus = TextFocusRequest(range: range) }
                        }
                        .frame(height: 280)
                    }
                }
            }
        }
    }

    private var uniqueReferences: [ScriptureReference] {
        var seen = Set<String>()
        return ScriptureReferences.find(text.wrappedValue + "\n" + model.draft.outline).filter { seen.insert($0.canonical.lowercased()).inserted }
    }
}

struct PassagePreview: View {
    var reference: ScriptureReference
    @State private var text: String?
    @State private var failed = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(reference.canonical).font(.system(.headline, design: .serif))
            Text(ScriptureReferences.previewTranslation).font(.caption).foregroundStyle(.secondary)
            if let text { ScrollView { Text(text).font(.system(.body, design: .serif)).textSelection(.enabled) }.frame(maxHeight: 220) }
            else if failed { Text("Preview unavailable offline. The link opens the passage on esv.org.").foregroundStyle(.secondary) }
            else { ProgressView() }
            Link("Open on esv.org", destination: ScriptureReferences.link(reference))
        }
        .padding(14).frame(width: 340)
        .task {
            guard let url = ScriptureReferences.previewURL(reference) else { failed = true; return }
            do {
                let (data, _) = try await URLSession.shared.data(from: url)
                let object = try JSONSerialization.jsonObject(with: data) as? [String: Any]
                if let passage = object?["text"] as? String, !passage.isEmpty { text = passage.trimmingCharacters(in: .whitespacesAndNewlines) } else { failed = true }
            } catch { failed = true }
        }
    }
}

struct TimestampList: View {
    var segments: [SermonSegment]
    @ObservedObject var audio: AudioController
    var follow: Bool
    var onSelect: (SermonSegment) -> Void

    var body: some View {
        let active = audio.url == nil ? nil : TranscriptTools.activeSegmentIndex(segments, at: audio.currentTime)
        ScrollViewReader { proxy in
            List(Array(segments.enumerated()), id: \.offset) { index, segment in
                Button { onSelect(segment) } label: {
                    HStack(alignment: .top, spacing: 6) {
                        Text(TranscriptTools.timestamp(segment.start)).monospacedDigit().foregroundStyle(Theme.gold)
                        Text(segment.text).lineLimit(3)
                        if let score = segment.confidence, score < -1 {
                            Image(systemName: "exclamationmark.triangle").foregroundStyle(.orange).help("Low confidence: review this passage")
                                .accessibilityLabel("Low confidence")
                        }
                    }
                }
                .buttonStyle(.plain)
                .listRowBackground(index == active ? Theme.gold.opacity(0.22) : Color.clear)
                .id(index)
            }
            .onChange(of: active) { _, newValue in
                if follow, let newValue { withAnimation { proxy.scrollTo(newValue, anchor: .center) } }
            }
        }
    }
}

// MARK: Outline workspace

struct OutlineView: View {
    @ObservedObject var model: AppModel
    @State private var lookup = ""
    @State private var transcriptFocus: TextFocusRequest?
    @State private var dragStart: Double?

    var body: some View {
        GeometryReader { geometry in
            HStack(spacing: 0) {
                if !model.focusMode {
                    transcriptPane.frame(width: max(220, geometry.size.width * model.workspaceSplit))
                    divider(total: geometry.size.width)
                }
                outlinePane.frame(maxWidth: .infinity)
            }
        }
        .padding(12)
    }

    private func divider(total: CGFloat) -> some View {
        Rectangle().fill(Theme.gold.opacity(0.35)).frame(width: 5)
            .contentShape(Rectangle())
            .onHover { inside in if inside { NSCursor.resizeLeftRight.push() } else { NSCursor.pop() } }
            .gesture(DragGesture().onChanged { value in
                if dragStart == nil { dragStart = model.workspaceSplit }
                model.workspaceSplit = SettingsStore.clampSplit((dragStart ?? 0.5) + Double(value.translation.width / max(1, total)))
            }.onEnded { _ in dragStart = nil })
            .accessibilityElement()
            .accessibilityLabel("Workspace divider")
            .accessibilityValue("\(Int(model.workspaceSplit * 100)) percent transcript")
            .accessibilityAdjustableAction { model.workspaceSplit = SettingsStore.clampSplit(model.workspaceSplit + ($0 == .increment ? 0.05 : -0.05)) }
    }

    private var transcriptPane: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Transcript").font(Theme.serif(.headline)).foregroundStyle(Theme.accent)
            HStack {
                TextField("Find an outline phrase in the transcript", text: $lookup).textFieldStyle(.roundedBorder).onSubmit(findSource)
                Button("Find Source", action: findSource)
            }
            PlainTextEditor(text: model.editingCleaned ? $model.draft.cleaned : $model.draft.original, fontSize: model.fontSize, focus: transcriptFocus, accessibilityLabel: "Transcript")
                .clipShape(RoundedRectangle(cornerRadius: 8))
            if !model.outlineSource.isEmpty && model.outlineSource != model.activeTranscript.trimmingCharacters(in: .whitespacesAndNewlines) {
                Label("The transcript changed after this outline was generated.", systemImage: "info.circle").font(.caption).foregroundStyle(.orange)
            }
        }
        .padding(.trailing, 8)
    }

    private func findSource() {
        if let range = TranscriptTools.locate(lookup, in: model.activeTranscript) { transcriptFocus = TextFocusRequest(range: range); model.status = "Source passage found." }
        else { model.status = "No matching transcript passage. Try a shorter phrase." }
    }

    private var outlinePane: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Picker("Generator", selection: $model.generator) {
                    Text("Original (offline)").tag(OutlineGeneratorKind.original)
                    Text("Ollama (local AI)").tag(OutlineGeneratorKind.ollama)
                }
                .frame(width: 230)
                .help("Applies to this sermon only. Change the default in Settings.")
                Button(model.generating ? "Generating…" : "Generate Outline") { model.generateOutline() }.buttonStyle(ProminentStyle()).disabled(model.generating)
                if model.generating { ProgressView().controlSize(.small); Button("Cancel") { model.cancelGeneration() } }
                Spacer()
                Picker("Mode", selection: Binding(get: { model.outlineMode }, set: { model.outlineEditor.flush(); model.outlineMode = $0 })) {
                    Text("Visual").tag("visual"); Text("Markdown").tag("markdown"); Text("Reading").tag("reading")
                }
                .pickerStyle(.segmented).frame(width: 240).labelsHidden()
                Menu("Export") {
                    ForEach(ExportFormat.outline) { format in Button(format.rawValue) { model.export(format, outline: true) } }
                    Divider()
                    Button("Print…") { model.printDocument(outline: true) }
                    Button("Copy Markdown") { model.outlineEditor.flush(); NSPasteboard.general.clearContents(); NSPasteboard.general.setString(model.draft.outline, forType: .string) }
                }.frame(width: 90)
            }
            if model.outlineMode == "visual" { formatBar }
            Group {
                switch model.outlineMode {
                case "markdown": PlainTextEditor(text: $model.draft.outline, fontSize: model.fontSize, accessibilityLabel: "Outline Markdown source")
                case "reading": OutlineReadingView(markdown: model.draft.outline, fontSize: model.fontSize, accent: Theme.accent)
                default: RichOutlineEditor(markdown: $model.draft.outline, baseSize: model.fontSize, controller: model.outlineEditor)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.secondary.opacity(0.2)))
            HStack {
                Text("\(TranscriptTools.wordCount(DocumentWriters.plainText(model.draft.outline))) words").monospacedDigit()
                Spacer()
                Text(model.isNewDraft ? "Save the sermon to keep this outline." : "Outline changes save automatically.")
            }
            .font(.caption).foregroundStyle(.secondary)
        }
        .padding(.leading, 8)
    }

    private var formatBar: some View {
        let editor = model.outlineEditor
        return HStack(spacing: 6) {
            Button("Heading") { editor.heading() }
            Button("Subheading") { editor.subheading() }
            Button("Normal") { editor.normal() }
            Divider().frame(height: 16)
            Button { editor.toggle(.boldFontMask) } label: { Image(systemName: "bold") }.help("Bold (⌘B)").accessibilityLabel("Bold")
            Button { editor.toggle(.italicFontMask) } label: { Image(systemName: "italic") }.help("Italic (⌘I)").accessibilityLabel("Italic")
            Button { editor.prefix(OutlineMarkdown.visualBullet) } label: { Image(systemName: "list.bullet") }.help("Bulleted list").accessibilityLabel("Bulleted list")
            Button { editor.prefix("", numbered: true) } label: { Image(systemName: "list.number") }.help("Numbered list").accessibilityLabel("Numbered list")
            Button { editor.prefix(OutlineMarkdown.visualQuote) } label: { Image(systemName: "text.quote") }.help("Scripture quotation").accessibilityLabel("Scripture quotation")
            Divider().frame(height: 16)
            Button { editor.undo() } label: { Image(systemName: "arrow.uturn.backward") }.help("Undo (⌘Z)").accessibilityLabel("Undo")
            Button { editor.redo() } label: { Image(systemName: "arrow.uturn.forward") }.help("Redo (⇧⌘Z)").accessibilityLabel("Redo")
        }
        .buttonStyle(.bordered).controlSize(.small)
    }
}

// MARK: Library

struct LibraryView: View {
    @ObservedObject var model: AppModel
    @State private var filter = LibraryFilter()
    @State private var sort: LibrarySort = .newest
    @State private var selection = Set<String>()
    @State private var showFilters = false
    @State private var useFrom = false
    @State private var useTo = false
    @State private var from = Date()
    @State private var to = Date()

    private var results: [SermonRecord] {
        var effective = filter
        effective.from = useFrom ? from : nil
        effective.to = useTo ? to : nil
        return LibraryQuery.apply(model.library, filter: effective, sort: sort)
    }

    var body: some View {
        let shown = results
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                TextField("Search titles, details, transcripts and outlines", text: $filter.search).textFieldStyle(.roundedBorder)
                Toggle(isOn: $filter.favoritesOnly) { Label("Favorites", systemImage: "star") }.toggleStyle(.button)
                Toggle(isOn: $showFilters) { Label("Filters", systemImage: "line.3.horizontal.decrease.circle") }.toggleStyle(.button)
                Picker("Sort", selection: $sort) { ForEach(LibrarySort.allCases) { Text($0.rawValue).tag($0) } }.frame(width: 190)
            }
            if showFilters {
                Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 6) {
                    GridRow {
                        suggestField("Speaker", text: $filter.speaker, values: LibraryQuery.values(model.library, \.speaker))
                        suggestField("Tag", text: $filter.tag, values: Array(Set(model.library.flatMap(\.tagList))).sorted())
                        suggestField("Series", text: $filter.series, values: LibraryQuery.values(model.library, \.series))
                        suggestField("Collection", text: $filter.collection, values: LibraryQuery.values(model.library, \.collection))
                    }
                    GridRow {
                        HStack { Toggle("From", isOn: $useFrom); DatePicker("", selection: $from, displayedComponents: .date).labelsHidden().disabled(!useFrom) }
                        HStack { Toggle("To", isOn: $useTo); DatePicker("", selection: $to, displayedComponents: .date).labelsHidden().disabled(!useTo) }
                        Button("Clear Filters") { filter = LibraryFilter(); useFrom = false; useTo = false }
                    }
                }
                .textFieldStyle(.roundedBorder)
            }
            let stats = LibraryQuery.stats(model.library)
            Text("Showing \(shown.count) of \(stats.sermons) sermons · \(stats.favorites) favorites · \(stats.outlines) outlines · \(stats.series) series")
                .font(.caption).foregroundStyle(.secondary)
            List(shown, selection: $selection) { record in
                LibraryRow(record: record, isOpen: record.id == model.draft.id && !model.isNewDraft)
                    .tag(record.id)
            }
            .contextMenu(forSelectionType: String.self) { ids in menu(for: Array(ids)) } primaryAction: { ids in if let id = ids.first { model.open(id) } }
            .onDeleteCommand { model.delete(Array(selection)) }
            HStack {
                Button("Open") { if let id = selection.first { model.open(id) } }.disabled(selection.count != 1)
                Button("Rename…") { if let id = selection.first { model.rename(id) } }.disabled(selection.count != 1)
                Button("Duplicate") { if let id = selection.first { model.duplicate(id) } }.disabled(selection.count != 1)
                Button("Delete…") { model.delete(Array(selection)); selection.removeAll() }.disabled(selection.isEmpty)
                Spacer()
                Menu("Archive") {
                    Button("Back Up Library…") { model.backup() }
                    Button("Restore or Import Archive…") { model.restore() }
                    Divider()
                    Button("Export All Sermons…") { model.bulkExport(model.library, label: "Library") }
                    Button("Export Shown Sermons…") { model.bulkExport(shown, label: "Selection") }
                    Button("Export Shown as Archive (Mac, Windows, Web)…") { model.backup(shown, suggestedName: "SermonStudio-filtered") }
                }.frame(width: 110)
            }
        }
        .padding(16)
    }

    @ViewBuilder private func menu(for ids: [String]) -> some View {
        if ids.count == 1, let id = ids.first {
            Button("Open") { model.open(id) }
            Button("Rename…") { model.rename(id) }
            Button("Duplicate") { model.duplicate(id) }
            Button("Toggle Favorite") { model.toggleFavorite(id) }
        }
        if !ids.isEmpty {
            Button("Export as Archive…") { model.backup(model.library.filter { ids.contains($0.id) }, suggestedName: "SermonStudio-selection") }
            Divider()
            Button("Delete…", role: .destructive) { model.delete(ids); selection.subtract(ids) }
        }
    }

    private func suggestField(_ title: String, text: Binding<String>, values: [String]) -> some View {
        HStack(spacing: 2) {
            TextField(title, text: text)
            if !values.isEmpty {
                Menu { ForEach(values, id: \.self) { value in Button(value) { text.wrappedValue = value } } } label: { Image(systemName: "chevron.down") }
                    .menuStyle(.borderlessButton).frame(width: 22).accessibilityLabel(title + " suggestions")
            }
        }
    }
}

struct LibraryRow: View {
    var record: SermonRecord
    var isOpen: Bool
    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack {
                if record.favorite { Image(systemName: "star.fill").foregroundStyle(Theme.gold).accessibilityLabel("Favorite") }
                Text(record.displayTitle).font(.system(.headline, design: .serif))
                if isOpen { Text("Open").font(.caption2).padding(.horizontal, 5).background(Theme.gold.opacity(0.3), in: Capsule()) }
                Spacer()
                Text(record.date).foregroundStyle(.secondary).monospacedDigit()
            }
            let details = [record.speaker, record.series, record.primaryScripture].filter { !$0.isEmpty }.joined(separator: " · ")
            if !details.isEmpty { Text(details).font(.caption).foregroundStyle(Theme.accent) }
            if !record.preview.isEmpty { Text(record.preview).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
        }
        .padding(.vertical, 3)
        .accessibilityElement(children: .combine)
    }
}
