import Foundation

public struct WhisperModelInfo: Identifiable, Equatable, Sendable {
    public let id: String
    public let name: String
    public let detail: String
    public let approximateSize: String
    /// WhisperKit variant searched in argmaxinc/whisperkit-coreml.
    public var variant: String { id }
    public var folderName: String { "openai_whisper-" + id }
}

public enum ModelInstallState: Equatable, Sendable {
    case missing
    case incomplete([String])
    case installed(bytes: Int64)

    public var isInstalled: Bool { if case .installed = self { return true }; return false }
}

/// Offline Whisper models (WhisperKit Core ML builds, accelerated on Apple Silicon by the Neural Engine).
public enum ModelCatalog {
    public static let all: [WhisperModelInfo] = [
        WhisperModelInfo(id: "tiny.en", name: "Tiny", detail: "Fastest, lowest accuracy", approximateSize: "≈75 MB"),
        WhisperModelInfo(id: "base.en", name: "Base", detail: "Fast drafts", approximateSize: "≈145 MB"),
        WhisperModelInfo(id: "small.en", name: "Small", detail: "Recommended balance", approximateSize: "≈480 MB"),
        WhisperModelInfo(id: "medium.en", name: "Medium", detail: "Higher accuracy, slower", approximateSize: "≈1.5 GB"),
        WhisperModelInfo(id: "large-v3-v20240930_626MB", name: "Large v3 Turbo", detail: "Best accuracy, compressed", approximateSize: "≈630 MB"),
    ]
    public static let defaultModel = "small.en"
    public static let repository = "argmaxinc/whisperkit-coreml"
    /// Components WhisperKit needs to load a model.
    public static let requiredComponents = ["MelSpectrogram.mlmodelc", "AudioEncoder.mlmodelc", "TextDecoder.mlmodelc"]

    /// Accepts legacy preference values ("large-v3-turbo") and rejects unknown names.
    public static func resolve(_ value: String?) -> String {
        guard let value else { return defaultModel }
        if value == "large-v3-turbo" { return "large-v3-v20240930_626MB" }
        return all.contains { $0.id == value } ? value : defaultModel
    }

    public static func info(_ id: String) -> WhisperModelInfo { all.first { $0.id == resolve(id) } ?? all[2] }

    /// Same location WhisperKit/Hub uses by default, so models downloaded by earlier versions are found.
    public static var defaultDownloadBase: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("huggingface")
    }

    public static func folder(for id: String, downloadBase: URL = defaultDownloadBase) -> URL {
        downloadBase.appendingPathComponent("models").appendingPathComponent(repository).appendingPathComponent(info(id).folderName)
    }

    /// Structural integrity check: every compiled model component must be present and non-empty.
    public static func installState(at folder: URL) -> ModelInstallState {
        let fm = FileManager.default
        guard fm.fileExists(atPath: folder.path) else { return .missing }
        var missing: [String] = []
        for component in requiredComponents {
            let data = folder.appendingPathComponent(component).appendingPathComponent("coremldata.bin")
            let size = (try? fm.attributesOfItem(atPath: data.path)[.size] as? NSNumber)?.int64Value ?? 0
            if size <= 0 { missing.append(component) }
        }
        if !missing.isEmpty { return .incomplete(missing) }
        return .installed(bytes: directorySize(folder))
    }

    public static func directorySize(_ url: URL) -> Int64 {
        guard let enumerator = FileManager.default.enumerator(at: url, includingPropertiesForKeys: [.fileSizeKey]) else { return 0 }
        var total: Int64 = 0
        for case let file as URL in enumerator { total += Int64((try? file.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) }
        return total
    }
}

public enum AppearanceMode: String, CaseIterable, Sendable { case system, light, dark }
public enum OutlineGeneratorKind: String, CaseIterable, Sendable { case original, ollama }

/// Validated preferences in UserDefaults. Keys match earlier releases, so settings survive updates.
/// Invalid stored values (from a damaged plist or an older build) fall back to defaults.
public final class SettingsStore {
    public enum Key {
        public static let model = "defaultModel"
        public static let exportFolder = "exportFolder"
        public static let exportBookmark = "exportFolderBookmark"
        public static let useOllama = "outlineUseOllama"
        public static let ollamaModel = "localOutlineModel"
        public static let fontSize = "editorFontSize"
        public static let appearance = "appearanceMode"
        public static let biblicalAssistance = "biblicalAssistance"
        public static let vocabulary = "contextVocabulary"
        public static let workspaceSplit = "workspaceSplit"
        public static let outlineMode = "outlineEditorMode"
        public static let followPlayback = "followPlayback"
    }

