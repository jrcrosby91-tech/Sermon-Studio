using System.Globalization;
using System.Text.RegularExpressions;

namespace SermonStudio;

/// <summary>
/// Reads and writes the small preference files stored beside the sermon library.
/// Existing file names are kept so preferences survive updates from older builds.
/// Invalid or unreadable values fall back to defaults; writes are atomic.
/// </summary>
internal sealed partial class PreferenceStore
{
    internal const string OfflineModel = "offline-model";
    internal const string DefaultGenerator = "default-generator";
    internal const string Appearance = "appearance";
    internal const string EditorSize = "editor-size";
    internal const string AutoUpdateCheck = "auto-update-check";
    internal const string Workspace = "workspace";
    internal const string OllamaModel = "ollama-model";
    internal const string WhisperPath = "whisper-path";
    internal const string FfmpegPath = "ffmpeg-path";
    internal const string CustomModelPath = "custom-model-path";

    internal static readonly string[] OfflineModels = { "tiny.en", "base.en", "small.en", "medium.en", "large-v3-turbo" };
    internal static readonly int[] EditorSizes = { 14, 17, 20, 23, 26 };
    internal const string DefaultOllamaModel = "llama3.2:3b";

    private readonly Action<string>? _log;
    internal string Folder { get; }

    internal PreferenceStore(string folder, Action<string>? log = null)
    {
        Folder = folder;
        _log = log;
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_.-]+(/[a-zA-Z0-9_.-]+)?(:[a-zA-Z0-9_.-]+)?$")]
    private static partial Regex OllamaNamePattern();

    internal string PathFor(string name)
    {
        if (!NamePattern().IsMatch(name)) throw new ArgumentException("Invalid preference name.", nameof(name));
        return System.IO.Path.Combine(Folder, name + ".txt");
    }

    /// <summary>Returns the trimmed stored value, or null when missing or unreadable.</summary>
    internal string? Get(string name)
    {
        var path = PathFor(name);
        try { return File.Exists(path) ? File.ReadAllText(path).Trim().TrimStart('\uFEFF') : null; }
        catch (Exception ex) { _log?.Invoke("Read preference " + name + ": " + ex.GetType().Name); return null; }
    }

    internal bool TrySet(string name, string value, out string? error)
    {
        try { AtomicFile.WriteAllText(PathFor(name), value); error = null; return true; }
        catch (Exception ex) { error = ex.Message; _log?.Invoke("Write preference " + name + ": " + ex.GetType().Name); return false; }
    }

    internal bool TryClear(string name, out string? error)
    {
        try { var path = PathFor(name); if (File.Exists(path)) File.Delete(path); error = null; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    internal string GetChoice(string name, IReadOnlyCollection<string> allowed, string fallback)
    {
        var value = Get(name);
        return value != null && allowed.Contains(value, StringComparer.Ordinal) ? value : fallback;
    }

    internal int GetInt(string name, Func<int, bool> valid, int fallback)
    {
        var value = Get(name);
        return value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && valid(parsed) ? parsed : fallback;
    }

    internal bool GetBool(string name, bool fallback) => Get(name) switch
    {
        "1" or "true" or "True" => true,
        "0" or "false" or "False" => false,
        _ => fallback,
    };

    internal string GetOfflineModel() => GetChoice(OfflineModel, OfflineModels, "base.en");

    /// <summary>0 = built-in offline generator, 1 = local Ollama.</summary>
    internal int GetDefaultGenerator() => Get(DefaultGenerator) == "ollama" ? 1 : 0;
    internal bool TrySetDefaultGenerator(int index, out string? error) => TrySet(DefaultGenerator, index == 1 ? "ollama" : "original", out error);

    internal string GetOllamaModel()
    {
        var value = Get(OllamaModel);
        return value != null && IsValidOllamaModel(value) ? value : DefaultOllamaModel;
    }

    /// <summary>Only local model tags are allowed; cloud-routed models are rejected to keep sermons on this computer.</summary>
    internal static bool IsValidOllamaModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && model.Length <= 120 && OllamaNamePattern().IsMatch(model)
        && !model.Contains("cloud", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns a saved executable path only if it still exists; otherwise null so the bundled engine is used.</summary>
    internal string? GetExistingFile(string name)
    {
        var value = Get(name);
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return System.IO.Path.IsPathFullyQualified(value) && File.Exists(value) ? value : null; }
        catch { return null; }
    }

    internal static (double Split, string Page) ParseWorkspace(string? text, string fallbackPage)
    {
        double split = .5;
        string page = fallbackPage;
        if (string.IsNullOrEmpty(text)) return (split, page);
        var lines = text.Replace("\r", "").Split('\n');
        if (double.TryParse(lines[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) && double.IsFinite(ratio))
            split = Math.Clamp(ratio, .2, .8);
        if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])) page = lines[1].Trim();
        return (split, page);
    }

    internal static string FormatWorkspace(double split, string page) =>
        Math.Clamp(double.IsFinite(split) ? split : .5, .2, .8).ToString(CultureInfo.InvariantCulture) + "\n" + page;
}
