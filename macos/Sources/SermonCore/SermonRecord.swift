import Foundation

/// Any JSON value. Used to carry fields written by another platform (or a newer version) through a
/// load/save cycle unchanged, so Sermon Studio on macOS never silently drops data it does not understand.
public enum JSONValue: Codable, Equatable, Sendable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([JSONValue])
    case object([String: JSONValue])

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() { self = .null }
        else if let value = try? container.decode(Bool.self) { self = .bool(value) }
        else if let value = try? container.decode(Double.self) { self = .number(value) }
        else if let value = try? container.decode(String.self) { self = .string(value) }
        else if let value = try? container.decode([JSONValue].self) { self = .array(value) }
        else { self = .object(try container.decode([String: JSONValue].self)) }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .null: try container.encodeNil()
        case .bool(let value): try container.encode(value)
        case .number(let value): try container.encode(value)
        case .string(let value): try container.encode(value)
        case .array(let value): try container.encode(value)
        case .object(let value): try container.encode(value)
        }
    }
}

struct AnyCodingKey: CodingKey, Hashable {
    var stringValue: String
    var intValue: Int?
    init(_ string: String) { stringValue = string }
    init?(stringValue: String) { self.stringValue = stringValue }
    init?(intValue: Int) { stringValue = String(intValue); self.intValue = intValue }
}

/// Reads keys case-insensitively so PascalCase archives (Windows sermons.json) and camelCase archives
/// (macOS, web and the Windows "Mac-compatible" export) decode identically.
struct LenientKeyedReader {
    let container: KeyedDecodingContainer<AnyCodingKey>
    let keys: [String: AnyCodingKey]

    init(_ decoder: Decoder) throws {
        container = try decoder.container(keyedBy: AnyCodingKey.self)
        var map: [String: AnyCodingKey] = [:]
        for key in container.allKeys where map[key.stringValue.lowercased()] == nil { map[key.stringValue.lowercased()] = key }
        keys = map
    }

    func key(_ name: String) -> AnyCodingKey? { keys[name.lowercased()] }

    func string(_ name: String) -> String? {
        guard let key = key(name) else { return nil }
        if let value = try? container.decodeIfPresent(String.self, forKey: key) { return value }
        if let list = try? container.decodeIfPresent([String].self, forKey: key) { return list.joined(separator: ", ") }
        if let number = try? container.decodeIfPresent(Double.self, forKey: key) {
            return number == number.rounded() && abs(number) < 1e15 ? String(Int64(number)) : String(number)
        }
        return nil
    }

    func double(_ name: String) -> Double? {
        guard let key = key(name) else { return nil }
        if let value = try? container.decodeIfPresent(Double.self, forKey: key) { return value }
        if let text = try? container.decodeIfPresent(String.self, forKey: key) { return Double(text) }
        return nil
    }

    func bool(_ name: String) -> Bool? {
        guard let key = key(name) else { return nil }
        if let value = try? container.decodeIfPresent(Bool.self, forKey: key) { return value }
        if let number = try? container.decodeIfPresent(Double.self, forKey: key) { return number != 0 }
        if let text = try? container.decodeIfPresent(String.self, forKey: key) { return ["true", "1", "yes"].contains(text.lowercased()) }
        return nil
    }

    func date(_ name: String) -> Date? {
        guard let key = key(name) else { return nil }
        if let text = try? container.decodeIfPresent(String.self, forKey: key) { return ArchiveDates.parse(text) }
        if let number = try? container.decodeIfPresent(Double.self, forKey: key), number > 0 {
            return Date(timeIntervalSince1970: number > 100_000_000_000 ? number / 1000 : number)
        }
        return nil
    }

    func extras(excluding known: Set<String>) -> [String: JSONValue] {
        var result: [String: JSONValue] = [:]
        for key in container.allKeys where !known.contains(key.stringValue.lowercased()) {
            if let value = try? container.decode(JSONValue.self, forKey: key) { result[key.stringValue] = value }
        }
        return result
    }
}

public enum ArchiveDates {
    private static let fractional: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()
    private static let plain: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()

    /// Parses ISO 8601 timestamps from every platform: web ("…T05:46:35.123Z"), Windows DateTimeOffset
    /// ("…T05:46:35.1234567+00:00") and plain seconds. The .NET default value (year 1) means "never".
    public static func parse(_ text: String) -> Date? {
        var value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty, !value.hasPrefix("0001-01-01") else { return nil }
        // ISO8601DateFormatter accepts at most millisecond precision; trim longer fractions (.NET writes 7 digits).
        if let dot = value.firstIndex(of: "."), let zone = value[dot...].firstIndex(where: { $0 == "Z" || $0 == "+" || $0 == "-" }) {
            let digits = value[value.index(after: dot)..<zone]
            if digits.count > 3 { value.replaceSubrange(value.index(after: dot)..<zone, with: digits.prefix(3)) }
        }
        return fractional.date(from: value) ?? plain.date(from: value)
    }

