namespace BG3LocTool.Core;

public sealed record PipelineConfig(
    string InputModPath,
    string? ReferenceChsPath,
    string OutputDir,
    string Author,
    Bg3Version ChsVersion,
    string TargetLangFolder,        // e.g. "Chinese"
    string TargetLangCode,          // e.g. "zh-CN"
    ITranslator Translator,
    bool EmitBg3mmZip = true,
    bool KeepStageDir = false,      // true → don't pack/cleanup; caller will edit then call PackAsync
    string? OutputBaseName = null); // override output filename (default: sanitized meta.Name + "_CHS")

public sealed record PipelineProgress(string Stage, int Done, int Total, string? Message = null);

public sealed record PipelineResult(
    string? PakPath, string? Bg3mmZipPath,
    int FromCache, int FromReference, int Translated, int Failed,
    bool UpgradeMode, string ChsUuid,
    string StageDir,
    string ModFolder,
    MetaModuleInfo Meta,
    IReadOnlyList<string> FailedHandles,
    VerifyResult? Verification,
    int FromOfficial);

public enum RowSource { Reference, Cached, New, Failed, Empty, Official }

public static class TranslationPipeline
{
    public static async Task<PipelineResult> RunAsync(PipelineConfig cfg, IProgress<PipelineProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new("opening", 0, 1, "opening input mod"));
        using var input = ModSource.Open(cfg.InputModPath);
        var origModFolder = input.FindModFolder();
        var origMeta = MetaEditor.ReadModuleInfo(Path.Combine(origModFolder, "meta.lsx"));

        // Reference (existing CHS): build (handle → oldEnText, oldChsText, oldChsVersion) index, plus pull old CHS meta
        var refIndex = new Dictionary<string, RefEntry>();
        MetaModuleInfo? refMeta = null;
        if (!string.IsNullOrWhiteSpace(cfg.ReferenceChsPath))
        {
            progress?.Report(new("opening", 0, 1, "opening reference CHS"));
            using var refSrc = ModSource.Open(cfg.ReferenceChsPath!);
            var refMod = refSrc.FindModFolder();
            refMeta = MetaEditor.ReadModuleInfo(Path.Combine(refMod, "meta.lsx"));

            var oldEn = new Dictionary<string, string>();
            var refEnDir = FindLocaDir(refSrc.ContentDir, refMod, "English");
            if (refEnDir != null)
                foreach (var f in EnumerateLocaFiles(refEnDir))
                    foreach (var e in LocaIO.Load(f)) oldEn[e.Handle] = e.Text;

            var refChsDir = FindLocaDir(refSrc.ContentDir, refMod, cfg.TargetLangFolder);
            if (refChsDir != null)
                foreach (var f in EnumerateLocaFiles(refChsDir))
                    foreach (var e in LocaIO.Load(f))
                    {
                        oldEn.TryGetValue(e.Handle, out var oldEnText);
                        refIndex[e.Handle] = new RefEntry(oldEnText ?? "", e.Text, e.Version);
                    }
        }

        bool upgrade = refMeta != null;
        var newName = upgrade ? refMeta!.Name : origMeta.Name + "_CHS";
        var newFolder = upgrade ? refMeta!.Folder : origMeta.Folder + "_CHS";
        var newUuid = upgrade ? refMeta!.Uuid : Guid.NewGuid().ToString();
        var preservedGroupUuid = upgrade ? Bg3mmInfo.TryReadGroupUuid(cfg.ReferenceChsPath) : null;

        var stageDir = Path.Combine(Path.GetTempPath(), "BG3LocTool_stage_" + Guid.NewGuid().ToString("N"));
        var stagedMod = Path.Combine(stageDir, "Mods", newFolder);
        Directory.CreateDirectory(stagedMod);

        var newLocaDir = Path.Combine(stagedMod, "Localization");
        var origEnDir = FindLocaDir(input.ContentDir, origModFolder, "English");
        if (origEnDir == null)
            throw new InvalidDataException($"input mod has no Localization/English/ folder (looked under {origModFolder} and {input.ContentDir})");
        // ⚠️ HARD RULE: 翻译包不能包含原文(English)。原因:汉化包独立于原 mod 加载,
        //   如果重复 ship English,版本冲突时可能用旧英文 override 上游 mod 的新英文,
        //   导致原 mod 升级后部分英文文本"被卡住"在翻译包打包时的旧版。
        //   解决方案:只读取原 mod 的 English 用于翻译,不写入输出 stagedMod。
        var logoSrc = Path.Combine(origModFolder, "mod_publish_logo.png");
        if (File.Exists(logoSrc)) File.Copy(logoSrc, Path.Combine(stagedMod, "mod_publish_logo.png"));

