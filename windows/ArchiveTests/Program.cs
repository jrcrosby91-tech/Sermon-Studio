using SermonStudio;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

// Regression tests for Sermon Studio's portable production logic.
// Every section runs even if an earlier one fails; the process exits with code 1 on any failure,
// which stops the Windows workflow before the release job can publish.
var failures = new List<string>();
int passed = 0;
void Check(bool condition, string name)
{
    if (condition) { passed++; Console.WriteLine("PASS: " + name); }
    else { failures.Add(name); Console.WriteLine("FAIL: " + name); }
}
void Section(string name, Action body)
{
    Console.WriteLine("== " + name);
    try { body(); }
    catch (Exception ex) { failures.Add(name + " threw " + ex.GetType().Name); Console.WriteLine("FAIL: " + name + " threw " + ex); }
}
bool Throws<T>(Action action) where T : Exception
{
    try { action(); return false; } catch (T) { return true; } catch { return false; }
}
var root = Path.Combine(Path.GetTempPath(), "sermon-studio-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string Dir(string name) { var d = Path.Combine(root, name); Directory.CreateDirectory(d); return d; }

Section("Archive normalization (legacy tests)", () =>
{
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var legacy = JsonSerializer.Deserialize<List<SermonRecord?>>("""[null,{"id":"same","title":"Faith","original":"Original text","outline":"**Faith**"},{"id":"same","title":null,"original":null},{"id":""}]""", options);
    var records = ArchiveRules.NormalizeRecords(legacy);
    Check(records.Count == 3, "Null records ignored");
    Check(records.Select(x => x.Id).Distinct().Count() == 3 && records.All(x => !string.IsNullOrWhiteSpace(x.Id)), "Unique nonempty identifiers");
    Check(records[0].Id == "same" && records[0].Outline == "**Faith**", "Existing identity and formatting preserved");
    Check(records[1].Title == "" && records[1].Original == "", "Legacy null text normalized");
    Check(ArchiveRules.NormalizeRecords(null).Count == 0, "Empty archive");
    var sermon = records[0]; sermon.Series = "Romans"; sermon.Collection = "Sunday"; sermon.Tags = "faith"; sermon.Favorite = true; sermon.Segments = new() { new() { Start = 1, End = 3, Text = "Faith" } };
    var json = JsonSerializer.Serialize(records);
    var restored = ArchiveRules.NormalizeRecords(JsonSerializer.Deserialize<List<SermonRecord?>>(json, options));
    Check(restored[0].Series == "Romans" && restored[0].Collection == "Sunday" && restored[0].Tags == "faith" && restored[0].Favorite, "Metadata round trip");
    Check(restored[0].Segments?.Single().Text == "Faith", "Timestamp round trip");
    Check(!json.Contains("LibraryPreview") && !json.Contains("LibraryHeading"), "Computed UI properties excluded");
    Check(restored[0].LibraryPreview == "Original text", "Preview text");
    Check(restored[0].ToString() == "★  Faith" && new SermonRecord().ToString() == "Untitled sermon", "Accessible item name is the sermon title");
    var missing = ArchiveRules.NormalizeRecords(JsonSerializer.Deserialize<List<SermonRecord?>>("""[{"speaker":null,"tags":null,"series":null,"collection":null,"primaryScripture":null}]""", options)).Single();
    Check(missing.Speaker == "" && missing.Tags == "" && missing.Series == "" && missing.Collection == "" && missing.PrimaryScripture == "", "Legacy null metadata normalized");
    Check(ArchiveRules.NormalizeRecords(restored).Select(x => x.Id).SequenceEqual(restored.Select(x => x.Id)), "Repeated normalization preserves identity");
});

Section("Library parsing and legacy migration", () =>
{
    Check(Throws<InvalidDataException>(() => ArchiveRules.ParseLibrary("")), "Empty file is an error, not an empty library");
    Check(Throws<InvalidDataException>(() => ArchiveRules.ParseLibrary("   \n")), "Whitespace file is an error");
    Check(Throws<InvalidDataException>(() => ArchiveRules.ParseLibrary("[{\"Id\":\"a\",\"Title\":\"Trunc")), "Truncated JSON is an error");
    Check(Throws<InvalidDataException>(() => ArchiveRules.ParseLibrary("{\"Id\":\"a\"}")), "Object root rejected");
    Check(Throws<InvalidDataException>(() => ArchiveRules.ParseLibrary("null")), "Null root rejected");
    Check(ArchiveRules.ParseLibrary("[]").Count == 0, "Empty list accepted");
    // Older Windows builds wrote PascalCase without metadata fields.
    var oldWindows = ArchiveRules.ParseLibrary("""[{"Id":"w1","Title":"Grace","Date":"2024-01-07","Original":"Text","Cleaned":"","Outline":"## Grace","Srt":"1\n00:00:01,000 --> 00:00:02,500\nHello\n","Vtt":""}]""");
    Check(oldWindows.Single().Id == "w1" && oldWindows[0].Speaker == "" && oldWindows[0].Segments == null && oldWindows[0].Outline == "## Grace", "Older Windows archive migrates");
    // macOS archives are camelCase, outline may be null and segment ids are uppercase UUIDs.
    var mac = ArchiveRules.ParseLibrary("""[{"id":"m1","title":"Hope","date":"2023-05-01","original":"O","cleaned":"C","outline":null,"segments":[{"id":"9F0B7B4C-6D3A-4E7E-8F3E-2A0F1B2C3D4E","start":0.5,"end":2,"text":"Hope","confidence":0.9}]}]""");
    Check(mac.Single().Outline == "" && mac[0].Segments!.Single().Start == 0.5 && mac[0].Segments![0].Confidence == 0.9, "macOS archive imports with segments");
    var full = new SermonRecord { Id = "f", Title = "Ünïcödé ✝ 恩典", Speaker = "Pastor", Tags = "a,b", Favorite = true, LastOpened = DateTimeOffset.Parse("2025-02-03T04:05:06Z"), Outline = "**B** *i*", Segments = new() { new() { Start = 1, End = 2, Text = "x" } } };
    var round = ArchiveRules.ParseLibrary(ArchiveRules.SerializeLibrary(new[] { full })).Single();
    Check(JsonSerializer.Serialize(round) == JsonSerializer.Serialize(full), "Library serialization round trip is lossless");
});

Section("Mac-compatible archive", () =>
{
    var records = new List<SermonRecord>
    {
        new() { Id = "a", Title = "T", Date = "2024-01-01", Original = "O", Outline = "## H", Srt = "1\n00:00:01,000 --> 00:00:02,000\nLine\n" },
        new() { Id = "b", Title = "U", Segments = new() { new() { Id = "not-a-uuid", Start = 3, End = 4, Text = "Seg" }, new() { Id = Guid.NewGuid().ToString(), Start = double.NaN, End = 5, Text = "N" } } },
    };
    var json = ArchiveRules.SerializeMacArchive(records);
    using var doc = JsonDocument.Parse(json);
    var first = doc.RootElement[0];
    Check(new[] { "id", "title", "date", "original", "cleaned", "outline", "segments" }.All(k => first.TryGetProperty(k, out _)), "Mac keys are camelCase");
    Check(!json.Contains("\"Id\"") && !json.Contains("\"Title\""), "No PascalCase keys");
    Check(first.GetProperty("cleaned").GetString() == "", "Required strings are non-null");
    Check(first.GetProperty("segments")[0].GetProperty("text").GetString() == "Line", "Segments derived from SRT");
    var segs = doc.RootElement[1].GetProperty("segments");
    Check(segs.EnumerateArray().All(s => Guid.TryParse(s.GetProperty("id").GetString(), out _)), "Segment ids are valid UUIDs for Swift");
    Check(segs[1].GetProperty("start").GetDouble() == 0, "Non-finite timestamps sanitized");
    var back = ArchiveRules.ParseLibrary(json);
    Check(back.Count == 2 && back[0].Outline == "## H" && back[1].Segments!.Count == 2, "Mac archive re-imports on Windows");
});

Section("Restore and backup merging", () =>
{
    var existing = new List<SermonRecord> { new() { Id = "1", Title = "Keep me", Outline = "mine" } };
    var plan = ArchiveRules.PlanRestore(existing, """[{"id":"1","title":"Overwrite attempt","outline":"theirs"},{"id":"2","title":"New","speaker":null}]""");
    Check(plan.Additions.Single().Id == "2" && plan.Skipped == 1 && plan.Total == 2, "Existing identifiers skipped, new ones added");
    Check(existing.Single().Title == "Keep me" && existing[0].Outline == "mine", "Restore never overwrites existing sermons");
    Check(plan.Additions[0].Speaker == "", "Restored legacy nulls normalized");
    Check(Throws<InvalidDataException>(() => ArchiveRules.PlanRestore(existing, """[{"id":"x"},{"id":"x"}]""")), "Duplicate identifiers rejected");
    Check(Throws<InvalidDataException>(() => ArchiveRules.PlanRestore(existing, """[{"id":""}]""")), "Blank identifier rejected");
    Check(Throws<InvalidDataException>(() => ArchiveRules.PlanRestore(existing, """[null]""")), "Null record rejected");
    Check(Throws<InvalidDataException>(() => ArchiveRules.PlanRestore(existing, """{"id":"x"}""")), "Non-list backup rejected");
    Check(Throws<InvalidDataException>(() => ArchiveRules.PlanRestore(existing, "not json")), "Malformed backup rejected");
    Check(Throws<InvalidDataException>(() => ArchiveRules.PlanRestore(existing, "")), "Empty backup rejected");
});

Section("Atomic writes", () =>
{
    var dir = Dir("atomic");
    var path = Path.Combine(dir, "sermons.json");
    AtomicFile.WriteAllText(path, "first ✝");
    Check(File.ReadAllText(path) == "first ✝", "New file written");
    Check(File.ReadAllBytes(path)[0] != 0xEF, "UTF-8 without byte-order mark");
    AtomicFile.WriteAllText(path, "second");
    Check(File.ReadAllText(path) == "second", "Existing file replaced");
    Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "No temporary files remain");
    var blocked = Path.Combine(dir, "blocked");
    Directory.CreateDirectory(blocked);
    File.WriteAllText(Path.Combine(blocked, "inside.txt"), "safe");
    Check(Throws<Exception>(() => AtomicFile.WriteAllText(blocked, "x")), "Failed replacement throws");
    Check(File.ReadAllText(Path.Combine(blocked, "inside.txt")) == "safe" && Directory.GetFiles(dir, "*.tmp").Length == 0, "Failed replacement leaves destination and no temp file");
    var stale = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    File.WriteAllText(stale, "partial");
    File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
    Check(AtomicFile.CleanupTemporaryFiles(path, TimeSpan.FromHours(1)) == 1 && !File.Exists(stale) && File.Exists(path), "Stale temporary files cleaned, library kept");
    var temp = Dir("temp-audio");
    var oldWav = Path.Combine(temp, "sermon-" + Guid.NewGuid().ToString("N") + ".wav");
    var newWav = Path.Combine(temp, "sermon-" + Guid.NewGuid().ToString("N") + ".wav");
    var unrelated = Path.Combine(temp, "sermon-notes.wav");
    foreach (var f in new[] { oldWav, newWav, unrelated }) File.WriteAllText(f, "x");
    File.SetLastWriteTimeUtc(oldWav, DateTime.UtcNow.AddDays(-2)); File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));
    Check(AtomicFile.CleanupStaleTempAudio(temp, TimeSpan.FromDays(1)) == 1 && !File.Exists(oldWav) && File.Exists(newWav) && File.Exists(unrelated), "Only stale Sermon Studio temp audio removed");
});

