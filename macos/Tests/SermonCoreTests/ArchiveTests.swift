import XCTest
@testable import SermonCore

func fixture(_ name: String) throws -> Data {
    let url = try XCTUnwrap(Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Fixtures"), "Missing fixture " + name)
    return try Data(contentsOf: url)
}

func temporaryDirectory() throws -> URL {
    let url = FileManager.default.temporaryDirectory.appendingPathComponent("sermon-tests-" + UUID().uuidString)
    try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    return url
}

final class ArchiveCompatibilityTests: XCTestCase {
    func testWindowsLibraryPascalCaseImportsEveryField() throws {
        let records = try ArchiveCodec.decodeLibrary(fixture("windows-library"))
        XCTAssertEqual(records.count, 2)
        let grace = records[0]
        XCTAssertEqual(grace.id, "6f1c2b0e-0d4b-4a63-9a0e-2f9e7d3c1a11")
        XCTAssertEqual(grace.title, "Grace That Saves")
        XCTAssertEqual(grace.date, "2026-03-08")
        XCTAssertEqual(grace.speaker, "Pastor Reed")
        XCTAssertEqual(grace.primaryScripture, "Ephesians 2:8-9")
        XCTAssertEqual(grace.series, "Ephesians")
        XCTAssertEqual(grace.collection, "Sunday Morning")
        XCTAssertEqual(grace.tags, "grace, salvation")
        XCTAssertTrue(grace.favorite)
        XCTAssertEqual(grace.cleaned, "For by grace you have been saved through faith.")
        XCTAssertTrue(grace.outline.contains("**Saved**"))
        XCTAssertEqual(grace.segments?.first?.end, 4.25)
        XCTAssertNil(grace.segments?.first?.confidence)
        XCTAssertTrue(grace.srt.contains("00:00:04,250"))
        XCTAssertTrue(grace.vtt.hasPrefix("WEBVTT"))
        // .NET DateTimeOffset with seven fractional digits and offsets.
        XCTAssertEqual(grace.lastOpened?.timeIntervalSince1970 ?? 0, ISO8601DateFormatter().date(from: "2026-11-20T14:03:11Z")!.timeIntervalSince1970 + 0.123, accuracy: 0.001)
        XCTAssertEqual(grace.lastEdited, ISO8601DateFormatter().date(from: "2026-11-21T14:15:00Z"))
        // The .NET default date (year 1) means "never".
        XCTAssertNil(records[1].lastOpened)
        XCTAssertNil(records[1].lastEdited)
        XCTAssertNil(records[1].segments)
        XCTAssertTrue(grace.extra.isEmpty, "Known Windows fields must not leak into extras")
    }

    func testWindowsMacCompatibleExportImports() throws {
        let record = try XCTUnwrap(ArchiveCodec.decodeLibrary(fixture("windows-mac-export")).first)
        XCTAssertEqual(record.segments?.first?.id, "0A6C3C0D-2B8A-41F6-9A45-6F0C38B8A7D2")
        XCTAssertEqual(record.speaker, "")
        XCTAssertFalse(record.favorite)
    }

    func testWebArchiveChunksAndUnknownFieldsArePreserved() throws {
        let record = try XCTUnwrap(ArchiveCodec.decodeLibrary(fixture("web-archive")).first)
        XCTAssertEqual(record.segments?.count, 1)
        XCTAssertEqual(record.segments?.first?.start, 1.5)
        XCTAssertEqual(record.segments?.first?.end, 3.25)
        XCTAssertEqual(record.segments?.first?.confidence, 0.91)
        XCTAssertEqual(record.collection, "Evening")
        XCTAssertNotNil(record.lastEdited)
        XCTAssertEqual(record.extra["webOnlyField"], .object(["nested": .array([.number(1), .number(2), .number(3)]), "flag": .bool(true)]))
        XCTAssertNotNil(record.extra["chunks"], "Web chunks are kept for the web app")
        // Save and reload: unknown fields survive.
        let again = try XCTUnwrap(ArchiveCodec.decodeLibrary(ArchiveCodec.encodeLibrary([record])).first)
        XCTAssertEqual(again, record)
        let json = try XCTUnwrap(String(data: ArchiveCodec.encodeLibrary([record]), encoding: .utf8))
        XCTAssertTrue(json.contains("\"webOnlyField\""))
    }

    func testLegacyMacArchiveMigrates() throws {
        let record = try XCTUnwrap(ArchiveCodec.decodeLibrary(fixture("legacy-mac-3.2")).first)
        XCTAssertEqual(record.id, "E5A1C3F2-6B7D-4E8F-9A0B-1C2D3E4F5A6B")
        XCTAssertEqual(record.outline, "")
        XCTAssertEqual(record.segments?.first?.id, "11111111-2222-3333-4444-555555555555")
        XCTAssertEqual(record.segments?.first?.confidence, -0.42)
        XCTAssertNil(record.lastEdited)
    }

    func testOutputIsReadableByOlderMacAndWindows() throws {
        var record = SermonRecord(id: "abc", title: "T", date: "2026-01-01", original: "O")
        record.segments = [SermonSegment(start: 1, end: 2, text: "x")]
        record.lastOpened = Date(timeIntervalSince1970: 1_700_000_000.5)
        let json = try XCTUnwrap(JSONSerialization.jsonObject(with: ArchiveCodec.encodeLibrary([record])) as? [[String: Any]]).first!
        // Older macOS releases require these keys; Windows reads them case-insensitively.
        for key in ["id", "title", "date", "original", "cleaned", "outline", "segments", "speaker", "favorite", "tags"] { XCTAssertNotNil(json[key], key) }
        XCTAssertEqual(json["lastOpened"] as? String, "2023-11-14T22:13:20.500Z")
        let segment = try XCTUnwrap((json["segments"] as? [[String: Any]])?.first)
        XCTAssertNotNil(UUID(uuidString: segment["id"] as? String ?? ""), "Segment ids decode as UUID in macOS 3.2")
    }

    func testFullRoundTripIsLossless() throws {
        let original = SermonRecord(id: "id-1", title: "Ünïcödé ✝ 恩典 🙏🏽", date: "2026-02-02", speaker: "S", primaryScripture: "John 3:16", series: "Series",
                                    collection: "C", tags: "a, b", favorite: true, lastEdited: Date(timeIntervalSince1970: 1_700_000_000.25),
                                    lastOpened: Date(timeIntervalSince1970: 1_700_000_100), original: "O\nline", cleaned: "C", outline: "## H\n**b**",
                                    segments: [SermonSegment(id: "s", start: 1.25, end: 2.5, text: "t", confidence: 0.5)], srt: "1\n", vtt: "WEBVTT\n",
                                    extra: ["futureField": .string("keep")])
        let decoded = try ArchiveCodec.decodeLibrary(ArchiveCodec.encodeLibrary([original]))
        XCTAssertEqual(decoded, [original])
    }

    func testStrictParsing() {
        for bad in ["", "   \n", "\u{FEFF}", "[{\"id\":\"a\",\"title\":\"Trunc", "{\"id\":\"a\"}", "null", "not json"] {
            XCTAssertThrowsError(try ArchiveCodec.decodeLibrary(Data(bad.utf8)), "Should reject: " + bad)
        }
        XCTAssertEqual(try ArchiveCodec.decodeLibrary(Data("[]".utf8)).count, 0)
        XCTAssertEqual(try ArchiveCodec.decodeLibrary(Data("\u{FEFF}[{\"id\":\"x\"}]".utf8)).first?.id, "x")
    }

    func testDuplicateAndMissingIdentifiers() throws {
        let records = try ArchiveCodec.decodeLibrary(Data("[null,{\"id\":\"d\",\"title\":\"One\"},{\"id\":\"d\",\"title\":\"Two\"},{\"id\":\"  \"},{\"title\":\"none\"}]".utf8))
        XCTAssertEqual(records.count, 4, "Null records ignored")
        XCTAssertEqual(Set(records.map(\.id)).count, 4)
        XCTAssertEqual(records[0].id, "d")
        XCTAssertEqual(records[1].title, "Two")
        XCTAssertTrue(records.allSatisfy { !$0.id.isEmpty })
    }

    func testLenientFieldTypes() throws {
        let record = try XCTUnwrap(ArchiveCodec.decodeLibrary(Data("[{\"id\":5,\"title\":null,\"favorite\":1,\"tags\":[\"a\",\"b\"],\"lastEdited\":1700000000000,\"segments\":\"garbage\"}]".utf8)).first)
        XCTAssertEqual(record.id, "5")
        XCTAssertEqual(record.title, "")
        XCTAssertTrue(record.favorite)
        XCTAssertEqual(record.tags, "a, b")
        XCTAssertEqual(record.lastEdited, Date(timeIntervalSince1970: 1_700_000_000))
        XCTAssertNil(record.segments)
        XCTAssertEqual(record.extra["segments"], .string("garbage"), "Malformed data is preserved, not discarded")
    }
}

final class RestoreTests: XCTestCase {
    func testRestoreNeverOverwritesExistingSermons() throws {
        let existing = [SermonRecord(id: "1", title: "Keep me", outline: "mine")]
        let plan = try ArchiveCodec.planRestore(existing: existing, data: Data("[{\"id\":\"1\",\"title\":\"Overwrite attempt\"},{\"id\":\"2\",\"title\":\"New\"},{\"title\":\"No id\"}]".utf8))
        XCTAssertEqual(plan.additions.map(\.title), ["New", "No id"])
        XCTAssertEqual(plan.conflicts.map(\.id), ["1"])
        XCTAssertEqual(existing[0].title, "Keep me")
        let copies = ArchiveCodec.copiesWithNewIdentifiers(plan.conflicts)
        XCTAssertNotEqual(copies[0].id, "1")
        XCTAssertEqual(copies[0].title, "Overwrite attempt (imported copy)")
    }

    func testRestoreRejectsInvalidBackups() {
        XCTAssertThrowsError(try ArchiveCodec.planRestore(existing: [], data: Data("[{\"id\":\"x\"},{\"id\":\"x\"}]".utf8))) { XCTAssertEqual($0 as? ArchiveError, .duplicateIdentifiers) }
        XCTAssertThrowsError(try ArchiveCodec.planRestore(existing: [], data: Data("{\"id\":\"x\"}".utf8)))
        XCTAssertThrowsError(try ArchiveCodec.planRestore(existing: [], data: Data()))
    }

    func testRestoreFromEveryPlatform() throws {
        for name in ["windows-library", "windows-mac-export", "web-archive", "legacy-mac-3.2"] {
            let plan = try ArchiveCodec.planRestore(existing: [], data: fixture(name))
            XCTAssertFalse(plan.additions.isEmpty, name)
        }
    }
}

final class PersistenceTests: XCTestCase {
    func testAtomicWriteReplacesAndCleansUp() throws {
        let dir = try temporaryDirectory()
        let url = dir.appendingPathComponent("archive.json")
        try AtomicFile.write(Data("first ✝".utf8), to: url)
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), "first ✝")
        try AtomicFile.write(Data("second".utf8), to: url)
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), "second")
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: dir.path).filter { $0.hasSuffix(".tmp") }.isEmpty)
    }

    func testFailedAtomicWriteLeavesDestination() throws {
        let dir = try temporaryDirectory()
        let blocked = dir.appendingPathComponent("blocked")
        try FileManager.default.createDirectory(at: blocked, withIntermediateDirectories: true)
        try Data("safe".utf8).write(to: blocked.appendingPathComponent("inside.txt"))
        XCTAssertThrowsError(try AtomicFile.write(Data("x".utf8), to: blocked))
        XCTAssertEqual(try String(contentsOf: blocked.appendingPathComponent("inside.txt"), encoding: .utf8), "safe")
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: dir.path).filter { $0.hasSuffix(".tmp") }.isEmpty)
    }

    func testStaleTemporaryFilesAreCleaned() throws {
        let dir = try temporaryDirectory()
        let url = dir.appendingPathComponent("archive.json")
        try Data("[]".utf8).write(to: url)
        let stale = dir.appendingPathComponent(".archive.json.\(UUID().uuidString).tmp")
        try Data("partial".utf8).write(to: stale)
        try FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSinceNow: -7200)], ofItemAtPath: stale.path)
        XCTAssertEqual(AtomicFile.cleanupTemporaryFiles(for: url, olderThan: 3600), 1)
        XCTAssertTrue(FileManager.default.fileExists(atPath: url.path))
    }

    func testUnreadableArchiveIsPreservedAndBlocksWrites() throws {
        let dir = try temporaryDirectory()
        let url = dir.appendingPathComponent("archive.json")
        let damaged = "[{\"id\":\"a\",\"title\":\"Trunc"
        try Data(damaged.utf8).write(to: url)
        let store = LibraryStore(url: url)
        store.load()
        XCTAssertTrue(store.isWriteBlocked)
        XCTAssertNotNil(store.loadError)
        let copy = try XCTUnwrap(store.preservedCopy)
        XCTAssertEqual(try String(contentsOf: copy, encoding: .utf8), damaged)
        store.records.append(SermonRecord(title: "New"))
        XCTAssertThrowsError(try store.save())
        XCTAssertNotNil(store.transaction { $0.append(SermonRecord(title: "Another")) })
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), damaged, "An unreadable archive is never replaced")
        XCTAssertTrue(store.unblockAfterPreservedFailure())
        store.records = [SermonRecord(id: "restored", title: "From backup")]
        try store.save()
        XCTAssertEqual(try ArchiveCodec.decodeLibrary(Data(contentsOf: url)).first?.id, "restored")
        XCTAssertEqual(try String(contentsOf: copy, encoding: .utf8), damaged, "Preserved copy stays")
    }

    func testSessionBackupAndRestartReload() throws {
        let dir = try temporaryDirectory()
        let url = dir.appendingPathComponent("archive.json")
        try ArchiveCodec.encodeLibrary([SermonRecord(id: "old", title: "Before session")]).write(to: url)
        let store = LibraryStore(url: url)
        store.load()
        XCTAssertNil(store.transaction { $0.append(SermonRecord(id: "new", title: "During")) })
        XCTAssertNil(store.transaction { $0[1].title = "Edited" })
        let backup = try ArchiveCodec.decodeLibrary(Data(contentsOf: store.sessionBackupURL))
        XCTAssertEqual(backup.map(\.id), ["old"], "Session backup holds the library from before this session")
        let restarted = LibraryStore(url: url)
        restarted.load()
        XCTAssertEqual(restarted.records.map(\.title), ["Before session", "Edited"])
    }

    func testFailedSaveRollsBackExactly() throws {
        let dir = try temporaryDirectory()
        let url = dir.appendingPathComponent("archive.json")
        let store = LibraryStore(url: url)
        store.load()
        XCTAssertNil(store.transaction { $0 = [SermonRecord(id: "1", title: "One"), SermonRecord(id: "2", title: "Two")] })
        let before = store.records
        // Make the destination unwritable by replacing it with a non-empty directory.
        try FileManager.default.removeItem(at: url)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        try Data("x".utf8).write(to: url.appendingPathComponent("keep"))
        let error = store.transaction { records in
            records[0].title = "Changed"
            records.remove(at: 1)
            records.append(SermonRecord(id: "3"))
        }
        XCTAssertNotNil(error)
        XCTAssertEqual(store.records, before)
    }

    func testRecoveryArchiveBeforeDeletion() throws {
        let dir = try temporaryDirectory()
        let store = LibraryStore(url: dir.appendingPathComponent("archive.json"))
        store.load()
        store.records = [SermonRecord(id: "1", title: "Delete me")]
        let recovery = try store.writeRecoveryArchive(store.records, label: "recovery")
        XCTAssertEqual(try ArchiveCodec.decodeLibrary(Data(contentsOf: recovery)).first?.title, "Delete me")
    }
}

