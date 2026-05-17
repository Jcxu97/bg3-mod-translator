using System.IO;
using BG3LocTool.Core;

namespace BG3LocTool.App;

public sealed class EditorContext : IDisposable
{
    public string StageDir { get; }
    public string ModFolder { get; }
    public string LangFolder { get; }
    public MetaModuleInfo Meta { get; private set; }
    public List<TranslationRow> Rows { get; }
    private readonly bool _ownsStageDir;

    private EditorContext(string stageDir, string modFolder, string langFolder, MetaModuleInfo meta, List<TranslationRow> rows, bool ownsStageDir)
    {
        StageDir = stageDir;
        ModFolder = modFolder;
        LangFolder = langFolder;
        Meta = meta;
        Rows = rows;
        _ownsStageDir = ownsStageDir;
    }

    public static EditorContext FromStaged(string stageDir, string modFolder, string langFolder, MetaModuleInfo meta, bool ownsStageDir = false)
    {
        var rows = LoadRows(modFolder, langFolder);
        return new EditorContext(stageDir, modFolder, langFolder, meta, rows, ownsStageDir);
    }

    public static EditorContext FromPak(string pakOrZipPath, string langFolder = "Chinese")
    {
        using var src = ModSource.Open(pakOrZipPath);
        var srcModFolder = src.FindModFolder();
        var meta = MetaEditor.ReadModuleInfo(Path.Combine(srcModFolder, "meta.lsx"));

        var stageDir = Path.Combine(Path.GetTempPath(), "BG3LocTool_edit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageDir);
        CopyDirectory(src.ContentDir, stageDir);

        var stagedMod = Path.Combine(stageDir, "Mods", meta.Folder);
        if (!Directory.Exists(stagedMod))
        {
            var modsDir = Path.Combine(stageDir, "Mods");
            var dirs = Directory.GetDirectories(modsDir);
            if (dirs.Length == 1) stagedMod = dirs[0];
        }

        var rows = LoadRows(stagedMod, langFolder);
        return new EditorContext(stageDir, stagedMod, langFolder, meta, rows, ownsStageDir: true);
    }

    private static Dictionary<string, RowSource> LoadSidecar(string modFolder)
    {
        var path = Path.Combine(modFolder, "_translation_status.json");
        if (!File.Exists(path)) return new();
        try
        {
            var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            var result = new Dictionary<string, RowSource>(raw?.Count ?? 0);
            if (raw != null)
                foreach (var (k, v) in raw)
                    if (Enum.TryParse<RowSource>(v, ignoreCase: true, out var rs))
                        result[k] = rs;
            return result;
        }
        catch { return new(); }
    }

    private static List<TranslationRow> LoadRows(string modFolder, string langFolder)
    {
        var rows = new List<TranslationRow>();
        var sidecar = LoadSidecar(modFolder);
        var enDir = Path.Combine(modFolder, "Localization", "English");
        if (!Directory.Exists(enDir))
        {
            // fallback: top-level Localization (rare for staged but safe)
            var contentDir = Path.GetDirectoryName(Path.GetDirectoryName(modFolder))!;
            var alt = Path.Combine(contentDir, "Localization", "English");
            if (Directory.Exists(alt)) enDir = alt;
            else return rows;
        }
        var langDir = Path.Combine(modFolder, "Localization", langFolder);

        foreach (var enFile in TranslationPipeline.EnumerateLocaFiles(enDir))
        {
            var fileName = Path.GetFileNameWithoutExtension(enFile) + ".xml";
            var enEntries = LocaIO.Load(enFile);

            var langIndex = new Dictionary<string, LocaEntry>();
            if (Directory.Exists(langDir))
            {
                var langFile = Path.Combine(langDir, fileName);
                if (!File.Exists(langFile))
                {
                    // try same-name .loca too
                    var alt = Path.Combine(langDir, Path.GetFileName(enFile));
                    if (File.Exists(alt)) langFile = alt;
                }
                if (File.Exists(langFile))
                    foreach (var e in LocaIO.Load(langFile))
                        langIndex[e.Handle] = e;
            }

            foreach (var en in enEntries)
            {
                langIndex.TryGetValue(en.Handle, out var ch);
                RowSource? src = sidecar.TryGetValue($"{fileName}|{en.Handle}", out var s) ? s : null;
                rows.Add(new TranslationRow
                {
                    SourceFile = fileName,
                    Handle = en.Handle,
                    Version = ch?.Version ?? en.Version,
                    English = en.Text,
                    Chinese = ch?.Text ?? "",
                    Source = src,
                });
            }
        }
        return rows;
    }

    public void Save()
    {
        var langDir = Path.Combine(ModFolder, "Localization", LangFolder);
        Directory.CreateDirectory(langDir);

        foreach (var group in Rows.GroupBy(r => r.SourceFile))
        {
            var entries = group.Select(r => new LocaEntry(r.Handle, r.Version, r.Chinese)).ToList();
            LocaIO.SaveXml(entries, Path.Combine(langDir, group.Key));
        }
        foreach (var r in Rows) r.IsDirty = false;
    }

    public Task<(string Pak, string? Zip)> ExportAsync(string outputDir, bool emitZip, IProgress<PipelineProgress>? progress = null)
    {
        Save();
        return InvokePack(outputDir, emitZip, progress);
    }

    private async Task<(string Pak, string? Zip)> InvokePack(string outputDir, bool emitZip, IProgress<PipelineProgress>? progress)
    {
        var (pak, zip) = await TranslationPipeline.PackAsync(StageDir, outputDir, Meta, emitZip, progress);
        return (pak, zip);
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), overwrite: true);
    }

    public void Dispose()
    {
        if (_ownsStageDir && Directory.Exists(StageDir))
            try { Directory.Delete(StageDir, true); } catch { }
    }
}