Section("Library load, session backup and failed-save recovery", () =>
{
    var dir = Dir("library");
    var path = Path.Combine(dir, "sermons.json");
    var missing = LibraryFile.Load(path);
    Check(!missing.Failed && missing.Records.Count == 0, "Missing library loads empty");
    File.WriteAllText(path, "[{\"Id\":\"a\",\"Title\":\"Trunc");
    var broken = LibraryFile.Load(path);
    Check(broken.Failed && broken.PreservedCopy != null && File.ReadAllText(broken.PreservedCopy) == File.ReadAllText(path), "Unreadable library preserved and reported");
    Check(File.ReadAllText(path) == "[{\"Id\":\"a\",\"Title\":\"Trunc", "Unreadable original left untouched");

    File.WriteAllText(path, ArchiveRules.SerializeLibrary(new[] { new SermonRecord { Id = "old", Title = "Before session" } }));
    bool taken = false;
    var library = LibraryFile.Load(path).Records;
    library.Add(new SermonRecord { Id = "new", Title = "During session" });
    LibraryFile.Save(path, library, ref taken);
    Check(taken && ArchiveRules.ParseLibrary(File.ReadAllText(LibraryFile.SessionBackupPath(path))).Single().Id == "old", "Session backup holds pre-session library");
    library[1].Title = "Edited";
    LibraryFile.Save(path, library, ref taken);
    Check(ArchiveRules.ParseLibrary(File.ReadAllText(LibraryFile.SessionBackupPath(path))).Single().Id == "old", "Session backup not overwritten by later saves");
    Check(LibraryFile.Load(path).Records.Select(x => x.Title).SequenceEqual(new[] { "Before session", "Edited" }), "Saved library reloads (restart)");

    var target = library[0];
    var before = ArchiveRules.SerializeLibrary(library);
    var error = LibraryFile.Transaction(library, new[] { target.Id }, () =>
    {
        target.Title = "Changed"; target.Outline = "changed";
        library.Add(new SermonRecord { Id = "added" });
        library.Remove(library[1]);
    }, () => throw new IOException("Disk full"));
    Check(error is IOException, "Failed save reports error");
    Check(ArchiveRules.SerializeLibrary(library) == before, "Failed save rolls library back exactly");
    Check(ReferenceEquals(library[0], target) && target.Title == "Before session", "Rollback keeps record objects used by the interface");
    var ok = LibraryFile.Transaction(library, new[] { target.Id }, () => target.Title = "Committed", () => LibraryFile.Save(path, library, ref taken));
    Check(ok == null && LibraryFile.Load(path).Records[0].Title == "Committed", "Successful transaction persists");
});