        MetaEditor.RewriteForChs(
            Path.Combine(origModFolder, "meta.lsx"),
            Path.Combine(stagedMod, "meta.lsx"),
            origMeta with { Author = cfg.Author, Name = newName, Folder = newFolder, Uuid = newUuid, Version64 = cfg.ChsVersion.Encode() });

        using var cache = new TranslationCache();
        var glossary = Bg3OfficialGlossary.Default;
        int fromCache = 0, fromRef = 0, fromOfficial = 0, translated = 0, failed = 0;
        var failedHandles = new List<string>();
        // (filename, handle) → source, written as sidecar at end of pipeline so editor can show per-row provenance
        var rowStatus = new Dictionary<string, RowSource>();

        // 读源:从原 mod 的 English 目录读(不再依赖 staged 副本,因为我们不再 copy English)
        var chineseDir = Path.Combine(stagedMod, "Localization", cfg.TargetLangFolder);
        Directory.CreateDirectory(chineseDir);

        foreach (var xml in EnumerateLocaFiles(origEnDir))
        {
            var fileKey = Path.GetFileNameWithoutExtension(xml) + ".xml";
            var entries = LocaIO.Load(xml);
            var outEntries = new List<LocaEntry>(entries.Count);
            var pending = new List<(int idx, string source)>();

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (string.IsNullOrEmpty(e.Text)) { outEntries.Add(e); rowStatus[$"{fileKey}|{e.Handle}"] = RowSource.Empty; continue; }

                ushort newVersion = e.Version;
                if (refIndex.TryGetValue(e.Handle, out var r))
                {
                    if (r.OldChsVersion > newVersion) newVersion = r.OldChsVersion;
                    if (r.OldEnText == e.Text)
                    {
                        outEntries.Add(e with { Text = r.OldChsText, Version = newVersion });
                        fromRef++;
                        rowStatus[$"{fileKey}|{e.Handle}"] = RowSource.Reference;
                        continue;
                    }
                }

                // Layer 2: vanilla BG3 official glossary exact-match (skip AI entirely)
                if (glossary != null && glossary.TryExactMatch(e.Text, out var officialZh))
                {
                    outEntries.Add(e with { Text = officialZh, Version = newVersion });
                    fromOfficial++;
                    rowStatus[$"{fileKey}|{e.Handle}"] = RowSource.Official;
                    continue;
                }

                var cached = cache.Get(e.Text, cfg.TargetLangCode, origMeta.Uuid);
                if (cached != null)
                {
                    outEntries.Add(e with { Text = cached, Version = newVersion });
                    fromCache++;
                    rowStatus[$"{fileKey}|{e.Handle}"] = RowSource.Cached;
                    continue;
                }

                outEntries.Add(e with { Version = newVersion });
                pending.Add((i, e.Text));
            }

            if (pending.Count > 0)
            {
                progress?.Report(new("translating", 0, pending.Count, Path.GetFileName(xml)));
                var sources = pending.Select(p => p.source).ToList();
                var contextSamples = refIndex.Values
                    .Where(r => !string.IsNullOrEmpty(r.OldEnText) && !string.IsNullOrEmpty(r.OldChsText))
                    .Take(5)
                    .Select(r => $"\"{r.OldEnText}\" → \"{r.OldChsText}\"")
                    .ToList();
                var results = await cfg.Translator.TranslateBatchAsync(sources, cfg.TargetLangCode, contextSamples, ct);
                for (int j = 0; j < pending.Count; j++)
                {
                    var (idx, src) = pending[j];
                    var r = results[j];
                    var handle = entries[idx].Handle;
                    if (r.Translation != null && PlaceholderValidator.IsValid(src, r.Translation))
                    {
                        outEntries[idx] = outEntries[idx] with { Text = r.Translation };
                        cache.Put(src, r.Translation, cfg.TargetLangCode, cfg.Translator.Name, origMeta.Uuid);
                        translated++;
                        rowStatus[$"{fileKey}|{handle}"] = RowSource.New;
                    }
                    else
                    {
                        outEntries[idx] = outEntries[idx] with { Text = src };
                        failed++;
                        failedHandles.Add(handle);
                        rowStatus[$"{fileKey}|{handle}"] = RowSource.Failed;
                    }
                    progress?.Report(new("translating", j + 1, pending.Count, Path.GetFileName(xml)));
                }
            }

