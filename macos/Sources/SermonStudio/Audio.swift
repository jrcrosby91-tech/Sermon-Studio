import AVFoundation
import Combine
import SwiftUI
import SermonCore

enum AudioError: LocalizedError {
    case unsupported(String)
    var errorDescription: String? {
        switch self { case .unsupported(let detail): return detail }
    }
}

/// Playback for the open sermon's recording. A recording always belongs to exactly one open sermon: it is
/// unloaded whenever another sermon is opened, created or deleted.
@MainActor final class AudioController: ObservableObject {
    @Published private(set) var url: URL?
    @Published private(set) var isPlaying = false
    @Published private(set) var currentTime: Double = 0
    @Published private(set) var duration: Double = 0
    @Published private(set) var peaks: [Float] = []
    @Published var rate: Float = 1 { didSet { if isPlaying { player?.rate = rate } } }

    private var player: AVPlayer?
    private var timeObserver: Any?
    private var endObserver: NSObjectProtocol?
    private var waveformTask: Task<Void, Never>?
    static let rates: [Float] = [0.75, 1, 1.25, 1.5, 1.75, 2]

    /// Validates and loads a recording. Throws a readable error for files without playable audio.
    func load(_ url: URL) async throws {
        let asset = AVURLAsset(url: url)
        let (playable, length) = try await asset.load(.isPlayable, .duration)
        let tracks = try await asset.loadTracks(withMediaType: .audio)
        guard playable, !tracks.isEmpty else {
            throw AudioError.unsupported("“" + url.lastPathComponent + "” has no audio macOS can play. Use MP3, M4A, WAV, AIFF, CAF, FLAC or a video file with an audio track.")
        }
        unload()
        let item = AVPlayerItem(asset: asset)
        item.audioTimePitchAlgorithm = .spectral // keeps pitch natural at 0.75×–2×
        let player = AVPlayer(playerItem: item)
        self.player = player
        self.url = url
        duration = length.seconds.isFinite ? length.seconds : 0
        currentTime = 0
        timeObserver = player.addPeriodicTimeObserver(forInterval: CMTime(seconds: 0.2, preferredTimescale: 600), queue: .main) { [weak self] time in
            MainActor.assumeIsolated { self?.currentTime = time.seconds.isFinite ? time.seconds : 0 }
        }
        endObserver = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.isPlaying = false }
        }
        let total = duration
        waveformTask = Task { [weak self] in
            let peaks = await Task.detached(priority: .utility) { Self.computePeaks(url: url, duration: total, buckets: 600) }.value
            guard !Task.isCancelled, let self, self.url == url else { return }
            self.peaks = peaks
        }
    }

    func unload() {
        waveformTask?.cancel(); waveformTask = nil
        player?.pause()
        if let timeObserver { player?.removeTimeObserver(timeObserver) }
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        timeObserver = nil; endObserver = nil
        player?.replaceCurrentItem(with: nil)
        player = nil
        url = nil; isPlaying = false; currentTime = 0; duration = 0; peaks = []
    }

    func togglePlay() {
        guard let player else { return }
        if isPlaying { player.pause(); isPlaying = false }
        else {
            if duration > 0, currentTime >= duration - 0.25 { seek(to: 0) }
            player.playImmediately(atRate: rate); isPlaying = true
        }
    }

    func seek(to seconds: Double) {
        guard let player else { return }
        let target = min(max(0, seconds), max(0, duration))
        currentTime = target
        player.seek(to: CMTime(seconds: target, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
    }

    func skip(_ delta: Double) { seek(to: currentTime + delta) }

    /// Peak levels for the waveform, read in bounded chunks so long recordings do not load into memory at once.
    nonisolated static func computePeaks(url: URL, duration: Double, buckets: Int) -> [Float] {
        let asset = AVURLAsset(url: url)
        guard let reader = try? AVAssetReader(asset: asset) else { return [] }
        final class TrackBox: @unchecked Sendable { var track: AVAssetTrack? }
        let box = TrackBox()
        let semaphore = DispatchSemaphore(value: 0)
        asset.loadTracks(withMediaType: .audio) { tracks, _ in box.track = tracks?.first; semaphore.signal() }
        semaphore.wait()
        guard let track = box.track else { return [] }
        let sampleRate = 4000.0
        let output = AVAssetReaderTrackOutput(track: track, outputSettings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false, AVSampleRateKey: sampleRate, AVNumberOfChannelsKey: 1,
        ])
        guard reader.canAdd(output) else { return [] }
        reader.add(output)
        guard reader.startReading() else { return [] }
        let totalSamples = max(1, Int(duration * sampleRate))
        let perBucket = max(1, totalSamples / buckets)
        var peaks = Array(repeating: Float(0), count: buckets)
        var index = 0
        while let buffer = output.copyNextSampleBuffer() {
            guard let block = CMSampleBufferGetDataBuffer(buffer) else { continue }
            // Copy rather than map: block buffers are not guaranteed to be contiguous.
            var samples = [Int16](repeating: 0, count: CMBlockBufferGetDataLength(block) / 2)
            guard !samples.isEmpty, CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: samples.count * 2, destination: &samples) == noErr else { continue }
            for sample in samples {
                let bucket = min(buckets - 1, index / perBucket)
                let value = Float(abs(Int32(sample))) / 32768
                if value > peaks[bucket] { peaks[bucket] = value }
                index += 1
            }
        }
        let top = max(peaks.max() ?? 1, 0.01)
        return peaks.map { $0 / top }
    }
}