Section("Duplicate identifiers", () =>
{
    var records = ArchiveRules.ParseLibrary("""[{"Id":"d","Title":"One"},{"Id":"d","Title":"Two"},{"Id":"  ","Title":"Three"}]""");
    Check(records.Count == 3 && records.Select(r => r.Id).Distinct().Count() == 3, "Duplicate and blank ids reassigned on load");
    Check(records[0].Id == "d" && records[1].Title == "Two", "First identity kept, content of duplicates kept");
});

Section("Settings persistence", () =>
{
    var dir = Dir("prefs");
    var store = new PreferenceStore(dir);
    Check(store.GetOfflineModel() == "base.en" && store.GetDefaultGenerator() == 0 && store.GetOllamaModel() == PreferenceStore.DefaultOllamaModel, "Defaults when nothing saved");
    Check(store.TrySet(PreferenceStore.OfflineModel, "medium.en", out _) && store.TrySetDefaultGenerator(1, out _) && store.TrySet(PreferenceStore.OllamaModel, "qwen2.5:7b", out _), "Preferences written");
    store.TrySet(PreferenceStore.Appearance, "2", out _); store.TrySet(PreferenceStore.EditorSize, "23", out _); store.TrySet(PreferenceStore.AutoUpdateCheck, "1", out _);
    store.TrySet(PreferenceStore.Workspace, PreferenceStore.FormatWorkspace(.63, "Full workspace"), out _);
    var restarted = new PreferenceStore(dir);
    Check(restarted.GetOfflineModel() == "medium.en", "Offline model survives restart (no revert to base.en)");
    Check(restarted.GetDefaultGenerator() == 1 && restarted.GetOllamaModel() == "qwen2.5:7b", "Generator and Ollama model survive restart");
    Check(restarted.GetInt(PreferenceStore.Appearance, v => v is >= 0 and <= 2, 0) == 2, "Appearance survives restart");
    Check(restarted.GetInt(PreferenceStore.EditorSize, PreferenceStore.EditorSizes.Contains, 17) == 23, "Editor size survives restart");
    Check(restarted.GetBool(PreferenceStore.AutoUpdateCheck, false), "Update preference survives restart");
    var ws = PreferenceStore.ParseWorkspace(restarted.Get(PreferenceStore.Workspace), "Sermon outlines");
    Check(Math.Abs(ws.Split - .63) < 1e-9 && ws.Page == "Full workspace", "Workspace layout survives restart");
    // Files written by older builds (with Windows line endings or a BOM) still read correctly.
    File.WriteAllText(store.PathFor(PreferenceStore.Workspace), "0.4\r\nSermon library\r\n");
    File.WriteAllText(store.PathFor(PreferenceStore.OfflineModel), "\uFEFFsmall.en\r\n", new UTF8Encoding(true));
    Check(PreferenceStore.ParseWorkspace(store.Get(PreferenceStore.Workspace), "x").Page == "Sermon library" && store.GetOfflineModel() == "small.en", "Legacy preference files read");
    // Invalid values fall back instead of crashing or being used.
    store.TrySet(PreferenceStore.OfflineModel, "../../evil", out _); store.TrySet(PreferenceStore.EditorSize, "999", out _); store.TrySet(PreferenceStore.OllamaModel, "gpt-oss:120b-cloud", out _);
    store.TrySet(PreferenceStore.Workspace, "NaN\n", out _); store.TrySet(PreferenceStore.AutoUpdateCheck, "maybe", out _);
    Check(store.GetOfflineModel() == "base.en", "Unknown offline model falls back");
    Check(store.GetInt(PreferenceStore.EditorSize, PreferenceStore.EditorSizes.Contains, 17) == 17, "Out-of-range editor size falls back");
    Check(store.GetOllamaModel() == PreferenceStore.DefaultOllamaModel, "Cloud Ollama model rejected");
    Check(PreferenceStore.ParseWorkspace(store.Get(PreferenceStore.Workspace), "Sermon outlines") == (.5, "Sermon outlines"), "Invalid workspace falls back");
    Check(!store.GetBool(PreferenceStore.AutoUpdateCheck, false), "Invalid boolean falls back");
    Check(Throws<ArgumentException>(() => store.PathFor("../x")), "Preference names cannot escape the folder");
    var exe = Path.Combine(dir, "whisper-cli.exe"); File.WriteAllText(exe, "");
    store.TrySet(PreferenceStore.WhisperPath, exe, out _);
    Check(store.GetExistingFile(PreferenceStore.WhisperPath) == exe, "Custom engine path survives restart");
    File.Delete(exe);
    Check(store.GetExistingFile(PreferenceStore.WhisperPath) == null, "Missing custom engine path falls back to bundled engine");
    store.TrySet(PreferenceStore.FfmpegPath, "relative\\ffmpeg.exe", out _);
    Check(store.GetExistingFile(PreferenceStore.FfmpegPath) == null, "Relative engine path ignored");
    var readOnlyStore = new PreferenceStore(Path.Combine(dir, "sermons.json-is-a-file"));
    File.WriteAllText(readOnlyStore.Folder, "occupied");
    Check(!readOnlyStore.TrySet(PreferenceStore.Appearance, "1", out var writeError) && writeError != null, "Failed preference write reported");
});

