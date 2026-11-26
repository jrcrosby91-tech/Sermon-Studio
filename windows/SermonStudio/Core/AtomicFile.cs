using System.Text;

namespace SermonStudio;

/// <summary>
/// Crash-safe file replacement. Content is written to a uniquely named sibling file,
/// flushed to the physical disk and then renamed over the destination, so a power loss
/// or failed write leaves either the complete old file or the complete new file.
/// </summary>
internal static class AtomicFile
{
    internal static void WriteAllText(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? throw new IOException("Invalid destination: " + path);
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(false).GetBytes(contents ?? "");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            // Same-directory rename with replace is atomic on NTFS and POSIX file systems and
            // never leaves the destination missing.
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    /// <summary>Deletes stale temporary files left behind by an interrupted write.</summary>
    internal static int CleanupTemporaryFiles(string path, TimeSpan olderThan)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (directory == null || !Directory.Exists(directory)) return 0;
        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory, Path.GetFileName(full) + ".*.tmp"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= olderThan) { File.Delete(file); removed++; }
            }
            catch { }
        }
        return removed;
    }

    /// <summary>
    /// Removes temporary audio files left in the temp folder by an earlier crash (transcription WAVs can exceed 200 MB
    /// for a two-hour recording). Only files matching Sermon Studio's own name patterns and older than <paramref name="olderThan"/> are removed.
    /// </summary>
    internal static int CleanupStaleTempAudio(string tempFolder, TimeSpan olderThan)
    {
        int removed = 0;
        if (!Directory.Exists(tempFolder)) return 0;
        // Wildcards alone are not precise ('?' may match zero characters), so confirm the exact generated name.
        var generated = new System.Text.RegularExpressions.Regex(@"^sermon-(wave-)?[0-9a-f]{32}\.(wav|txt|srt|vtt|pcm)$");
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(tempFolder, "sermon-*").ToList(); } catch { return 0; }
        foreach (var file in files)
        {
            try
            {
                if (generated.IsMatch(Path.GetFileName(file)) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= olderThan) { File.Delete(file); removed++; }
            }
            catch { }
        }
        return removed;
    }
}