final class SettingsTests: XCTestCase {
    private func makeDefaults() -> UserDefaults {
        let name = "SermonStudioTests-" + UUID().uuidString
        let defaults = UserDefaults(suiteName: name)!
        defaults.removePersistentDomain(forName: name)
        return defaults
    }

    func testDefaultsAndRestartPersistence() {
        let defaults = makeDefaults()
        let settings = SettingsStore(defaults: defaults)
        XCTAssertEqual(settings.model, "small.en")
        XCTAssertEqual(settings.outlineGenerator, .original)
        XCTAssertEqual(settings.ollamaModel, "llama3.2:3b")
        XCTAssertEqual(settings.fontSize, 18)
        settings.model = "medium.en"
        settings.outlineGenerator = .ollama
        settings.ollamaModel = "qwen2.5:7b"
        settings.fontSize = 21
        settings.appearance = .dark
        settings.workspaceSplit = 0.63
        settings.outlineMode = "markdown"
        settings.vocabulary = "Melchizedek, Nebuchadnezzar"
        let restarted = SettingsStore(defaults: defaults)
        XCTAssertEqual(restarted.model, "medium.en", "Model must not revert after restart")
        XCTAssertEqual(restarted.outlineGenerator, .ollama)
        XCTAssertEqual(restarted.ollamaModel, "qwen2.5:7b")
        XCTAssertEqual(restarted.fontSize, 21)
        XCTAssertEqual(restarted.appearance, .dark)
        XCTAssertEqual(restarted.workspaceSplit, 0.63, accuracy: 1e-9)
        XCTAssertEqual(restarted.outlineMode, "markdown")
        XCTAssertEqual(restarted.vocabulary, "Melchizedek, Nebuchadnezzar")
    }