Section("Markdown inline formatting", () =>
{
    var runs = OutlineMarkdown.ParseInline("Plain **bold** *italic* ***both*** end");
    Check(runs.SequenceEqual(new OutlineRun[] { new("Plain ", false, false), new("bold", true, false), new(" ", false, false), new("italic", false, true), new(" ", false, false), new("both", true, true), new(" end", false, false) }), "Bold, italic and combined parsed");
    Check(OutlineMarkdown.ParseInline("5 * 3 * 2").Single() == new OutlineRun("5 * 3 * 2", false, false), "Spaced asterisks stay literal");
    Check(OutlineMarkdown.ParseInline("**unclosed").Single().Text == "**unclosed", "Unclosed markers stay literal");
    Check(OutlineMarkdown.ParseInline(@"a \*literal\* b").Single().Text == "a *literal* b", "Escaped asterisks are literal");
    foreach (var sample in new[] { "**Grace** and *truth*", "***All***", "5 * 3", "x*y", "Pay **$5** *now*", "trailing *", @"esc \*star\*", "**a *b* c**", "" })
        Check(OutlineMarkdown.ParseInline(OutlineMarkdown.SerializeInline(OutlineMarkdown.ParseInline(sample))).SequenceEqual(OutlineMarkdown.ParseInline(sample)), "Inline round trip: " + sample);
    Check(OutlineMarkdown.SerializeInline(new OutlineRun[] { new("a*b", true, false) }) == @"**a\*b**", "Literal star inside bold escaped");
    Check(OutlineMarkdown.SerializeInline(new OutlineRun[] { new("Say ", false, false), new(" this ", true, false) }) == "Say  **this** ", "Whitespace moved outside markers");
    Check(OutlineMarkdown.SerializeInline(new OutlineRun[] { new("5 * 3", false, false) }) == "5 * 3", "No needless escaping");
});

