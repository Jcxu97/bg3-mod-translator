using System.Text.Json;
using System.Text.RegularExpressions;

namespace BG3LocTool.Core;

/// <summary>
/// BG3 官方简中字典(从游戏 English.pak / Chinese.pak 提取的 232K 条 EN→ZH)。
/// 本类提供:
///   1) 整句精确反查 (ExactMatch) — 命中即权威,跳过 AI
///   2) 术语级匹配 (FindTerms) — 检测原文里的官方术语,产生约束注入 prompt
///   3) 后置校验 (VerifyTerms) — AI 输出后检查官方术语是否到位
/// </summary>
public sealed class Bg3OfficialGlossary
{
    private static readonly Lazy<Bg3OfficialGlossary?> _default = new(() => TryLoad());
    public static Bg3OfficialGlossary? Default => _default.Value;

    private readonly Dictionary<string, string> _en2zh;          // 整句精确反查
    private readonly Dictionary<string, string> _termDict;       // 术语级 EN→ZH
    private readonly Regex? _termRegex;                          // 单一大正则,长度倒序 alternation

    public int ReverseEntryCount => _en2zh.Count;
    public int TermCount => _termDict.Count;

    private Bg3OfficialGlossary(Dictionary<string, string> en2zh, Dictionary<string, string> termDict)
    {
        _en2zh = en2zh;
        _termDict = termDict;
        if (termDict.Count > 0)
        {
            var sorted = termDict.Keys.OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal);
            var alt = string.Join("|", sorted.Select(Regex.Escape));
            // word boundaries via [^A-Za-z0-9]; allows matches at string start/end
            _termRegex = new Regex(@"(?<![A-Za-z0-9])(" + alt + @")(?![A-Za-z0-9])", RegexOptions.Compiled);
        }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static Bg3OfficialGlossary? TryLoad(string? rootDir = null)
    {
        try
        {
            // Resource path (default): embedded into Core.dll; works in single-file publish.
            // Disk path (rootDir or dev fallback): used by tests / scripts when running outside the bundled exe.
            string ReadFile(string name)
            {
                if (rootDir != null)
                {
                    var p = Path.Combine(rootDir, name);
                    if (File.Exists(p)) return File.ReadAllText(p);
                }
                using var s = typeof(Bg3OfficialGlossary).Assembly.GetManifestResourceStream("bg3glossary." + name);
                if (s != null) { using var r = new StreamReader(s); return r.ReadToEnd(); }
                if (rootDir == null)
                {
                    // last-ditch: probe disk relative to base dir
                    var fallback = ResolveDefaultRoot();
                    if (fallback != null && File.Exists(Path.Combine(fallback, name))) return File.ReadAllText(Path.Combine(fallback, name));
                }
                return "";
            }

            var en2zhJson = ReadFile("en2zh.json");
            if (string.IsNullOrEmpty(en2zhJson)) return null;
            var en2zh = JsonSerializer.Deserialize<Dictionary<string, string>>(en2zhJson, _jsonOpts) ?? new();

            // Build term dict in priority order: core (high-freq) + lstag (D&D mechanics) always included;
            // short terms fill remaining budget. Cap keeps regex compile time reasonable.
            // 30k was tried but the alternation regex blew the stack at runtime (STATUS_STACK_OVERFLOW
            // 0xC00000FD on first match). 8k is the empirically-stable cap with RegexOptions.Compiled +
            // lookbehind/lookahead. Coverage prioritizes core + lstag + longest short terms.
            const int minLen = 2, maxLen = 30, maxTerms = 8000;
            var termDict = new Dictionary<string, string>(StringComparer.Ordinal);

            var coreJson = ReadFile("terms_core.json");
            if (!string.IsNullOrEmpty(coreJson))
            {
                var core = JsonSerializer.Deserialize<List<CoreTerm>>(coreJson, _jsonOpts);
                if (core != null)
                    foreach (var t in core)
                        if (!string.IsNullOrEmpty(t.En) && !string.IsNullOrEmpty(t.Zh) && t.En.Length >= minLen && t.En.Length <= maxLen)
                            termDict.TryAdd(t.En, t.Zh);
            }

            var lstagJson = ReadFile("lstag_terms.json");
            if (!string.IsNullOrEmpty(lstagJson))
            {
                var ls = JsonSerializer.Deserialize<Dictionary<string, LsTagTerm>>(lstagJson, _jsonOpts);
                if (ls != null)
                    foreach (var v in ls.Values)
                        if (!string.IsNullOrEmpty(v.En) && !string.IsNullOrEmpty(v.Zh) && v.En.Length >= minLen && v.En.Length <= maxLen)
                            termDict.TryAdd(v.En, v.Zh);
            }

            var shortJson = ReadFile("terms_short.json");
            if (!string.IsNullOrEmpty(shortJson))
            {
                var sh = JsonSerializer.Deserialize<Dictionary<string, string>>(shortJson, _jsonOpts);
                if (sh != null)
                {
                    int budget = maxTerms - termDict.Count;
                    foreach (var kv in sh.OrderByDescending(kv => kv.Key.Length))
                    {
                        if (budget <= 0) break;
                        if (kv.Key.Length < minLen || kv.Key.Length > maxLen || string.IsNullOrEmpty(kv.Value)) continue;
                        if (termDict.TryAdd(kv.Key, kv.Value)) budget--;
                    }
                }
            }

            return new Bg3OfficialGlossary(en2zh, termDict);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>整句精确反查。命中即权威,跳过 AI 调用。</summary>
    public bool TryExactMatch(string en, out string zh)
    {
        if (!string.IsNullOrEmpty(en) && _en2zh.TryGetValue(en.Trim(), out var v))
        {
            zh = v;
            return true;
        }
        zh = "";
        return false;
    }

    /// <summary>识别原文里所有官方术语(最长前缀匹配)。</summary>
    public List<TermHit> FindTerms(string en)
    {
        var hits = new List<TermHit>();
        if (_termRegex is null || string.IsNullOrEmpty(en)) return hits;
        foreach (Match m in _termRegex.Matches(en))
        {
            var key = m.Groups[1].Value;
            if (_termDict.TryGetValue(key, out var zh))
                hits.Add(new TermHit(key, zh, m.Index, m.Length));
        }
        return hits;
    }

    /// <summary>生成可注入 LLM system prompt 的术语约束块。空文本表示无约束。</summary>
    public string BuildConstraintBlock(string en)
    {
        var hits = FindTerms(en);
        if (hits.Count == 0) return "";
        // 同一术语只列一次,长的先列
        var unique = hits.GroupBy(h => h.En).Select(g => g.First())
            .OrderByDescending(h => h.En.Length);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BG3 OFFICIAL TERMINOLOGY (these EXACT English terms appear in the source — you MUST use the official Chinese listed; no synonyms):");
        foreach (var h in unique) sb.AppendLine($"  · {h.En}  →  {h.Zh}");
        return sb.ToString();
    }

    /// <summary>校验 AI 输出是否使用了官方术语。返回未达标的术语列表。</summary>
    public List<TermHit> VerifyTerms(string en, string zh)
    {
        var missing = new List<TermHit>();
        foreach (var h in FindTerms(en))
            if (!zh.Contains(h.Zh, StringComparison.Ordinal))
                missing.Add(h);
        return missing;
    }

    private static string? ResolveDefaultRoot()
    {
        // try several candidate locations relative to assembly / exe
        var asmDir = AppContext.BaseDirectory;
        foreach (var rel in new[] { "bg3-glossary", Path.Combine("..", "..", "..", "..", "..", "lib", "bg3-glossary"), Path.Combine("..", "..", "..", "..", "..", "..", "lib", "bg3-glossary") })
        {
            var p = Path.GetFullPath(Path.Combine(asmDir, rel));
            if (File.Exists(Path.Combine(p, "en2zh.json"))) return p;
        }
        return null;
    }

    private sealed class CoreTerm { public string En { get; set; } = ""; public string Zh { get; set; } = ""; public int Freq { get; set; } }
    private sealed class LsTagTerm { public string En { get; set; } = ""; public string Zh { get; set; } = ""; public int Freq { get; set; } public string Tooltip { get; set; } = ""; }
}

public sealed record TermHit(string En, string Zh, int Position, int Length);
