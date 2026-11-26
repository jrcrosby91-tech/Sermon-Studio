import AVFoundation
import AppKit
import SermonCore
import WhisperKit

struct TranscriptionOutput {
    var text: String
    var segments: [SermonSegment]
}

struct TranscriptionCancelled: Error {
    var partial: TranscriptionOutput
}

/// Thread-safe flag WhisperKit's decoding callback reads to stop early.
final class StopFlag: @unchecked Sendable {
    private let lock = NSLock()
    private var stopped = false
    var isStopped: Bool { lock.lock(); defer { lock.unlock() }; return stopped }
    func stop() { lock.lock(); stopped = true; lock.unlock() }
}

/// Offline transcription with WhisperKit (Core ML on the Apple Neural Engine) plus model management.
@MainActor final class TranscriptionService: ObservableObject {
    @Published private(set) var states: [String: ModelInstallState] = [:]
    @Published private(set) var downloadingModel: String?
    @Published private(set) var downloadFraction: Double = 0
    @Published private(set) var isTranscribing = false
    @Published private(set) var progress: Double = 0
    @Published private(set) var progressText = ""

    private var downloadTask: Task<Void, Never>?
    private var stopFlag: StopFlag?
    /// Audio is decoded in sections of this length so a two-hour recording never sits in memory at once.
    static let sectionSeconds: Double = 600

    init() { refresh() }

    func refresh() {
        var result: [String: ModelInstallState] = [:]
        for model in ModelCatalog.all { result[model.id] = ModelCatalog.installState(at: ModelCatalog.folder(for: model.id)) }
        states = result
    }

    func state(_ id: String) -> ModelInstallState { states[ModelCatalog.resolve(id)] ?? .missing }

    var modelsFolder: URL { ModelCatalog.defaultDownloadBase.appendingPathComponent("models").appendingPathComponent(ModelCatalog.repository) }

    /// Downloads a model with progress. Returns its folder after verifying every component is present.
    func download(_ id: String) async throws -> URL {
        let info = ModelCatalog.info(id)
        downloadingModel = info.id
        downloadFraction = 0
        defer { downloadingModel = nil; refresh() }
        let folder = try await WhisperKit.download(variant: info.variant, downloadBase: ModelCatalog.defaultDownloadBase) { progress in
            let fraction = progress.fractionCompleted
            Task { @MainActor [weak self] in self?.downloadFraction = fraction }
        }
        try Task.checkCancellation()
        let state = ModelCatalog.installState(at: folder)
        guard state.isInstalled else {
            throw NSError(domain: "SermonStudio.Model", code: 1, userInfo: [NSLocalizedDescriptionKey: "The " + info.name + " model download is incomplete. Delete it in Settings and download again."])
        }
        return folder
    }

    func startDownload(_ id: String, onFinish: @escaping (String) -> Void) {
        guard downloadTask == nil else { return }
        downloadTask = Task {
            defer { downloadTask = nil }
            do {
                _ = try await download(id)
                onFinish(ModelCatalog.info(id).name + " model installed and verified.")
            } catch is CancellationError {
                removeIncomplete(id)
                onFinish("Model download canceled.")
            } catch {
                if Task.isCancelled { removeIncomplete(id); onFinish("Model download canceled.") }
                else { onFinish("Model download failed: " + error.localizedDescription) }
            }
        }
    }

    func cancelDownload() { downloadTask?.cancel() }
    var isDownloading: Bool { downloadTask != nil || downloadingModel != nil }

    private func removeIncomplete(_ id: String) {
        let folder = ModelCatalog.folder(for: id)
        if !ModelCatalog.installState(at: folder).isInstalled { try? FileManager.default.removeItem(at: folder) }
        refresh()
    }

    func delete(_ id: String) throws {
        try FileManager.default.removeItem(at: ModelCatalog.folder(for: id))
        refresh()
    }

    func cancelTranscription() { stopFlag?.stop() }