            LocaIO.SaveXml(outEntries, Path.Combine(chineseDir, fileKey));
        }

        // Sidecar metadata for editor consumption
        var sidecarPath = Path.Combine(stagedMod, "_translation_status.json");
        File.WriteAllText(sidecarPath, System.Text.Json.JsonSerializer.Serialize(
            rowStatus.ToDictionary(kv => kv.Key, kv => kv.Value.ToString())));
        if (preservedGroupUuid != null)
            File.WriteAllText(Path.Combine(stagedMod, "_bg3mm_group.txt"), preservedGroupUuid);

        var finalMeta = origMeta with { Author = cfg.Author, Name = newName, Folder = newFolder, Uuid = newUuid, Version64 = cfg.ChsVersion.Encode() };

        // 注意:staged 已不再含 English(我们故意不 ship),所以用原 mod 的 English 当对比源
        var verification = TranslationVerifier.VerifyStaged(stageDir, stagedMod, cfg.TargetLangFolder, englishSourceDir: origEnDir);

        // Always pack. Output filename: caller-supplied OR sanitized "<original mod display name>_CHS".
        var baseName = !string.IsNullOrWhiteSpace(cfg.OutputBaseName)
            ? SanitizeFileName(cfg.OutputBaseName!)
            : DefaultOutputBaseName(origMeta);
        var (pakOut, zipOut) = await PackAsync(stageDir, cfg.OutputDir, finalMeta, cfg.EmitBg3mmZip, progress, preservedGroupUuid, baseName);
        if (!cfg.KeepStageDir) try { Directory.Delete(stageDir, true); } catch { }

        progress?.Report(new("done", 1, 1));
        return new(pakOut, zipOut, fromCache, fromRef, translated, failed, upgrade, newUuid, stageDir, stagedMod, finalMeta, failedHandles, verification, fromOfficial);
    }

    public static async Task<(string PakPath, string? ZipPath)> PackAsync(
        string stageDir, string outputDir, MetaModuleInfo meta, bool emitZip, IProgress<PipelineProgress>? progress = null, string? groupUuid = null, string? outputBaseName = null)
    {
        Directory.CreateDirectory(outputDir);
        // outputBaseName overrides; default to internal Folder for back-compat callers (Editor's export)
        var baseName = !string.IsNullOrWhiteSpace(outputBaseName) ? SanitizeFileName(outputBaseName!) : meta.Folder;
        var pakOut = Path.Combine(outputDir, baseName + ".pak");
        progress?.Report(new("packing", 0, 1, "building pak"));
        await PakHandler.BuildAsync(stageDir, pakOut);

        string? zipOut = null;
        if (emitZip)
        {
            if (groupUuid is null)
            {
                var modDir = Directory.GetDirectories(Path.Combine(stageDir, "Mods")).FirstOrDefault();
                if (modDir != null)
                {
                    var sidecar = Path.Combine(modDir, "_bg3mm_group.txt");
                    if (File.Exists(sidecar)) groupUuid = File.ReadAllText(sidecar).Trim();
                }
            }
            zipOut = Path.Combine(outputDir, baseName + ".zip");
            Bg3mmInfo.WriteZip(zipOut, pakOut, meta, groupUuid);
        }
        return (pakOut, zipOut);
    }

    public static string DefaultOutputBaseName(MetaModuleInfo origMeta)
    {
        // Prefer the human-readable Name; strip parenthesized aliases like "Awakened Blood (ABoL)" → "Awakened Blood"
        var raw = origMeta.Name;
        if (string.IsNullOrWhiteSpace(raw)) raw = origMeta.Folder;
        var paren = raw.IndexOf('(');
        if (paren > 0) raw = raw.Substring(0, paren).TrimEnd();
        return SanitizeFileName(raw + "_CHS");
    }

    private static readonly char[] _illegalFileChars = Path.GetInvalidFileNameChars();

    public static string SanitizeFileName(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) sb.Append(Array.IndexOf(_illegalFileChars, c) >= 0 ? '_' : c);
        return sb.ToString().Trim();
    }

    private sealed record RefEntry(string OldEnText, string OldChsText, ushort OldChsVersion);

    public static string? FindLocaDir(string contentDir, string modFolder, string langSubfolder)
    {
        var nested = Path.Combine(modFolder, "Localization", langSubfolder);
        if (Directory.Exists(nested)) return nested;
        var topLevel = Path.Combine(contentDir, "Localization", langSubfolder);
        if (Directory.Exists(topLevel)) return topLevel;
        return null;
    }

    public static IEnumerable<string> EnumerateLocaFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.xml").Concat(Directory.EnumerateFiles(dir, "*.loca"));
}
