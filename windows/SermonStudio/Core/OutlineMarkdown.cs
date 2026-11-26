using System.Text;
using System.Text.RegularExpressions;

namespace SermonStudio;

internal readonly record struct OutlineRun(string Text, bool Bold, bool Italic);
internal enum OutlineBlock { Paragraph, Heading, Quote, Bullet, Numbered }
internal sealed record OutlineLine(OutlineBlock Block, int Level, string Marker, IReadOnlyList<OutlineRun> Runs)
{
    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}
internal readonly record struct VisualSpan(int Start, int End, bool Bold, bool Italic, float Size);
internal sealed record VisualDocument(string Text, IReadOnlyList<VisualSpan> Spans);

/// <summary>
/// The outline formatting model shared by the visual editor, reading view and document exports.
/// Supported Markdown: "## " heading, "### " subheading, "> " scripture quotation, "- "/"* " bullets,
/// numbered lines, **bold**, *italic* and ***bold italic***. A backslash escapes a literal asterisk.
/// </summary>
internal static partial class OutlineMarkdown
{
    internal const float DefaultBaseSize = 17f;
    internal const string VisualQuote = "❯ ";
    internal const string VisualBullet = "• ";

    internal static float HeadingSize(float baseSize) => baseSize * 23f / 17f;
    internal static float SubheadingSize(float baseSize) => baseSize * 19f / 17f;

    [GeneratedRegex("\r\n|\r|\n")]
    private static partial Regex NewLine();
    [GeneratedRegex(@"^(#{1,6})[ \t]+")]
    private static partial Regex HeadingMarker();
    [GeneratedRegex(@"^>[ \t]?")]
    private static partial Regex QuoteMarker();
    [GeneratedRegex(@"^[-*+][ \t]+")]
    private static partial Regex BulletMarker();
    [GeneratedRegex(@"^\d{1,3}[.)][ \t]+")]
    private static partial Regex NumberMarker();

    /// <summary>Splits on CRLF, CR (WinUI TextBox line breaks) or LF.</summary>
    internal static string[] SplitLines(string? text) => NewLine().Split(text ?? "");

    internal static string NormalizeNewlines(string? text, string newline = "\n") => string.Join(newline, SplitLines(text));

    // ----- Inline formatting -------------------------------------------------------------