Section("Markdown block structure", () =>
{
    var lines = OutlineMarkdown.ParseDocument("# Title\r## Main\r\n### Sub\n> *Jn 3:16*\n- one\n* two\n1. first\n#hashtag\n    - indented");
    Check(lines[0].Block == OutlineBlock.Heading && lines[0].Level == 1 && lines[1].Level == 2 && lines[2].Level == 3, "Heading levels with CR, CRLF and LF");
    Check(lines[3].Block == OutlineBlock.Quote && lines[3].Runs.Single().Italic, "Scripture quotation");
    Check(lines[4].Block == OutlineBlock.Bullet && lines[5].Block == OutlineBlock.Bullet && lines[5].PlainText == "two", "Both bullet styles");
    Check(lines[6].Block == OutlineBlock.Numbered && lines[6].Marker == "1. " && lines[6].PlainText == "first", "Numbered list");
    Check(lines[7].Block == OutlineBlock.Paragraph && lines[7].PlainText == "#hashtag", "Hash without space is text");
    Check(lines[8].Block == OutlineBlock.Bullet, "Indented bullet");
    Check(OutlineMarkdown.SplitLines("a\rb\r\nc\nd").Length == 4, "WinUI carriage-return line breaks split");
    Check(OutlineMarkdown.NormalizeNewlines("a\rb\r\nc") == "a\nb\nc", "Line endings normalized");
});

Section("Visual editor round trips", () =>
{
    string[] samples =
    {
        "## Introduction\n**Grace** is *unearned*.\n\n### Point one\n> For God so loved the world\n- First\n* Second\n1. Numbered\n# Title stays\n#### Deep",
        "Plain text only",
        "",
        "\n\n",
        "## #1 priority",
        "> **Bold** in quote",
        "- ***both*** in bullet",
        "Ünïcödé ✝ 恩典 — “quotes” 🙏🏽",
        "5 * 3 = 15 and **x**",
    };
    foreach (var md in samples)
    {
        var visual = OutlineMarkdown.ToVisual(md);
        var stored = OutlineMarkdown.RoundTrip(md);
        Check(OutlineMarkdown.MergeVisualEdit(md, stored) == OutlineMarkdown.NormalizeNewlines(md), "Unchanged visual round trip preserves Markdown: " + md.Replace("\n", "⏎"));
        Check(OutlineMarkdown.RoundTrip(stored) == stored, "Visual storage is idempotent: " + md.Replace("\n", "⏎"));
        Check(visual.Text.Split('\r').Length == OutlineMarkdown.SplitLines(md).Length, "Line count preserved: " + md.Replace("\n", "⏎"));
    }
    Check(OutlineMarkdown.ToVisual("## #1 priority").Text == "#1 priority", "Heading text beginning with # is not stripped");
    var v = OutlineMarkdown.ToVisual("## Head\n**b** *i*\n> q");
    Check(v.Spans.Any(s => s.Size == OutlineMarkdown.HeadingSize(17) && s.Bold), "Heading span sized and bold");
    Check(v.Spans.Any(s => s.Bold && !s.Italic && v.Text[s.Start..s.End] == "b") && v.Spans.Any(s => s.Italic && v.Text[s.Start..s.End] == "i"), "Bold and italic spans");
    Check(v.Text.EndsWith("❯ q") && v.Spans.Last().Italic, "Quote shown with marker and italic");
    var large = OutlineMarkdown.ToVisual("## Head", 26);
    Check(large.Spans[0].Size == OutlineMarkdown.HeadingSize(26) && OutlineMarkdown.RoundTrip("## Head", 26) == "## Head", "Headings scale with editor text size");
    // Editing one line keeps original spelling of untouched lines.
    var original = "* alpha\n## Beta\n#### gamma\n\\*literal\\*";
    var edited = "- alpha\n## Beta changed\n#### gamma\n*literal*";
    var merged = OutlineMarkdown.MergeVisualEdit(original, OutlineMarkdown.RoundTrip(edited));
    Check(merged.Split('\n')[0] == "* alpha" && merged.Split('\n')[1] == "## Beta changed" && merged.Split('\n')[2] == "#### gamma", "Only edited lines change");
    var inserted = OutlineMarkdown.MergeVisualEdit("* a\n* c", "- a\n- b\n- c");
    Check(inserted == "* a\n- b\n* c", "Inserted line merges between preserved lines");
    Check(OutlineMarkdown.MergeVisualEdit("* a\n* b", "- b") == "* b", "Deleted line removed, others preserved");
    Check(OutlineMarkdown.LineFromVisual("Heading", 23, 17, _ => (true, false)) == "## Heading", "Visual heading detected");
    Check(OutlineMarkdown.LineFromVisual("Sub", 19, 17, _ => (true, false)) == "### Sub", "Visual subheading detected");
    Check(OutlineMarkdown.LineFromVisual("• item", 17, 17, i => (i >= 2, false)) == "- **item**", "Visual bullet with bold");
});