    public static func format(_ date: Date) -> String { fractional.string(from: date) }

    /// Calendar date used by sermons ("yyyy-MM-dd").
    public static func day(_ date: Date) -> String {
        let components = Calendar(identifier: .gregorian).dateComponents(in: TimeZone.current, from: date)
        return String(format: "%04d-%02d-%02d", components.year ?? 1970, components.month ?? 1, components.day ?? 1)
    }

    public static func dayDate(_ text: String) -> Date? {
        let parts = text.prefix(10).split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        var components = DateComponents()
        components.year = parts[0]; components.month = parts[1]; components.day = parts[2]; components.hour = 12
        return Calendar(identifier: .gregorian).date(from: components)
    }
}

public struct SermonSegment: Codable, Equatable, Identifiable, Sendable {
    public var id: String
    public var start: Double
    public var end: Double
    public var text: String
    public var confidence: Double?
    public var extra: [String: JSONValue]

    public init(id: String = UUID().uuidString, start: Double, end: Double, text: String, confidence: Double? = nil, extra: [String: JSONValue] = [:]) {
        self.id = id; self.start = start; self.end = end; self.text = text; self.confidence = confidence; self.extra = extra
    }

    private static let known: Set<String> = ["id", "start", "end", "text", "confidence"]

    public init(from decoder: Decoder) throws {
        let reader = try LenientKeyedReader(decoder)
        let id = reader.string("id")?.trimmingCharacters(in: .whitespaces) ?? ""
        self.id = id.isEmpty ? UUID().uuidString : id
        var start = reader.double("start")
        var end = reader.double("end")
        // Web transcription chunks store [start, end] in "timestamp".
        if start == nil, let key = reader.key("timestamp"), let pair = try? reader.container.decode([Double?].self, forKey: key) {
            start = pair.first ?? nil
            end = pair.count > 1 ? pair[1] : nil
        }
        let safeStart = start.flatMap { $0.isFinite ? max(0, $0) : nil } ?? 0
        self.start = safeStart
        self.end = max(safeStart, end.flatMap { $0.isFinite ? $0 : nil } ?? safeStart)
        text = reader.string("text") ?? ""
        confidence = reader.double("confidence").flatMap { $0.isFinite ? $0 : nil }
        extra = reader.extras(excluding: Self.known)
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: AnyCodingKey.self)
        try container.encode(id, forKey: AnyCodingKey("id"))
        try container.encode(start.isFinite ? start : 0, forKey: AnyCodingKey("start"))
        try container.encode(end.isFinite ? end : 0, forKey: AnyCodingKey("end"))
        try container.encode(text, forKey: AnyCodingKey("text"))
        if let confidence, confidence.isFinite { try container.encode(confidence, forKey: AnyCodingKey("confidence")) }
        for (key, value) in extra where !Self.known.contains(key.lowercased()) { try container.encode(value, forKey: AnyCodingKey(key)) }
    }
}

/// One sermon. The JSON format is shared with Windows and the web app: camelCase keys are written,
/// any capitalization is read, and unknown fields are preserved.
public struct SermonRecord: Codable, Equatable, Identifiable, Sendable {
    public var id: String
    public var title: String
    public var date: String
    public var speaker: String
    public var primaryScripture: String
    public var series: String
    public var collection: String
    public var tags: String
    public var favorite: Bool
    public var lastEdited: Date?
    public var lastOpened: Date?
    public var original: String
    public var cleaned: String
    public var outline: String
    public var segments: [SermonSegment]?
    public var srt: String
    public var vtt: String
    /// Fields this version does not understand, written back unchanged.
    public var extra: [String: JSONValue]

