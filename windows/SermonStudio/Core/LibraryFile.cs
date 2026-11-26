using System.Globalization;

namespace SermonStudio;

/// <summary>Loading, crash-safe saving and transactional changes for sermons.json.</summary>
internal static class LibraryFile
{
    internal sealed record LoadResult(List<SermonRecord> Records, bool Failed, string? Error, string? PreservedCopy);

    internal static string SessionBackupPath(string libraryPath) => libraryPath + ".session-backup.json";

    /// <summary>
    /// Loads the library. If it exists but cannot be read, a copy is preserved beside it and <see cref="LoadResult.Failed"/>
    /// is set so the caller blocks writes instead of replacing the user's sermons with an empty list.
    /// </summary>
    internal static LoadResult Load(string path)
    {
        if (!File.Exists(path)) return new LoadResult(new List<SermonRecord>(), false, null, null);
        try
        {
            return new LoadResult(ArchiveRules.ParseLibrary(File.ReadAllText(path)), false, null, null);
        }
        catch (Exception ex)
        {
            string? kept = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
            try { File.Copy(path, kept, overwrite: false); } catch { kept = null; }
            return new LoadResult(new List<SermonRecord>(), true, ex.Message, kept);
        }
    }

    /// <summary>
    /// Writes the library atomically. Before the first write of a session the previous file is copied to
    /// <see cref="SessionBackupPath"/>, giving a recovery point from before any of this session's edits.
    /// </summary>
    internal static void Save(string path, IReadOnlyCollection<SermonRecord> records, ref bool sessionBackupTaken)
    {
        if (!sessionBackupTaken && File.Exists(path))
        {
            AtomicFile.WriteAllText(SessionBackupPath(path), File.ReadAllText(path));
            sessionBackupTaken = true;
        }
        AtomicFile.WriteAllText(path, ArchiveRules.SerializeLibrary(records));
        sessionBackupTaken = true;
    }

    /// <summary>
    /// Runs <paramref name="change"/> then <paramref name="persist"/>. If either throws, the list membership/order
    /// and the contents of the records named in <paramref name="touchedIds"/> are restored in place, so objects
    /// referenced by the interface stay valid. Returns the exception, or null on success.
    /// </summary>
    internal static Exception? Transaction(List<SermonRecord> library, IEnumerable<string?> touchedIds, Action change, Action persist)
    {
        var order = library.ToList();
        var saved = touchedIds.Where(id => id != null).Distinct(StringComparer.Ordinal)
            .Select(id => library.FirstOrDefault(x => x.Id == id)).Where(x => x != null)
            .Select(x => (Target: x!, Copy: ArchiveRules.Clone(x!))).ToList();
        try
        {
            change();
            persist();
            return null;
        }
        catch (Exception ex)
        {
            library.Clear();
            library.AddRange(order);
            foreach (var (target, copy) in saved) CopyInto(copy, target);
            return ex;
        }
    }

    internal static void CopyInto(SermonRecord source, SermonRecord target)
    {
        target.Id = source.Id; target.Title = source.Title; target.Date = source.Date; target.Speaker = source.Speaker;
        target.PrimaryScripture = source.PrimaryScripture; target.Series = source.Series; target.Collection = source.Collection;
        target.LastOpened = source.LastOpened; target.Tags = source.Tags; target.Favorite = source.Favorite; target.LastEdited = source.LastEdited;
        target.Original = source.Original; target.Cleaned = source.Cleaned; target.Outline = source.Outline;
        target.Segments = source.Segments?.Select(s => new SermonSegment { Id = s.Id, Start = s.Start, End = s.End, Text = s.Text, Confidence = s.Confidence }).ToList();
        target.Srt = source.Srt; target.Vtt = source.Vtt;
    }
}
