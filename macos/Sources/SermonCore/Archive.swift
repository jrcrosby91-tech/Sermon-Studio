import Foundation
#if canImport(Darwin)
import Darwin
#elseif canImport(Glibc)
import Glibc
#endif

public enum ArchiveError: LocalizedError, Equatable {
    case empty
    case notAList
    case invalidJSON(String)
    case duplicateIdentifiers
    case writesBlocked

    public var errorDescription: String? {
        switch self {
        case .empty: return "The sermon archive is empty or truncated."
        case .notAList: return "The file is not a Sermon Studio archive (expected a list of sermons)."
        case .invalidJSON(let detail): return "The sermon archive is not valid JSON (" + detail + ")."
        case .duplicateIdentifiers: return "The backup contains the same sermon identifier more than once."
        case .writesBlocked: return "The library could not be read when Sermon Studio started, so saving is blocked to protect it."
        }
    }
}

public enum ArchiveCodec {
    /// Assigns identifiers to records that have none and to later duplicates, keeping the first occurrence's identity.
    public static func normalize(_ records: [SermonRecord?]) -> [SermonRecord] {
        var seen = Set<String>()
        var result: [SermonRecord] = []
        for case var record? in records {
            let id = record.id.trimmingCharacters(in: .whitespacesAndNewlines)
            record.id = id.isEmpty || seen.contains(id) ? UUID().uuidString : id
            seen.insert(record.id)
            result.append(record)
        }
        return result
    }

    /// Strict parse: empty, truncated or non-list data throws rather than yielding an empty library.
    public static func decodeLibrary(_ data: Data) throws -> [SermonRecord] {
        let raw = try decodeRaw(data)
        return normalize(raw)
    }

    static func decodeRaw(_ input: Data) throws -> [SermonRecord?] {
        let data = input.starts(with: [0xEF, 0xBB, 0xBF]) ? Data(input.dropFirst(3)) : input
        // Whitespace and a UTF-8 byte-order mark alone mean a truncated file, never an empty library.
        let ignorable: Set<UInt8> = [0x20, 0x09, 0x0A, 0x0D, 0xEF, 0xBB, 0xBF]
        guard data.contains(where: { !ignorable.contains($0) }) else { throw ArchiveError.empty }
        let object: Any
        do { object = try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed]) }
        catch { throw ArchiveError.invalidJSON(error.localizedDescription) }
        guard object is [Any] else { throw ArchiveError.notAList }
        do { return try JSONDecoder().decode([SermonRecord?].self, from: data) }
        catch { throw ArchiveError.invalidJSON(String(describing: error).prefix(200).description) }
    }

    public static func encodeLibrary(_ records: [SermonRecord]) throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        return try encoder.encode(records)
    }

    public struct RestorePlan: Equatable {
        /// Sermons whose identifiers are not in the library.
        public var additions: [SermonRecord]
        /// Sermons whose identifiers already exist. They are never overwritten; the user may import them as copies.
        public var conflicts: [SermonRecord]
        public var total: Int { additions.count + conflicts.count }
    }

    public static func planRestore(existing: [SermonRecord], data: Data) throws -> RestorePlan {
        let raw = try decodeRaw(data)
        let ids = raw.compactMap { $0?.id.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }
        if Set(ids).count != ids.count { throw ArchiveError.duplicateIdentifiers }
        let records = normalize(raw)
        let known = Set(existing.map(\.id))
        return RestorePlan(additions: records.filter { !known.contains($0.id) }, conflicts: records.filter { known.contains($0.id) })
    }

    /// Copies for explicitly authorized "import as copies": new sermon and segment identifiers, marked titles.
    public static func copiesWithNewIdentifiers(_ records: [SermonRecord], suffix: String = " (imported copy)") -> [SermonRecord] {
        records.map { record in
            var copy = record
            copy.id = UUID().uuidString
            copy.title = record.displayTitle + suffix
            copy.lastOpened = nil
            copy.segments = record.segments?.map { segment in var s = segment; s.id = UUID().uuidString; return s }
            return copy
        }
    }
}

