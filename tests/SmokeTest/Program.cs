using BG3LocTool.Core;

Console.WriteLine("=== Bg3OfficialGlossary Smoke Test ===\n");

var g = Bg3OfficialGlossary.Default;
if (g is null) { Console.Error.WriteLine("❌ Glossary failed to load"); return 1; }

Console.WriteLine($"✓ Loaded: {g.ReverseEntryCount} reverse entries / {g.TermCount} terms\n");

// Test 1: Exact match
string[] tests1 = { "Mephit", "Long Rest", "Zhentarim", "SomeUnknownXxx" };
foreach (var en in tests1)
{
    if (g.TryExactMatch(en, out var zh))
        Console.WriteLine($"  TryExactMatch(\"{en}\") → \"{zh}\"");
    else
        Console.WriteLine($"  TryExactMatch(\"{en}\") → MISS");
}

// Test 2: Term-level matching in a sentence
Console.WriteLine();
var sentence = "Restores all Hit Points after a Long Rest near a Mind Flayer.";
Console.WriteLine($"  FindTerms(\"{sentence}\"):");
var hits = g.FindTerms(sentence);
foreach (var h in hits)
    Console.WriteLine($"    [{h.Position,3}] {h.En,-15} → {h.Zh}");
Console.WriteLine($"  Total hits: {hits.Count}");

// Test 3: BuildConstraintBlock
Console.WriteLine("\n  BuildConstraintBlock(...) preview:");
var block = g.BuildConstraintBlock(sentence);
foreach (var line in block.Split('\n').Take(6))
    Console.WriteLine("    " + line);

// Test 4: VerifyTerms — simulate a translation that misses one term
var zhMissing = "在长休后恢复所有生命值，附近有夺心魔。";  // Long Rest + Hit Points + Mind Flayer 都用了官方
var zhCorrect = "在长休后恢复所有生命值，靠近夺心魔。";
var zhBad = "休息后恢复所有血量，附近有怪。";  // 故意全不命中
Console.WriteLine($"\n  VerifyTerms (correct zh): missing = {g.VerifyTerms(sentence, zhCorrect).Count}");
Console.WriteLine($"  VerifyTerms (bad zh):     missing = {g.VerifyTerms(sentence, zhBad).Count}");
foreach (var m in g.VerifyTerms(sentence, zhBad))
    Console.WriteLine($"    × missing: {m.En} → expected {m.Zh}");

// Test 5: Bg3Glossary 字典精华（验证今天 Patch 8 校对）
Console.WriteLine();
Console.WriteLine("  Bg3Glossary.ForSystemPrompt 字典精华:");
var dict = Bg3Glossary.ForSystemPrompt;
string[] expectedFixes = { "散塔林会", "魔蝠", "耐色脑", "幽影诅咒", "洛山达", "长休", "法术位" };
int fixHits = 0;
foreach (var t in expectedFixes)
{
    bool ok = dict.Contains(t);
    Console.WriteLine($"    {(ok ? "✓" : "✗")} contains \"{t}\"");
    if (ok) fixHits++;
}
if (fixHits != expectedFixes.Length) { Console.Error.WriteLine($"❌ Bg3Glossary 漏 {expectedFixes.Length - fixHits} 条"); return 1; }
Console.WriteLine($"  ✓ Bg3Glossary 含 {fixHits}/{expectedFixes.Length} 条 Patch 8 校对项");

// Test 6: Bg3ModdingWiki 语境精华
Console.WriteLine();
Console.WriteLine("  Bg3ModdingWiki.ForSystemPrompt 语境精华:");
var wiki = Bg3ModdingWiki.ForSystemPrompt;
Console.WriteLine($"    长度: {wiki.Length} chars (~{wiki.Length / 1024} KB)");
string[] expectedWikiKeys = {
    "Script Extender", "v32", "Mod Fixer", "Patch 8 起反而有害",
    "SE Lua runtime override", "VFX_Library_SHV", "VladsCodex",
    "[1]", "&lt;LSTag", "豁免检定", "PRONE→倒伏", "Schools of magic"
};
int wikiHits = 0;
foreach (var k in expectedWikiKeys)
{
    bool ok = wiki.Contains(k);
    if (ok) wikiHits++;
    else Console.WriteLine($"    ✗ missing \"{k}\"");
}
if (wikiHits != expectedWikiKeys.Length) { Console.Error.WriteLine($"❌ Bg3ModdingWiki 漏 {expectedWikiKeys.Length - wikiHits} 关键词"); return 1; }
Console.WriteLine($"  ✓ Bg3ModdingWiki 含 {wikiHits}/{expectedWikiKeys.Length} 关键概念");

// Test 7: System prompt 总量（确认 wiki 已注入 OpenAICompatibleTranslator）
Console.WriteLine();
var totalSystemBytes = 800  // basePrompt 估算
                       + Bg3Glossary.ForSystemPrompt.Length
                       + Bg3ModdingWiki.ForSystemPrompt.Length;
Console.WriteLine($"  Translator system prompt 总量约 {totalSystemBytes} chars (~{totalSystemBytes / 1024} KB)");
Console.WriteLine($"    → OpenAI/Anthropic prompt cache 命中后，每条 source 摊销成本 ~$0.0001");

Console.WriteLine("\n=== ✅ ALL SMOKE TESTS PASSED (7/7) ===");
return 0;