/// Interactive waveform: click or drag to seek. VoiceOver users get an adjustable control.
struct WaveformView: View {
    @ObservedObject var audio: AudioController
    var accent: Color

    var body: some View {
        GeometryReader { geometry in
            let progress = audio.duration > 0 ? audio.currentTime / audio.duration : 0
            Canvas { context, size in
                let peaks = audio.peaks.isEmpty ? Array(repeating: Float(0.04), count: 120) : audio.peaks
                let width = size.width / CGFloat(peaks.count)
                for (index, peak) in peaks.enumerated() {
                    let height = max(2, CGFloat(peak) * size.height * 0.9)
                    let rect = CGRect(x: CGFloat(index) * width, y: (size.height - height) / 2, width: max(1, width - 1), height: height)
                    let played = Double(index) / Double(peaks.count) <= progress
                    context.fill(Path(roundedRect: rect, cornerRadius: 1), with: .color(played ? accent : Color.secondary.opacity(0.35)))
                }
            }
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onChanged { value in
                guard audio.duration > 0, geometry.size.width > 0 else { return }
                audio.seek(to: Double(value.location.x / geometry.size.width) * audio.duration)
            })
        }
        .frame(height: 56)
        .accessibilityElement()
        .accessibilityLabel("Recording waveform")
        .accessibilityValue(TranscriptTools.timestamp(audio.currentTime) + " of " + TranscriptTools.timestamp(audio.duration))
        .accessibilityAdjustableAction { direction in audio.skip(direction == .increment ? 15 : -15) }
    }
}

/// Play/pause, ±15 s, speed, elapsed/total and seeking.
struct PlayerBar: View {
    @ObservedObject var audio: AudioController
    var accent: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            WaveformView(audio: audio, accent: accent)
            HStack(spacing: 10) {
                Button { audio.skip(-15) } label: { Image(systemName: "gobackward.15") }
                    .help("Back 15 seconds").accessibilityLabel("Back 15 seconds")
                Button { audio.togglePlay() } label: { Image(systemName: audio.isPlaying ? "pause.fill" : "play.fill").frame(width: 18) }
                    .help(audio.isPlaying ? "Pause (⌥Space)" : "Play (⌥Space)").accessibilityLabel(audio.isPlaying ? "Pause" : "Play")
                Button { audio.skip(15) } label: { Image(systemName: "goforward.15") }
                    .help("Forward 15 seconds").accessibilityLabel("Forward 15 seconds")
                Text(TranscriptTools.timestamp(audio.currentTime) + " / " + TranscriptTools.timestamp(audio.duration))
                    .monospacedDigit().foregroundStyle(.secondary)
                Slider(value: Binding(get: { audio.currentTime }, set: { audio.seek(to: $0) }), in: 0...max(1, audio.duration))
                    .accessibilityLabel("Playback position")
                Picker("Speed", selection: $audio.rate) {
                    ForEach(AudioController.rates, id: \.self) { Text(String(format: "%g×", $0)).tag($0) }
                }
                .frame(width: 110)
            }
            Text(audio.url?.lastPathComponent ?? "").font(.caption).foregroundStyle(.secondary).lineLimit(1)
        }
        .disabled(audio.url == nil)
    }
}