    func testLegacyAndInvalidValues() {
        let defaults = makeDefaults()
        defaults.set("large-v3-turbo", forKey: SettingsStore.Key.model)
        defaults.set(999.0, forKey: SettingsStore.Key.fontSize)
        defaults.set("purple", forKey: SettingsStore.Key.appearance)
        defaults.set(Double.nan, forKey: SettingsStore.Key.workspaceSplit)
        defaults.set("bogus", forKey: SettingsStore.Key.outlineMode)
        let settings = SettingsStore(defaults: defaults)
        XCTAssertEqual(settings.model, "large-v3-v20240930_626MB", "Legacy turbo name maps to the current model")
        XCTAssertEqual(settings.fontSize, 24)
        XCTAssertEqual(settings.appearance, .system)
        XCTAssertEqual(settings.workspaceSplit, 0.5)
        XCTAssertEqual(settings.outlineMode, "visual")
        defaults.set("../../evil", forKey: SettingsStore.Key.model)
        XCTAssertEqual(settings.model, "small.en")
        settings.ollamaModel = "gpt-oss:120b-cloud"
        XCTAssertEqual(settings.ollamaModel, "llama3.2:3b", "Cloud models rejected")
        XCTAssertFalse(SettingsStore.isValidOllamaModel("x; rm -rf /"))
        XCTAssertTrue(SettingsStore.isValidOllamaModel("hf.co/user/model:Q4"))
    }