    /// Transcribes a recording section by section. Cancelling stops after the current window and throws
    /// `TranscriptionCancelled` carrying everything transcribed so far.
    func transcribe(_ url: URL, model id: String, vocabulary: String?, onStatus: @escaping (String) -> Void) async throws -> TranscriptionOutput {
        let flag = StopFlag()
        stopFlag = flag
        isTranscribing = true
        progress = 0
        defer { isTranscribing = false; stopFlag = nil; progressText = "" }

        var folder = ModelCatalog.folder(for: id)
        if !ModelCatalog.installState(at: folder).isInstalled {
            onStatus("Downloading the " + ModelCatalog.info(id).name + " model (first use only)…")
            folder = try await download(id)
        }
        let source = try await Self.decodableAudio(url)
        defer { if source != url { try? FileManager.default.removeItem(at: source) } }
        let file = try AVAudioFile(forReading: source)
        let duration = Double(file.length) / file.fileFormat.sampleRate
        guard duration > 0.2 else { throw AudioError.unsupported("The recording is empty.") }

        onStatus("Loading the " + ModelCatalog.info(id).name + " model…")
        let kit = try await WhisperKit(WhisperKitConfig(model: ModelCatalog.info(id).variant, downloadBase: ModelCatalog.defaultDownloadBase,
                                                        modelFolder: folder.path, verbose: false, logLevel: .error, load: true, download: false))
        var options = DecodingOptions(verbose: false, task: .transcribe, language: "en", skipSpecialTokens: true, withoutTimestamps: false)
        if let vocabulary, let tokenizer = kit.tokenizer {
            // Biblical names and terms are given to the decoder as a prompt, which biases spelling toward them.
            let prompt = " " + vocabulary.trimmingCharacters(in: .whitespacesAndNewlines)
            let tokens = tokenizer.encode(text: prompt).filter { $0 < tokenizer.specialTokens.specialTokenBegin }
            if !tokens.isEmpty { options.promptTokens = Array(tokens.suffix(200)); options.usePrefillPrompt = true }
        }

        var texts: [String] = []
        var segments: [SermonSegment] = []
        var start = 0.0
        while start < duration {
            if flag.isStopped { throw TranscriptionCancelled(partial: TranscriptionOutput(text: texts.joined(separator: " "), segments: segments)) }
            let end = min(start + Self.sectionSeconds, duration)
            progressText = "Transcribing " + TranscriptTools.timestamp(start) + "–" + TranscriptTools.timestamp(end) + " of " + TranscriptTools.timestamp(duration)
            onStatus(progressText + "…")
            let path = source.path, sectionFrom = start
            let samples = try await Task.detached(priority: .userInitiated) {
                try AudioProcessor.loadAudioAsFloatArray(fromPath: path, startTime: sectionFrom, endTime: end)
            }.value
            let sectionStart = start
            let sectionLength = end - start
            let results: [TranscriptionResult] = try await kit.transcribe(audioArray: samples, decodeOptions: options) { [weak self] update in
                let windowEnd = min(sectionLength, Double(update.windowId + 1) * 30)
                let fraction = (sectionStart + windowEnd) / duration
                Task { @MainActor in self?.progress = min(0.99, fraction) }
                return flag.isStopped ? false : nil
            }
            for result in results {
                let text = result.text.trimmingCharacters(in: .whitespacesAndNewlines)
                if !text.isEmpty { texts.append(text) }
                for segment in result.segments {
                    let clean = Self.stripTimingTokens(segment.text)
                    guard !clean.isEmpty else { continue }
                    segments.append(SermonSegment(start: sectionStart + Double(segment.start), end: sectionStart + Double(segment.end), text: clean,
                                                  confidence: Double(segment.avgLogprob).isFinite ? Double(segment.avgLogprob) : nil))
                }
            }
            progress = end / duration
            start = end
        }
        if flag.isStopped { throw TranscriptionCancelled(partial: TranscriptionOutput(text: texts.joined(separator: " "), segments: segments)) }
        return TranscriptionOutput(text: Self.stripTimingTokens(texts.joined(separator: " ")), segments: segments)
    }

    nonisolated static func stripTimingTokens(_ text: String) -> String {
        text.replacingOccurrences(of: "<\\|[^|]*\\|>", with: "", options: .regularExpression)
            .split(whereSeparator: { $0 == " " || $0 == "\t" }).joined(separator: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Returns a file AVAudioFile can read. Containers it cannot open directly (for example video files) are
    /// converted to a temporary M4A with AVFoundation; formats macOS cannot decode at all get a clear error.
    nonisolated static func decodableAudio(_ url: URL) async throws -> URL {
        if (try? AVAudioFile(forReading: url)) != nil { return url }
        let asset = AVURLAsset(url: url)
        let tracks = try await asset.loadTracks(withMediaType: .audio)
        guard !tracks.isEmpty,
              let session = AVAssetExportSession(asset: asset, presetName: AVAssetExportPresetAppleM4A) else {
            throw AudioError.unsupported("“" + url.lastPathComponent + "” cannot be decoded on this Mac. Convert it to MP3, M4A or WAV and try again.")
        }
        let output = FileManager.default.temporaryDirectory.appendingPathComponent("sermon-" + UUID().uuidString + ".m4a")
        session.outputURL = output
        session.outputFileType = .m4a
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in session.exportAsynchronously { continuation.resume() } }
        guard session.status == .completed else {
            try? FileManager.default.removeItem(at: output)
            throw AudioError.unsupported("Could not extract audio from “" + url.lastPathComponent + "”: " + (session.error?.localizedDescription ?? "unknown error"))
        }
        return output
    }

    /// Removes temporary audio left by an earlier crash.
    nonisolated static func cleanupTemporaryAudio() {
        let temp = FileManager.default.temporaryDirectory
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: temp.path) else { return }
        for name in names where name.hasPrefix("sermon-") && name.hasSuffix(".m4a") {
            let url = temp.appendingPathComponent(name)
            let modified = (try? FileManager.default.attributesOfItem(atPath: url.path)[.modificationDate] as? Date) ?? Date()
            if Date().timeIntervalSince(modified) > 86_400 { try? FileManager.default.removeItem(at: url) }
        }
    }
}