    public init(id: String = UUID().uuidString, title: String = "", date: String = ArchiveDates.day(Date()), speaker: String = "",
                primaryScripture: String = "", series: String = "", collection: String = "", tags: String = "", favorite: Bool = false,
                lastEdited: Date? = nil, lastOpened: Date? = nil, original: String = "", cleaned: String = "", outline: String = "",
                segments: [SermonSegment]? = nil, srt: String = "", vtt: String = "", extra: [String: JSONValue] = [:]) {
        self.id = id; self.title = title; self.date = date; self.speaker = speaker; self.primaryScripture = primaryScripture
        self.series = series; self.collection = collection; self.tags = tags; self.favorite = favorite
        self.lastEdited = lastEdited; self.lastOpened = lastOpened; self.original = original; self.cleaned = cleaned
        self.outline = outline; self.segments = segments; self.srt = srt; self.vtt = vtt; self.extra = extra
    }

    static let knownKeys: Set<String> = ["id", "title", "date", "speaker", "primaryscripture", "series", "collection", "tags", "favorite",
                                         "lastedited", "lastopened", "original", "cleaned", "outline", "segments", "srt", "vtt",
                                         // Computed by the Windows interface; never stored.
                                         "librarypreview", "libraryheading"]

    public init(from decoder: Decoder) throws {
        let reader = try LenientKeyedReader(decoder)
        id = reader.string("id")?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        title = reader.string("title") ?? ""
        date = reader.string("date") ?? ""
        speaker = reader.string("speaker") ?? ""
        primaryScripture = reader.string("primaryScripture") ?? ""
        series = reader.string("series") ?? ""
        collection = reader.string("collection") ?? ""
        tags = reader.string("tags") ?? ""
        favorite = reader.bool("favorite") ?? false
        lastEdited = reader.date("lastEdited")
        lastOpened = reader.date("lastOpened")
        original = reader.string("original") ?? ""
        cleaned = reader.string("cleaned") ?? ""
        outline = reader.string("outline") ?? ""
        srt = reader.string("srt") ?? ""
        vtt = reader.string("vtt") ?? ""
        var extra = reader.extras(excluding: Self.knownKeys)
        if let key = reader.key("segments"), let list = try? reader.container.decodeIfPresent([SermonSegment].self, forKey: key) {
            segments = list
        } else {
            segments = nil
            // Keep malformed segment data rather than discarding it.
            if let key = reader.key("segments"), let raw = try? reader.container.decode(JSONValue.self, forKey: key), raw != .null { extra[key.stringValue] = raw }
        }
        if segments == nil, let key = reader.key("chunks"), let chunks = try? reader.container.decode([SermonSegment].self, forKey: key), !chunks.isEmpty {
            segments = chunks
        }
        if segments == nil, !srt.isEmpty {
            let parsed = Subtitles.parse(srt)
            if !parsed.isEmpty { segments = parsed }
        }
        self.extra = extra
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: AnyCodingKey.self)
        func put(_ value: String, _ key: String) throws { try container.encode(value, forKey: AnyCodingKey(key)) }
        try put(id, "id"); try put(title, "title"); try put(date, "date")
        try put(speaker, "speaker"); try put(primaryScripture, "primaryScripture"); try put(series, "series")
        try put(collection, "collection"); try put(tags, "tags")
        try container.encode(favorite, forKey: AnyCodingKey("favorite"))
        if let lastEdited { try put(ArchiveDates.format(lastEdited), "lastEdited") }
        if let lastOpened { try put(ArchiveDates.format(lastOpened), "lastOpened") }
        try put(original, "original"); try put(cleaned, "cleaned"); try put(outline, "outline")
        if let segments { try container.encode(segments, forKey: AnyCodingKey("segments")) }
        if !srt.isEmpty { try put(srt, "srt") }
        if !vtt.isEmpty { try put(vtt, "vtt") }
        var written: Set<String> = ["id", "title", "date", "speaker", "primaryscripture", "series", "collection", "tags", "favorite",
                                    "original", "cleaned", "outline"]
        if lastEdited != nil { written.insert("lastedited") }
        if lastOpened != nil { written.insert("lastopened") }
        if segments != nil { written.insert("segments") }
        if !srt.isEmpty { written.insert("srt") }
        if !vtt.isEmpty { written.insert("vtt") }
        for (key, value) in extra.sorted(by: { $0.key < $1.key }) where !written.contains(key.lowercased()) && !["librarypreview", "libraryheading"].contains(key.lowercased()) {
            try container.encode(value, forKey: AnyCodingKey(key))
        }
    }

    public var displayTitle: String {
        let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "Untitled sermon" : trimmed
    }

    /// Text shown in library previews: cleaned transcript when present, otherwise the original.
    public var preview: String {
        let source = cleaned.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? original : cleaned
        let collapsed = source.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        return collapsed.count > 180 ? String(collapsed.prefix(177)) + "…" : collapsed
    }

    public var tagList: [String] {
        tags.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }
}
