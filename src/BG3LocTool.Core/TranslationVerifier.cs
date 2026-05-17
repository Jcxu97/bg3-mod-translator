namespace BG3LocTool.Core;

public sealed record VerifyResult(
    int Total,
    int Translated,        // chinese != english AND not empty
    int IdenticalToSource, // chinese == english (silent failure: tool wrote English-as-Chinese)
    int Empty,             // chinese is null/empty/whitespace
    int Missing,           // english entry has no chinese counterpart
    string Verdict)        // human summary
{
    public bool IsHollowShell => Total > 0 && Translated == 0;
    public bool IsLowQuality => Total > 0 && Translated * 2 < Total;
    public double TranslationRate => Total == 0 ? 0 : (double)Translated / Total;
}

public static class TranslationVerifier
{
    /// <summary>Verifies a packaged .pak by re-extracting it and comparing Chinese vs English entries.</summary>
    public static VerifyResult VerifyPak(string pakPath, string langFolder)
    {
        var temp = Path.Combine(Path.GetTempPath(), "BG3LocTool_verify_" + Guid.NewGuid().ToString("N"));
        try
        {
            PakHandler.Extract(pakPath, temp);
            var modDirs = Directory.GetDirectories(Path.Combine(temp, "Mods"));
            var modFolder = modDirs.FirstOrDefault();
            if (modFolder == null) return new(0, 0, 0, 0, 0, "no Mods/<X>/ folder found");
            return VerifyStaged(temp, modFolder, langFolder);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    /// <summary>Verifies a stage directory directly. <paramref name="englishSourceDir"/> overrides the
    /// English source — needed because translation paks (correctly) don't ship English; we must compare
    /// against the original mod's English instead.</summary>
    public static VerifyResult VerifyStaged(string contentDir, string modFolder, string langFolder, string? englishSourceDir = null)
    {
        var enDir = englishSourceDir ?? TranslationPipeline.FindLocaDir(contentDir, modFolder, "English");
        var zhDir = TranslationPipeline.FindLocaDir(contentDir, modFolder, langFolder);
        if (enDir == null) return new(0, 0, 0, 0, 0, "no English/ loca dir(请传 englishSourceDir 或在 staged 里有 English/)");
        if (zhDir == null) return new(0, 0, 0, 0, 0, $"no {langFolder}/ loca dir — pak has zero translation files");

        int total = 0, translated = 0, identical = 0, empty = 0, missing = 0;
        // Match by handle globally (filenames in Chinese/English may differ — e.g. "english.xml" vs "englishss.xml")
        var zhByHandle = new Dictionary<string, string>();
        foreach (var f in TranslationPipeline.EnumerateLocaFiles(zhDir))
            foreach (var e in LocaIO.Load(f))
                zhByHandle[e.Handle] = e.Text ?? "";

        foreach (var f in TranslationPipeline.EnumerateLocaFiles(enDir))
            foreach (var e in LocaIO.Load(f))
            {
                if (string.IsNullOrEmpty(e.Text)) continue;
                total++;
                if (!zhByHandle.TryGetValue(e.Handle, out var zh)) { missing++; continue; }
                if (string.IsNullOrWhiteSpace(zh)) empty++;
                else if (zh == e.Text) identical++;
                else translated++;
            }

        string verdict;
        if (total == 0) verdict = "no entries to verify";
        else if (translated == 0) verdict = $"🚨 空壳:{total} 条全部未翻译({identical} 条原样英文 / {empty} 条空 / {missing} 条缺失)";
        else if (translated * 2 < total) verdict = $"⚠️ 翻译率低:{translated}/{total} ({100 * translated / total}%) 实际翻译,其余原样/空/缺失";
        else verdict = $"✓ 翻译率 {translated}/{total} ({100 * translated / total}%);原样 {identical},空 {empty},缺失 {missing}";

        return new(total, translated, identical, empty, missing, verdict);
    }
}