Section("Scripture reference parsing", () =>
{
    var found = ScriptureReferences.Distinct("Read John 3:16 and 1 John 4:8, then 2 Cor 5:17-21, Ps 23, Psalm 119:105, Song of Solomon 2:4, I Peter 5:7, Second Timothy 3:16 and Jude 3.");
    Check(found.SequenceEqual(new[] { "John 3:16", "1 John 4:8", "2 Corinthians 5:17-21", "Psalm 23", "Psalm 119:105", "Song of Solomon 2:4", "1 Peter 5:7", "2 Timothy 3:16", "Jude 1:3" }), "Books, numbers, abbreviations and ranges: " + string.Join(" | ", found));
    Check(ScriptureReferences.Distinct("1 John 3:16").Single() == "1 John 3:16", "1 John is not reported as John");
    Check(ScriptureReferences.Distinct("Mark 25:1 Genesis 51 Acts 0").Count == 0, "Impossible chapters rejected");
    Check(ScriptureReferences.Distinct("Corinthians 13").Count == 0, "Unnumbered numbered-book rejected");
    Check(ScriptureReferences.Distinct("I am here. I went 3 times. Johnathan 3:4").Count == 0, "Prose not detected");
    Check(ScriptureReferences.Distinct("Romans 8:28-9:2").Single() == "Romans 8:28-9:2", "Cross-chapter range");
    Check(ScriptureReferences.Distinct("John 3:16 john 3:16").Count == 1, "Case-insensitive de-duplication");
    Check(ScriptureReferences.Find("See Romans 8", requireVerse: true).Count == 0 && ScriptureReferences.Find("See Romans 8").Count == 1, "Verse requirement for previews");
    var match = ScriptureReferences.Find("Text: Eph. 2:8-9!").Single();
    Check("Text: Eph. 2:8-9!".Substring(match.Index, match.Length) == "Eph. 2:8-9" && match.Canonical == "Ephesians 2:8-9", "Match position covers reference text");
    var link = ScriptureReferences.PassageLink(match);
    Check(link.Host == "www.biblegateway.com" && link.Query.Contains("version=KJV") && !link.Query.Contains("ESV"), "Links use public-domain KJV, not ESV");
    Check(ScriptureReferences.PreviewUrl(match).StartsWith("https://bible-api.com/Ephesians%202%3A8-9"), "Preview sends only the reference");
});

Section("Subtitle parsing", () =>
{
    var srt = "1\r\n00:00:01,000 --> 00:00:04,250\r\nIn the beginning\r\nwas the Word\r\n\r\n2\r\n01:02:03,004 --> 01:02:05,000\r\nSecond\r\n";
    var segs = Subtitles.Parse(srt);
    Check(segs.Count == 2 && segs[0].Start == 1 && segs[0].End == 4.25 && segs[0].Text == "In the beginning\nwas the Word", "SRT with CRLF and multi-line cue");
    Check(Math.Abs(segs[1].Start - 3723.004) < 1e-9, "Hour timestamps");
    var vtt = "WEBVTT\n\nNOTE header\n\n00:01.500 --> 00:03.000\nShort form\n\n00:00:04.000 --> 00:00:05.000 align:start\nWith settings";
    var vsegs = Subtitles.Parse(vtt);
    Check(vsegs.Count == 2 && vsegs[0].Start == 1.5 && vsegs[1].Text == "With settings", "WebVTT with short timestamps and cue settings");
    Check(Subtitles.Parse("garbage\n99:99:99,999 --> x\n\n").Count == 0 && Subtitles.Parse(null).Count == 0, "Malformed subtitles ignored");
    var made = Subtitles.Make(segs, false);
    var again = Subtitles.Parse(made);
    Check(again.Count == 2 && again.Zip(segs).All(p => Math.Abs(p.First.Start - p.Second.Start) < 1e-9 && p.First.Text == p.Second.Text), "SRT write/read round trip");
    Check(Subtitles.Make(segs, true).StartsWith("WEBVTT\n\n00:00:01.000 --> 00:00:04.250"), "VTT output format");
    Check(Subtitles.FormatTime(7322.9996, false) == "02:02:03,000" && Subtitles.FormatTime(-5, true) == "00:00:00.000", "Millisecond rounding and clamping");
    Check(Subtitles.Parse(Subtitles.Make(new[] { new SermonSegment { Start = 1, End = 2, Text = "a\n\nb" } }, false)).Single().Text == "a\nb", "Blank lines inside cue cannot split it");
});

