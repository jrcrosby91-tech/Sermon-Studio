using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SermonStudio;

/// <summary>Version comparison and package validation for the Windows test-build updater.</summary>
internal static partial class UpdateRules
{
    internal const string AssetName = "Sermon-Studio-Windows-x64-portable.zip";
    internal const string DownloadPrefix = "https://github.com/jrcrosby91-tech/Sermon-Studio/releases/download/";
    internal const string ReleasesApi = "https://api.github.com/repos/jrcrosby91-tech/Sermon-Studio/releases?per_page=30";
    internal const long MaxPackageBytes = 1L << 30; // 1 GiB guard against a runaway download or zip bomb.

    [GeneratedRegex(@"^windows-test-(\d{1,9})-(\d{1,4})$")]
    private static partial Regex TagPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();

    internal static bool IsReleaseTag(string? tag) => tag != null && TagPattern().IsMatch(tag.Trim());

    /// <summary>Returns (0,0) for unknown or malformed tags.</summary>
    internal static (int Run, int Attempt) BuildNumber(string? tag)
    {
        var match = TagPattern().Match(tag?.Trim() ?? "");
        return match.Success && int.TryParse(match.Groups[1].Value, out var run) && int.TryParse(match.Groups[2].Value, out var attempt) ? (run, attempt) : (0, 0);
    }

    /// <summary>True when <paramref name="candidate"/> is a valid build newer than the installed one. An unknown installed build (developer build) treats any valid release as available.</summary>
    internal static bool IsNewer(string? candidate, string? installed)
    {
        var next = BuildNumber(candidate);
        if (next == (0, 0)) return false;
        var current = BuildNumber(installed);
        return current == (0, 0) || next.CompareTo(current) > 0;
    }

    internal static void ValidateDownloadUrl(string? url)
    {
        if (url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || url.Contains("..", StringComparison.Ordinal) || !url.EndsWith("/" + AssetName, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected download URL.");
    }

    /// <summary>Extracts the hex SHA-256 from a GitHub asset digest ("sha256:…").</summary>
    internal static string RequireSha256(string? digest)
    {
        if (digest == null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !Sha256Pattern().IsMatch(digest[7..]))
            throw new InvalidDataException("Release has no valid SHA-256 digest.");
        return digest[7..].ToLowerInvariant();
    }

    /// <summary>
    /// Rejects archives with absolute paths, parent-directory segments, drive letters, alternate data streams
    /// or entries that resolve outside the destination, then extracts them.
    /// </summary>
    internal static void SafeExtract(string zipPath, string destination)
    {
        var root = Path.GetFullPath(destination);
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryName(entry.FullName, root);
            total += entry.Length;
            if (total > MaxPackageBytes * 3) throw new InvalidDataException("Update package expands beyond the allowed size.");
        }
        Directory.CreateDirectory(root);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    internal static void ValidateEntryName(string name, string rootWithSeparator)
    {
        var normalized = name.Replace('\\', '/');
        if (normalized.Length == 0 || normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(part => part == ".."))
            throw new InvalidDataException("Unsafe path in update package: " + name);
        var target = Path.GetFullPath(Path.Combine(rootWithSeparator, normalized));
        if (!target.StartsWith(rootWithSeparator, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Unsafe path in update package: " + name);
    }

    internal static bool IsCompletePackage(string folder) =>
        File.Exists(Path.Combine(folder, "SermonStudio.exe"))
        && (File.Exists(Path.Combine(folder, "SermonStudio.pri")) || File.Exists(Path.Combine(folder, "resources.pri")));
}