/// Crash-safe file replacement: write a uniquely named sibling, flush it to disk, then rename it over the
/// destination. A crash or failed write leaves either the complete old file or the complete new one.
public enum AtomicFile {
    public static func write(_ data: Data, to url: URL) throws {
        let directory = url.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let temp = directory.appendingPathComponent("." + url.lastPathComponent + "." + UUID().uuidString + ".tmp")
        guard FileManager.default.createFile(atPath: temp.path, contents: nil) else {
            throw CocoaError(.fileWriteNoPermission, userInfo: [NSFilePathErrorKey: directory.path])
        }
        do {
            let handle = try FileHandle(forWritingTo: temp)
            do {
                try handle.write(contentsOf: data)
                try handle.synchronize()
                try handle.close()
            } catch {
                try? handle.close()
                throw error
            }
            if rename(temp.path, url.path) != 0 {
                let code = errno
                throw NSError(domain: NSPOSIXErrorDomain, code: Int(code), userInfo: [NSLocalizedDescriptionKey: "Could not replace " + url.lastPathComponent + ": " + String(cString: strerror(code))])
            }
        } catch {
            try? FileManager.default.removeItem(at: temp)
            throw error
        }
    }

    /// Removes temporary files left by an interrupted write.
    @discardableResult
    public static func cleanupTemporaryFiles(for url: URL, olderThan age: TimeInterval) -> Int {
        let directory = url.deletingLastPathComponent()
        let prefix = "." + url.lastPathComponent + "."
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: directory.path) else { return 0 }
        var removed = 0
        for name in names where name.hasPrefix(prefix) && name.hasSuffix(".tmp") {
            let path = directory.appendingPathComponent(name)
            let modified = (try? FileManager.default.attributesOfItem(atPath: path.path)[.modificationDate] as? Date) ?? Date()
            if Date().timeIntervalSince(modified) >= age, (try? FileManager.default.removeItem(at: path)) != nil { removed += 1 }
        }
        return removed
    }
}

/// The sermon library on disk. Unreadable archives are preserved and block writes; every change is a
/// transaction that is rolled back if it cannot be saved.
public final class LibraryStore {
    public let url: URL
    public var records: [SermonRecord] = []
    public private(set) var loadError: String?
    public private(set) var preservedCopy: URL?
    private var sessionBackupTaken = false

    public init(url: URL) { self.url = url }

    public static var defaultURL: URL {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return support.appendingPathComponent("SermonStudio/archive.json")
    }

    public var isWriteBlocked: Bool { loadError != nil }
    public var sessionBackupURL: URL { url.deletingLastPathComponent().appendingPathComponent(url.lastPathComponent + ".session-backup.json") }

    public func load() {
        AtomicFile.cleanupTemporaryFiles(for: url, olderThan: 3600)
        records = []
        loadError = nil
        preservedCopy = nil
        guard FileManager.default.fileExists(atPath: url.path) else { return }
        do {
            records = try ArchiveCodec.decodeLibrary(Data(contentsOf: url))
        } catch {
            loadError = error.localizedDescription
            let stamp = Self.stamp()
            let copy = url.deletingLastPathComponent().appendingPathComponent(url.lastPathComponent + ".unreadable-" + stamp)
            if (try? FileManager.default.copyItem(at: url, to: copy)) != nil { preservedCopy = copy }
        }
    }

    public func save() throws {
        guard !isWriteBlocked else { throw ArchiveError.writesBlocked }
        if !sessionBackupTaken, FileManager.default.fileExists(atPath: url.path) {
            try AtomicFile.write(Data(contentsOf: url), to: sessionBackupURL)
        }
        sessionBackupTaken = true
        try AtomicFile.write(ArchiveCodec.encodeLibrary(records), to: url)
    }

    /// Applies a change and saves it. On failure the in-memory library is restored exactly and the error returned.
    @discardableResult
    public func transaction(_ change: (inout [SermonRecord]) throws -> Void) -> Error? {
        let snapshot = records
        do {
            try change(&records)
            try save()
            return nil
        } catch {
            records = snapshot
            return error
        }
    }

    /// Writes a standalone recovery archive beside the library (used before deletions).
    public func writeRecoveryArchive(_ records: [SermonRecord], label: String) throws -> URL {
        let file = url.deletingLastPathComponent().appendingPathComponent(url.lastPathComponent + "." + label + "-" + Self.stamp() + ".json")
        try AtomicFile.write(ArchiveCodec.encodeLibrary(records), to: file)
        return file
    }

    /// After an unreadable archive was preserved, the user may start a library from a backup.
    public func unblockAfterPreservedFailure() -> Bool {
        guard loadError != nil, preservedCopy != nil else { return false }
        loadError = nil
        return true
    }

    static func stamp() -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyyMMdd-HHmmss"
        return formatter.string(from: Date()) + "-" + String(UUID().uuidString.prefix(6))
    }
}