Section("Export filename sanitization", () =>
{
    Check(ExportPaths.SanitizeFileName("Faith: Hope / Love?") == "Faith_ Hope _ Love_", "Invalid characters replaced");
    Check(ExportPaths.SanitizeFileName("CON") == "_CON" && ExportPaths.SanitizeFileName("lpt1.notes") == "_lpt1.notes", "Reserved device names avoided");
    Check(ExportPaths.SanitizeFileName("Ending dots... ") == "Ending dots", "Trailing dots and spaces removed");
    Check(ExportPaths.SanitizeFileName("   ") == "Sermon" && ExportPaths.SanitizeFileName(null) == "Sermon" && ExportPaths.SanitizeFileName("???") == "Sermon", "Empty title falls back");
    Check(ExportPaths.SanitizeFileName("Tab\there\nnew") == "Tab here new", "Control characters and line breaks removed");
    Check(ExportPaths.SanitizeFileName("恩典与真理 ✝ Gnade") == "恩典与真理 ✝ Gnade", "Unicode titles kept");
    var longName = ExportPaths.SanitizeFileName(new string('a', 79) + "🙏🏽tail", maxLength: 80);
    Check(longName.Length <= 80 && !char.IsHighSurrogate(longName[^1]), "Truncation keeps surrogate pairs whole");
    var dir = Dir("exports");
    var p1 = ExportPaths.ReserveNewFile(dir, "Sermon", ".md");
    File.WriteAllText(p1, "existing work");
    var p2 = ExportPaths.ReserveNewFile(dir, "Sermon", ".md");
    Check(p1 != p2 && Path.GetFileName(p2) == "Sermon (2).md" && File.ReadAllText(p1) == "existing work", "Exports never overwrite existing files");
    File.WriteAllText(Path.Combine(dir, "Talk.docx"), "x");
    Check(ExportPaths.UniqueBaseName(dir, "Talk", ".txt", ".docx") == "Talk (2)", "Multi-file export set gets one shared unique name");
    Check(ExportPaths.UniqueBaseName(dir, "Fresh", ".txt", ".docx") == "Fresh", "Unused name kept");
});

Section("Document exports", () =>
{
    var dir = Dir("documents");
    var docx = Path.Combine(dir, "o.docx");
    var outline = "## Main point\n**Bold** and *italic* and ***both***\n> For God so loved\n- bullet\n1. numbered\nÜnïcödé ✝ 恩典 🙏🏽\u0001\uD800";
    DocumentWriters.WriteDocx(docx, "Title ✝", outline, formatted: true);
    using (var zip = ZipFile.OpenRead(docx))
    {
        var xml = new StreamReader(zip.GetEntry("word/document.xml")!.Open()).ReadToEnd();
        Check(zip.GetEntry("[Content_Types].xml") != null && zip.GetEntry("_rels/.rels") != null, "DOCX package parts present");
        var parsed = System.Xml.Linq.XDocument.Parse(xml);
        Check(parsed.Root != null, "DOCX XML well-formed despite control characters and lone surrogates");
        Check(xml.Contains("<w:b /><w:i />") || xml.Contains("<w:b/><w:i/>"), "DOCX combined bold italic");
        Check(xml.Contains(">Bold<") && xml.Contains(">italic<") && !xml.Contains("**"), "DOCX markers converted to formatting");
        Check(xml.Contains("Ünïcödé ✝ 恩典 🙏🏽"), "DOCX Unicode preserved");
        Check(xml.Contains("• ") && xml.Contains("1. ") && xml.Contains("w:left=\"360\""), "DOCX bullets, numbering and quote indent");
    }
    DocumentWriters.WriteDocx(Path.Combine(dir, "empty.docx"), "", "", formatted: true);
    Check(new FileInfo(Path.Combine(dir, "empty.docx")).Length > 0, "Empty DOCX export succeeds");
    var rtf = DocumentWriters.BuildRtf("Title", outline + "\n{braces} \\slash", formatted: true);
    var unescaped = rtf.Replace(@"\\", "").Replace(@"\{", "").Replace(@"\}", "");
    Check(rtf.StartsWith(@"{\rtf1") && rtf.EndsWith("}") && unescaped.Count(c => c == '{') == unescaped.Count(c => c == '}'), "RTF groups balanced");
    Check(rtf.Contains(@"{\b Bold}") && rtf.Contains(@"{\i italic}") && rtf.Contains(@"{\b\i both}"), "RTF bold, italic and combined");
    Check(rtf.Contains(@"\u-10179?\u-8625?"), "RTF encodes astral characters as UTF-16 surrogate units");
    Check(rtf.Contains(@"\{braces\}") && rtf.Contains(@"\\slash"), "RTF special characters escaped");
    Check(!rtf.Any(c => c > 127), "RTF output is pure ASCII");
    Check(DocumentWriters.BuildRtf(null, "a\rb", false).Contains(@"a\par b\par"), "RTF keeps WinUI carriage-return paragraphs");
    var longText = string.Join("\n", Enumerable.Range(0, 20000).Select(i => "Line " + i + " **bold**"));
    DocumentWriters.WriteDocx(Path.Combine(dir, "long.docx"), "Long", longText, formatted: true);
    Check(new FileInfo(Path.Combine(dir, "long.docx")).Length > 10000, "Long document export");
});