    func testModelCatalogIntegrityCheck() throws {
        let base = try temporaryDirectory()
        let folder = ModelCatalog.folder(for: "small.en", downloadBase: base)
        XCTAssertTrue(folder.path.hasSuffix("huggingface/models/argmaxinc/whisperkit-coreml/openai_whisper-small.en") || folder.path.hasSuffix("models/argmaxinc/whisperkit-coreml/openai_whisper-small.en"))
        XCTAssertEqual(ModelCatalog.installState(at: folder), .missing)
        for component in ModelCatalog.requiredComponents.dropLast() {
            let dir = folder.appendingPathComponent(component)
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            try Data("weights".utf8).write(to: dir.appendingPathComponent("coremldata.bin"))
        }
        XCTAssertEqual(ModelCatalog.installState(at: folder), .incomplete(["TextDecoder.mlmodelc"]))
        let last = folder.appendingPathComponent("TextDecoder.mlmodelc")
        try FileManager.default.createDirectory(at: last, withIntermediateDirectories: true)
        try Data().write(to: last.appendingPathComponent("coremldata.bin"))
        XCTAssertEqual(ModelCatalog.installState(at: folder), .incomplete(["TextDecoder.mlmodelc"]), "Empty component is incomplete")
        try Data("w".utf8).write(to: last.appendingPathComponent("coremldata.bin"))
        XCTAssertTrue(ModelCatalog.installState(at: folder).isInstalled)
    }
}