    internal static List<OutlineRun> ParseInline(string? text)
    {
        var runs = new List<OutlineRun>();
        if (string.IsNullOrEmpty(text)) return runs;
        var plain = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '*') { plain.Append('*'); i += 2; continue; }
            if (c == '*')
            {
                int n = 0;
                while (i + n < text.Length && text[i + n] == '*') n++;
                if (n <= 3 && i + n < text.Length && !char.IsWhiteSpace(text[i + n]))
                {
                    int close = FindClose(text, i + n, n);
                    if (close > 0)
                    {
                        Flush(runs, plain);
                        runs.Add(new OutlineRun(Unescape(text[(i + n)..close]), n >= 2, n != 2));
                        i = close + n;
                        continue;
                    }
                }
                plain.Append('*', n);
                i += n;
                continue;
            }
            plain.Append(c);
            i++;
        }
        Flush(runs, plain);
        return Merge(runs);
    }

    private static int FindClose(string text, int start, int count)
    {
        for (int j = start; j < text.Length; j++)
        {
            if (text[j] == '\\' && j + 1 < text.Length && text[j + 1] == '*') { j++; continue; }
            if (text[j] != '*') continue;
            int n = 0;
            while (j + n < text.Length && text[j + n] == '*') n++;
            return n == count && j > start && !char.IsWhiteSpace(text[j - 1]) ? j : -1;
        }
        return -1;
    }

    private static string Unescape(string value) => value.Replace("\\*", "*");

    private static void Flush(List<OutlineRun> runs, StringBuilder plain)
    {
        if (plain.Length == 0) return;
        runs.Add(new OutlineRun(plain.ToString(), false, false));
        plain.Clear();
    }

    internal static List<OutlineRun> Merge(IEnumerable<OutlineRun> runs)
    {
        var result = new List<OutlineRun>();
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;
            if (result.Count > 0 && result[^1].Bold == run.Bold && result[^1].Italic == run.Italic)
                result[^1] = result[^1] with { Text = result[^1].Text + run.Text };
            else result.Add(run);
        }
        return result;
    }

    /// <summary>Writes runs as Markdown, escaping literal asterisks only when needed to round-trip.</summary>
    internal static string SerializeInline(IEnumerable<OutlineRun> runs)
    {
        var merged = Merge(runs);
        var simple = Write(merged, escapePlain: false);
        return ParseInline(simple).SequenceEqual(merged) ? simple : Write(merged, escapePlain: true);
    }

    private static string Write(List<OutlineRun> runs, bool escapePlain)
    {
        var output = new StringBuilder();
        foreach (var run in runs)
        {
            if (!run.Bold && !run.Italic) { output.Append(escapePlain ? run.Text.Replace("*", "\\*") : run.Text); continue; }
            var core = run.Text.Trim();
            if (core.Length == 0) { output.Append(run.Text); continue; }
            int lead = run.Text.Length - run.Text.TrimStart().Length;
            var marker = run.Bold && run.Italic ? "***" : run.Bold ? "**" : "*";
            output.Append(run.Text, 0, lead).Append(marker).Append(core.Replace("*", "\\*")).Append(marker).Append(run.Text[(lead + core.Length)..]);
        }
        return output.ToString();
    }

    // ----- Block structure (exports and reading view) -------------------------------------

    internal static OutlineLine ParseLine(string? raw)
    {
        var line = (raw ?? "").TrimEnd();
        var body = line.TrimStart();
        Match m;
        if ((m = HeadingMarker().Match(body)).Success)
            return new OutlineLine(OutlineBlock.Heading, m.Groups[1].Length, m.Value, ParseInline(body[m.Length..]));
        if ((m = QuoteMarker().Match(body)).Success)
            return new OutlineLine(OutlineBlock.Quote, 0, m.Value, ParseInline(body[m.Length..]));
        if ((m = BulletMarker().Match(body)).Success)
            return new OutlineLine(OutlineBlock.Bullet, 0, m.Value, ParseInline(body[m.Length..]));
        if ((m = NumberMarker().Match(body)).Success)
            return new OutlineLine(OutlineBlock.Numbered, 0, m.Value.TrimEnd() + " ", ParseInline(body[m.Length..]));
        return new OutlineLine(OutlineBlock.Paragraph, 0, "", ParseInline(body));
    }

    internal static List<OutlineLine> ParseDocument(string? markdown) => SplitLines(markdown).Select(ParseLine).ToList();

    // ----- Visual (rich-text) editor mapping ----------------------------------------------

    /// <summary>Converts Markdown to the plain text and character formatting shown in the visual editor. Lines are separated by '\r'.</summary>
    internal static VisualDocument ToVisual(string? markdown, float baseSize = DefaultBaseSize)
    {
        var text = new StringBuilder();
        var spans = new List<VisualSpan>();
        var lines = SplitLines(markdown);
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0) text.Append('\r');
            var line = lines[index];
            float size = baseSize;
            bool heading = line.StartsWith("## ", StringComparison.Ordinal), sub = line.StartsWith("### ", StringComparison.Ordinal);
            bool quote = false;
            string prefix = "", body = line;
            if (heading) { size = HeadingSize(baseSize); body = line[3..]; }
            else if (sub) { size = SubheadingSize(baseSize); body = line[4..]; }
            else if (line.StartsWith("> ", StringComparison.Ordinal)) { quote = true; prefix = VisualQuote; body = line[2..]; }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) { prefix = VisualBullet; body = line[2..]; }
            bool strong = heading || sub;
            if (prefix.Length > 0) { spans.Add(new VisualSpan(text.Length, text.Length + prefix.Length, strong, quote, size)); text.Append(prefix); }
            foreach (var run in ParseInline(body))
            {
                spans.Add(new VisualSpan(text.Length, text.Length + run.Text.Length, run.Bold || strong, run.Italic || quote, size));
                text.Append(run.Text);
            }
        }
        return new VisualDocument(text.ToString(), spans);
    }

    /// <summary>
    /// Converts visual-editor content back to Markdown. <paramref name="sizeAt"/> and <paramref name="formatAt"/>
    /// read the character formatting at a document offset.
    /// </summary>
    /// <remarks>RichEditBox.GetText appends a final paragraph mark; callers strip it before calling.</remarks>
    internal static string FromVisual(string plain, float baseSize, Func<int, float> sizeAt, Func<int, (bool Bold, bool Italic)> formatAt)
    {
        var output = new StringBuilder();
        int offset = 0;
        var lines = plain.Split('\r');
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0) output.Append('\n');
            var line = lines[index];
            int start = offset;
            output.Append(LineFromVisual(line, line.Length > 0 ? sizeAt(start) : baseSize, baseSize, i => formatAt(start + i)));
            offset += line.Length + 1;
        }
        return output.ToString();
    }

    internal static string LineFromVisual(string line, float firstSize, float baseSize, Func<int, (bool Bold, bool Italic)> formatAt)
    {
        if (line.Length == 0) return "";
        // Thresholds sit midway between normal/subheading/heading sizes, relative to the editor's base size.
        bool heading = firstSize >= baseSize * 21f / 17f;
        bool sub = !heading && firstSize >= baseSize * 18f / 17f;
        var prefix = heading ? "## " : sub ? "### " : "";
        int i = 0;
        bool quote = false;
        if (!heading && !sub)
        {
            if (line.StartsWith(VisualQuote, StringComparison.Ordinal)) { prefix = "> "; quote = true; i = VisualQuote.Length; }
            else if (line.StartsWith(VisualBullet, StringComparison.Ordinal)) { prefix = "- "; i = VisualBullet.Length; }
        }
        var runs = new List<OutlineRun>();
        while (i < line.Length)
        {
            var f = formatAt(i);
            bool bold = !heading && !sub && f.Bold, italic = !quote && f.Italic;
            int end = i + 1;
            while (end < line.Length)
            {
                var next = formatAt(end);
                if ((!heading && !sub && next.Bold) != bold || (!quote && next.Italic) != italic) break;
                end++;
            }
            runs.Add(new OutlineRun(line[i..end], bold, italic));
            i = end;
        }
        return prefix + SerializeInline(runs);
    }

    /// <summary>Round-trips Markdown through the visual model, exactly as the editor would store it unchanged.</summary>
    internal static string RoundTrip(string? markdown, float baseSize = DefaultBaseSize)
    {
        var visual = ToVisual(markdown, baseSize);
        var sizes = new float[visual.Text.Length];
        var formats = new (bool, bool)[visual.Text.Length];
        Array.Fill(sizes, baseSize);
        foreach (var span in visual.Spans)
            for (int k = span.Start; k < span.End; k++) { sizes[k] = span.Size; formats[k] = (span.Bold, span.Italic); }
        return FromVisual(visual.Text, baseSize, k => sizes[k], k => formats[k]);
    }

    /// <summary>
    /// Combines a visual-editor result with the Markdown it was loaded from. Lines the user did not change
    /// keep their original Markdown spelling ("* " bullets, H1/H4 lines, escapes, spacing), so switching
    /// views never rewrites untouched content. Changed or inserted lines take the editor's Markdown.
    /// </summary>
    internal static string MergeVisualEdit(string? original, string updated, float baseSize = DefaultBaseSize)
    {
        var a = SplitLines(original);
        var b = SplitLines(updated);
        var keyA = a.Select(line => RoundTrip(line, baseSize)).ToArray();
        var keyB = b.Select(line => RoundTrip(line, baseSize)).ToArray();
        var output = new string[b.Length];
        if ((long)a.Length * b.Length > 4_000_000)
        {
            for (int j = 0; j < b.Length; j++) output[j] = j < a.Length && keyA[j] == keyB[j] ? a[j] : b[j];
            return string.Join("\n", output);
        }
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
            for (int j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = keyA[i] == keyB[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        int x = 0, y = 0;
        while (y < b.Length)
        {
            if (x < a.Length && keyA[x] == keyB[y]) { output[y] = a[x]; x++; y++; }
            else if (x < a.Length && lcs[x + 1, y] >= lcs[x, y + 1]) x++;
            else { output[y] = b[y]; y++; }
        }
        return string.Join("\n", output);
    }
}
