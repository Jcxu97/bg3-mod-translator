using System.Text.RegularExpressions;

namespace BG3LocTool.Core;

public sealed record PlaceholderCheck(
    bool IsValid,
    IReadOnlyList<string> MissingInTranslation,
    IReadOnlyList<string> ExtraInTranslation);

public static class PlaceholderValidator
{
    private static readonly Regex LsTagRx = new(@"<LSTag\b[^>]*?/?>", RegexOptions.Compiled);
    private static readonly Regex BracketNumRx = new(@"\[\d+\]", RegexOptions.Compiled);
    private static readonly Regex CurlyTokenRx = new(@"\{[A-Za-z0-9_]+\}", RegexOptions.Compiled);
    private static readonly Regex AttrRx = new(@"([A-Za-z_][A-Za-z0-9_\-:]*)\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.Compiled);
    private static readonly Regex InnerWsRx = new(@"\s+", RegexOptions.Compiled);

    public static PlaceholderCheck Check(string source, string translation)
    {
        var src = ExtractPlaceholders(source);
        var dst = ExtractPlaceholders(translation);
        var missing = MultisetDiff(src, dst);
        var extra = MultisetDiff(dst, src);
        return new PlaceholderCheck(missing.Count == 0 && extra.Count == 0, missing, extra);
    }

    public static bool IsValid(string source, string translation) => Check(source, translation).IsValid;

    public static List<string> ExtractPlaceholders(string s)
    {
        var n = NormalizeEntities(s);
        var found = new List<string>();
        foreach (Match m in LsTagRx.Matches(n)) found.Add(CanonicalizeLsTag(m.Value));
        foreach (Match m in BracketNumRx.Matches(n)) found.Add(m.Value);
        foreach (Match m in CurlyTokenRx.Matches(n)) found.Add(m.Value);
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static string NormalizeEntities(string s) => s
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&quot;", "\"")
        .Replace("&apos;", "'")
        .Replace("&amp;", "&");

    private static string CanonicalizeLsTag(string tag)
    {
        var collapsed = InnerWsRx.Replace(tag, " ").Trim();
        var attrs = AttrRx.Matches(collapsed)
            .Select(m => m.Groups[1].Value + "=\"" + StripQuotes(m.Groups[2].Value) + "\"")
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();
        var selfClose = collapsed.EndsWith("/>", StringComparison.Ordinal);
        return "<LSTag" + (attrs.Count > 0 ? " " + string.Join(' ', attrs) : "") + (selfClose ? "/>" : ">");
    }

    private static string StripQuotes(string q) =>
        q.Length >= 2 && (q[0] == '"' || q[0] == '\'') ? q[1..^1] : q;

    private static List<string> MultisetDiff(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in b) counts[x] = counts.TryGetValue(x, out var c) ? c + 1 : 1;
        var diff = new List<string>();
        foreach (var x in a)
        {
            if (counts.TryGetValue(x, out var c) && c > 0) counts[x] = c - 1;
            else diff.Add(x);
        }
        return diff;
    }
}
