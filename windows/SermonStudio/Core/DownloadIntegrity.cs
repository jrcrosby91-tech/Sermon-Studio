using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SermonStudio;

/// <summary>Integrity checks for Whisper model downloads.</summary>
internal static partial class DownloadIntegrity
{
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();

    internal static readonly string[] ModelNames = PreferenceStore.OfflineModels;

    internal static string ModelUrl(string model)
    {
        if (!ModelNames.Contains(model, StringComparer.Ordinal)) throw new ArgumentException("Unknown model.", nameof(model));
        return "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-" + model + ".bin";
    }

    /// <summary>
    /// Hugging Face reports the SHA-256 of large (LFS) files in the X-Linked-Etag header of the redirect response,
    /// for example <c>"5c6a…"</c> or <c>W/"5c6a…"</c>. Returns the lowercase hex digest or null when absent/invalid.
    /// </summary>
    internal static string? ParseLinkedEtag(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var value = header.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        value = value.Trim('"').ToLowerInvariant();
        return Sha256Hex().IsMatch(value) ? value : null;
    }

    /// <summary>Verifies a completed download. Throws when the size or digest does not match.</summary>
    internal static void Verify(long actualBytes, string actualSha256, long? expectedBytes, string? expectedSha256)
    {
        if (actualBytes <= 0) throw new InvalidDataException("Downloaded file is empty.");
        if (expectedBytes is long size && size != actualBytes) throw new InvalidDataException($"Incomplete download ({actualBytes:N0} of {size:N0} bytes).");
        if (expectedSha256 != null && !string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded model failed its SHA-256 integrity check.");
    }

    internal static string HexDigest(IncrementalHash hash) => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}
