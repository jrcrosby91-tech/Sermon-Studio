using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SermonStudio;

/// <summary>Parses and writes SRT/WebVTT subtitle files produced by whisper.cpp.</summary>
internal static partial class Subtitles
{
    [GeneratedRegex(@"^\s*(?<a>(?:\d+:)?\d{1,2}:\d{1,2}[,.]\d{1,3})\s*-->\s*(?<b>(?:\d+:)?\d{1,2}:\d{1,2}[,.]\d{1,3})")]
    private static partial Regex CueTiming();

    /// <summary>Parses SRT or WebVTT text. Malformed cues are skipped rather than throwing.</summary>
    internal static List<SermonSegment> Parse(string? text)
    {
        var result = new List<SermonSegment>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var lines = Regex.Split(text.TrimStart('\uFEFF'), "\r\n|\r|\n");
        for (int i = 0; i < lines.Length; i++)
        {
            var timing = CueTiming().Match(lines[i]);
            if (!timing.Success || !TryParseTime(timing.Groups["a"].Value, out var start) || !TryParseTime(timing.Groups["b"].Value, out var end)) continue;
            var body = new List<string>();
            int j = i + 1;
            for (; j < lines.Length && lines[j].Trim().Length > 0; j++)
            {
                if (CueTiming().IsMatch(lines[j])) { j--; break; }
                body.Add(lines[j].Trim());
            }
            i = j;
            result.Add(new SermonSegment { Start = start, End = Math.Max(start, end), Text = string.Join("\n", body) });
        }
        return result;
    }

    internal static bool TryParseTime(string value, out double seconds)
    {
        seconds = 0;
        var parts = value.Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3) return false;
        int hours = 0;
        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours)) return false;
        if (!int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes > 59) return false;
        if (!double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var secs) || secs >= 60) return false;
        seconds = hours * 3600 + minutes * 60 + secs;
        return true;
    }

    internal static string FormatTime(double seconds, bool vtt)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        long ms = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        long h = ms / 3_600_000, m = ms / 60_000 % 60, s = ms / 1000 % 60, f = ms % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{h:00}:{m:00}:{s:00}{(vtt ? '.' : ',')}{f:000}");
    }

    internal static string Make(IEnumerable<SermonSegment> segments, bool vtt)
    {
        var builder = new StringBuilder();
        if (vtt) builder.Append("WEBVTT\n\n");
        int index = 0;
        foreach (var s in segments)
        {
            // A blank line inside cue text would terminate the cue, so collapse internal blank lines.
            var text = string.Join("\n", Regex.Split(s.Text ?? "", "\r\n|\r|\n").Select(x => x.Trim()).Where(x => x.Length > 0));
            if (index > 0) builder.Append('\n');
            index++;
            if (!vtt) builder.Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append(FormatTime(s.Start, vtt)).Append(" --> ").Append(FormatTime(Math.Max(s.Start, s.End), vtt)).Append('\n').Append(text).Append('\n');
        }
        return builder.ToString();
    }
}
