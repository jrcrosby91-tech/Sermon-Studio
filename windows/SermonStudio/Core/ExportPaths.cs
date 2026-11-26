using System.Globalization;
using System.Text;

namespace SermonStudio;

/// <summary>File naming rules for exports. Never returns a path that already exists.</summary>
internal static class ExportPaths
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private const string Invalid = "\\/:*?\"<>|";

    /// <summary>
    /// Produces a Windows-safe file name stem (no extension) from a sermon title. Unicode letters are kept.
    /// </summary>
    internal static string SanitizeFileName(string? name, string fallback = "Sermon", int maxLength = 80)
    {
        var builder = new StringBuilder();
        foreach (var c in (name ?? "").Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c)) builder.Append(' ');
            else if (c < 32 || c == 127 || Invalid.IndexOf(c) >= 0) builder.Append('_');
            else builder.Append(c);
        }
        var value = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        value = TrimEnds(value);
        if (value.Length > maxLength)
        {
            // Cut on text-element boundaries so surrogate pairs and combining marks stay intact.
            var elements = StringInfo.GetTextElementEnumerator(value);
            var cut = new StringBuilder();
            while (elements.MoveNext() && cut.Length + elements.GetTextElement().Length <= maxLength) cut.Append(elements.GetTextElement());
            value = TrimEnds(cut.ToString());
        }
        if (value.Length == 0 || value.All(c => c == '_')) value = fallback;
        var stem = value.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem)) value = "_" + value;
        return value;
    }

    private static string TrimEnds(string value) => value.Trim().TrimEnd('.', ' ').Trim();

    /// <summary>
    /// Returns a base name such that every <paramref name="suffixes"/> file ("name" + suffix) is absent in the folder.
    /// Adds " (2)", " (3)" … when an export with the same title already exists.
    /// </summary>
    internal static string UniqueBaseName(string folder, string baseName, params string[] suffixes)
    {
        if (suffixes.Length == 0) suffixes = new[] { "" };
        for (int n = 1; n < 10000; n++)
        {
            var candidate = n == 1 ? baseName : baseName + " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
            if (suffixes.All(s => !File.Exists(Path.Combine(folder, candidate + s)) && !Directory.Exists(Path.Combine(folder, candidate + s))))
                return candidate;
        }
        return baseName + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// Atomically reserves a new, previously nonexistent file and returns its path. The caller then overwrites
    /// its own empty placeholder, so a concurrently created file can never be replaced.
    /// </summary>
    internal static string ReserveNewFile(string folder, string baseName, string extension)
    {
        Directory.CreateDirectory(folder);
        for (int n = 1; n < 10000; n++)
        {
            var candidate = Path.Combine(folder, (n == 1 ? baseName : baseName + " (" + n.ToString(CultureInfo.InvariantCulture) + ")") + extension);
            try { using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write)) { } return candidate; }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
        }
        var fallback = Path.Combine(folder, baseName + "-" + Guid.NewGuid().ToString("N")[..8] + extension);
        using (new FileStream(fallback, FileMode.CreateNew, FileAccess.Write)) { }
        return fallback;
    }

    /// <summary>Default export folder: Documents\Sermon Studio.</summary>
    internal static string DefaultFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sermon Studio");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