Section("Update version comparison and package safety", () =>
{
    Check(UpdateRules.BuildNumber("windows-test-12-3") == (12, 3) && UpdateRules.BuildNumber("v1.0") == (0, 0) && UpdateRules.BuildNumber(null) == (0, 0), "Tag parsing");
    Check(UpdateRules.IsNewer("windows-test-10-1", "windows-test-9-4"), "Numeric, not lexical, comparison");
    Check(UpdateRules.IsNewer("windows-test-9-2", "windows-test-9-1") && !UpdateRules.IsNewer("windows-test-9-1", "windows-test-9-1") && !UpdateRules.IsNewer("windows-test-8-9", "windows-test-9-1"), "Attempt ordering and no downgrade");
    Check(UpdateRules.IsNewer("windows-test-1-1", null) && UpdateRules.IsNewer("windows-test-1-1", "garbage") && !UpdateRules.IsNewer("evil", null), "Unknown installed build and invalid candidates");
    Check(UpdateRules.RequireSha256("sha256:" + new string('A', 64)) == new string('a', 64), "Digest parsed");
    Check(Throws<InvalidDataException>(() => UpdateRules.RequireSha256("sha256:xyz")) && Throws<InvalidDataException>(() => UpdateRules.RequireSha256(null)), "Missing or malformed digest rejected");
    UpdateRules.ValidateDownloadUrl(UpdateRules.DownloadPrefix + "windows-test-5-1/" + UpdateRules.AssetName);
    Check(Throws<InvalidDataException>(() => UpdateRules.ValidateDownloadUrl("https://evil.example/" + UpdateRules.AssetName)), "Foreign download host rejected");
    Check(Throws<InvalidDataException>(() => UpdateRules.ValidateDownloadUrl(UpdateRules.DownloadPrefix + "../x/" + UpdateRules.AssetName)), "Path traversal in URL rejected");
    var dir = Dir("update");
    var good = Path.Combine(dir, "good.zip");
    using (var zip = ZipFile.Open(good, ZipArchiveMode.Create)) { using (var w = new StreamWriter(zip.CreateEntry("SermonStudio.exe").Open())) w.Write("exe"); using (var w = new StreamWriter(zip.CreateEntry("engines/whisper/x.dll").Open())) w.Write("dll"); using (var w = new StreamWriter(zip.CreateEntry("SermonStudio.pri").Open())) w.Write("pri"); }
    var outDir = Path.Combine(dir, "out");
    UpdateRules.SafeExtract(good, outDir);
    Check(UpdateRules.IsCompletePackage(outDir) && File.Exists(Path.Combine(outDir, "engines", "whisper", "x.dll")), "Valid package extracted");
    foreach (var bad in new[] { "../escape.txt", "sub/../../escape.txt", "/abs.txt", "C:/abs.txt", "file.txt:stream" })
    {
        var zipPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) using (var w = new StreamWriter(zip.CreateEntry(bad).Open())) w.Write("x");
        var target = Path.Combine(dir, "bad-" + Guid.NewGuid().ToString("N"));
        Check(Throws<InvalidDataException>(() => UpdateRules.SafeExtract(zipPath, target)) && !File.Exists(Path.Combine(dir, "escape.txt")) && !Directory.Exists(target), "Unsafe archive path rejected: " + bad);
    }
    Check(!UpdateRules.IsCompletePackage(Path.Combine(dir, "missing")), "Incomplete package detected");
});

Section("Asynchronous operation isolation", () =>
{
    var guard = new WorkspaceGuard();
    var ticket = guard.Capture("sermon-a", "transcript", "outline");
    Check(guard.IsCurrent(ticket, "sermon-a", "transcript", "outline"), "Unchanged workspace accepts result");
    Check(!guard.IsCurrent(ticket, "sermon-b", "transcript", "outline"), "Result rejected after switching sermons");
    Check(!guard.IsCurrent(ticket, "sermon-a", "transcript", "edited outline"), "Result rejected after user edits");
    guard.Advance();
    Check(!guard.IsCurrent(ticket, "sermon-a", "transcript", "outline"), "Result rejected after New/switch/restore even when content matches");
    var results = new System.Collections.Concurrent.ConcurrentBag<bool>();
    var live = new WorkspaceGuard();
    var tickets = Enumerable.Range(0, 50).Select(i => live.Capture("s" + i)).ToList();
    Parallel.For(0, 50, i => { if (i == 25) live.Advance(); });
    foreach (var t in tickets) results.Add(live.IsCurrent(t, t.SermonId));
    Check(results.All(r => !r), "Concurrent advance invalidates every in-flight result");
    var pending = live.Capture("x", "input");
    var delayed = Task.Run(async () => { await Task.Delay(30); return live.IsCurrent(pending, "x", "input"); });
    live.Advance();
    Check(!delayed.GetAwaiter().GetResult(), "Delayed completion checked against latest revision");
});

try { Directory.Delete(root, true); } catch { }
Console.WriteLine();
Console.WriteLine($"{passed} checks passed, {failures.Count} failed.");
if (failures.Count > 0)
{
    foreach (var failure in failures) Console.WriteLine("  FAILED: " + failure);
    return 1;
}
Console.WriteLine("All archive regression checks passed.");
return 0;