    public static let fontSizes: [Double] = [14, 16, 18, 21, 24]
    public static let defaultOllamaModel = "llama3.2:3b"
    private let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) { self.defaults = defaults }

    public var model: String {
        get { ModelCatalog.resolve(defaults.string(forKey: Key.model)) }
        set { defaults.set(ModelCatalog.resolve(newValue), forKey: Key.model) }
    }
    public var outlineGenerator: OutlineGeneratorKind {
        get { defaults.bool(forKey: Key.useOllama) ? .ollama : .original }
        set { defaults.set(newValue == .ollama, forKey: Key.useOllama) }
    }
    public var ollamaModel: String {
        get { let value = defaults.string(forKey: Key.ollamaModel) ?? ""; return Self.isValidOllamaModel(value) ? value : Self.defaultOllamaModel }
        set { if Self.isValidOllamaModel(newValue) { defaults.set(newValue, forKey: Key.ollamaModel) } }
    }
    public var fontSize: Double {
        get { Self.nearestFontSize(defaults.object(forKey: Key.fontSize) as? Double ?? 18) }
        set { defaults.set(Self.nearestFontSize(newValue), forKey: Key.fontSize) }
    }
    public var appearance: AppearanceMode {
        get { AppearanceMode(rawValue: defaults.string(forKey: Key.appearance) ?? "") ?? .system }
        set { defaults.set(newValue.rawValue, forKey: Key.appearance) }
    }
    public var biblicalAssistance: Bool {
        get { defaults.object(forKey: Key.biblicalAssistance) as? Bool ?? true }
        set { defaults.set(newValue, forKey: Key.biblicalAssistance) }
    }
    public var vocabulary: String {
        get { String((defaults.string(forKey: Key.vocabulary) ?? "").prefix(2000)) }
        set { defaults.set(String(newValue.prefix(2000)), forKey: Key.vocabulary) }
    }
    public var workspaceSplit: Double {
        get { Self.clampSplit(defaults.object(forKey: Key.workspaceSplit) as? Double ?? 0.5) }
        set { defaults.set(Self.clampSplit(newValue), forKey: Key.workspaceSplit) }
    }
    public var outlineMode: String {
        get { let value = defaults.string(forKey: Key.outlineMode) ?? "visual"; return ["visual", "markdown", "reading"].contains(value) ? value : "visual" }
        set { defaults.set(["visual", "markdown", "reading"].contains(newValue) ? newValue : "visual", forKey: Key.outlineMode) }
    }
    public var followPlayback: Bool {
        get { defaults.object(forKey: Key.followPlayback) as? Bool ?? true }
        set { defaults.set(newValue, forKey: Key.followPlayback) }
    }
    public var exportFolderPath: String {
        get { defaults.string(forKey: Key.exportFolder) ?? "" }
        set { defaults.set(newValue, forKey: Key.exportFolder) }
    }
    public var exportBookmark: Data? {
        get { defaults.data(forKey: Key.exportBookmark) }
        set { defaults.set(newValue, forKey: Key.exportBookmark) }
    }

    public static func nearestFontSize(_ value: Double) -> Double {
        guard value.isFinite else { return 18 }
        return fontSizes.min { abs($0 - value) < abs($1 - value) } ?? 18
    }
    public static func clampSplit(_ value: Double) -> Double { value.isFinite ? min(0.8, max(0.2, value)) : 0.5 }

    /// Local model tags only; anything routed to Ollama's cloud is rejected to keep sermons on this Mac.
    public static func isValidOllamaModel(_ model: String) -> Bool {
        let trimmed = model.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty, trimmed.count <= 120, !trimmed.lowercased().contains("cloud") else { return false }
        return trimmed.range(of: "^[a-zA-Z0-9_.-]+(/[a-zA-Z0-9_.-]+){0,2}(:[a-zA-Z0-9_.-]+)?$", options: .regularExpression) != nil
    }
}

/// Protects asynchronous work from applying results to a different sermon or over newer edits.
public final class WorkspaceGuard: @unchecked Sendable {
    public struct Ticket: Equatable, Sendable { public let revision: Int; public let inputs: [String] }
    private let lock = NSLock()
    private var value = 0

    public init() {}

    public var revision: Int { lock.lock(); defer { lock.unlock() }; return value }
    /// Call whenever the open sermon changes (open, new, delete, restore).
    public func advance() { lock.lock(); value += 1; lock.unlock() }
    public func capture(_ inputs: String...) -> Ticket { Ticket(revision: revision, inputs: inputs) }
    public func isCurrent(_ ticket: Ticket, _ inputs: String...) -> Bool { ticket.revision == revision && ticket.inputs == inputs }
}
