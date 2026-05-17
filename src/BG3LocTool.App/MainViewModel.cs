using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BG3LocTool.Core;
using BG3LocTool.Translators;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BG3LocTool.App;

public sealed record TranslatorPreset(string Display, string BaseUrl, string Model)
{
    public override string ToString() => Display;
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string? _inputModPath;
    [ObservableProperty] private string _inputSummary = "拖到这里或点击浏览…";

    public string GlossaryStatus
    {
        get
        {
            var g = Bg3OfficialGlossary.Default;
            return g is null
                ? "📚 官方字典: 未加载"
                : $"📚 官方字典 (内置): {g.ReverseEntryCount:N0} 整句反查 + {g.TermCount:N0} 术语约束";
        }
    }

    [ObservableProperty] private string? _referenceChsPath;
    [ObservableProperty] private string _referenceSummary = "拖旧 CHS .pak/.zip/文件夹 或留空";

    public enum WorkMode { FromScratch, Upgrade }

    [ObservableProperty] private WorkMode _mode = WorkMode.FromScratch;
    public bool IsScratchMode { get => Mode == WorkMode.FromScratch; set { if (value) Mode = WorkMode.FromScratch; } }
    public bool IsUpgradeMode { get => Mode == WorkMode.Upgrade; set { if (value) Mode = WorkMode.Upgrade; } }
    public string ModeHint => Mode == WorkMode.FromScratch
        ? "首次汉化:生成全新 UUID,Folder = <原名>_CHS,所有英文条目都送翻译。"
        : "增量更新:沿用旧 UUID/Folder(玩家无感升级),英文未变的条目复用旧译,只重译新增/改动。";

    public ObservableCollection<TranslatorPreset> Presets { get; } = new()
    {
        new("Databricks", "https://<your-workspace>.cloud.databricks.com/serving-endpoints/<endpoint>/invocations", "databricks-claude-opus-4-7"),
        new("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat"),
        new("OpenAI", "https://api.openai.com/v1", "gpt-4o-mini"),
        new("Ollama", "http://localhost:11434/v1", "qwen2.5:14b"),
        new("自定义", "", ""),
    };

    [ObservableProperty] private TranslatorPreset? _selectedPreset;
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "";

    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _versionText = "1.0.0.1";
    [ObservableProperty] private string _targetLangFolder = "Chinese";
    [ObservableProperty] private string _targetLangCode = "zh-CN";

    [ObservableProperty] private string _outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BG3CHS");
    [ObservableProperty] private string _outputFileName = "";   // empty = auto-derive from mod's Name
    public bool EmitBg3mmZip => true;

    [ObservableProperty] private string? _stageDirAfterRun;
    [ObservableProperty] private string? _modFolderAfterRun;
    [ObservableProperty] private MetaModuleInfo? _metaAfterRun;
    public bool HasStaged => !string.IsNullOrEmpty(StageDirAfterRun);
    partial void OnStageDirAfterRunChanged(string? value) => OnPropertyChanged(nameof(HasStaged));

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMax = 1;
    [ObservableProperty] private string _progressText = "";

    [ObservableProperty] private string _logText = "";

    [ObservableProperty] private string? _lastOutputDir;
    public bool HasLastOutput => !string.IsNullOrEmpty(LastOutputDir);

    [ObservableProperty] private int _lastFailedCount;
    public bool HasFailures => LastFailedCount > 0;
    partial void OnLastFailedCountChanged(int value) => OnPropertyChanged(nameof(HasFailures));

    [ObservableProperty] private EditorViewModel? _embeddedEditor;
    public bool HasEmbeddedEditor => EmbeddedEditor != null;
    partial void OnEmbeddedEditorChanged(EditorViewModel? value) => OnPropertyChanged(nameof(HasEmbeddedEditor));

    private CancellationTokenSource? _cts;
    private bool _settingsLoaded;

    public MainViewModel()
    {
        var s = AppSettings.Load();
        Mode = s.Mode == "Upgrade" ? WorkMode.Upgrade : WorkMode.FromScratch;
        BaseUrl = s.BaseUrl;
        ApiKey = s.ApiKey;
        Model = s.Model;
        Author = s.Author;
        VersionText = s.VersionText;
        OutputDir = s.OutputDir;
        SelectedPreset = Presets.FirstOrDefault(p => p.Display == s.SelectedPreset) ?? Presets[0];
        _settingsLoaded = true;
    }

    private void SaveSettings()
    {
        if (!_settingsLoaded) return;
        try
        {
            new AppSettings
            {
                Mode = Mode == WorkMode.Upgrade ? "Upgrade" : "FromScratch",
                SelectedPreset = SelectedPreset?.Display ?? "DeepSeek",
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                Model = Model,
                Author = Author,
                VersionText = VersionText,
                OutputDir = OutputDir,
            }.Save();
        }
        catch { }
    }

    partial void OnBaseUrlChanged(string value) => SaveSettings();
    partial void OnApiKeyChanged(string value) => SaveSettings();
    partial void OnModelChanged(string value) => SaveSettings();
    partial void OnAuthorChanged(string value) => SaveSettings();
    partial void OnVersionTextChanged(string value) => SaveSettings();
    partial void OnOutputDirChanged(string value) => SaveSettings();

    partial void OnSelectedPresetChanged(TranslatorPreset? value)
    {
        if (value is null) return;
        if (value.Display != "自定义")
        {
            BaseUrl = value.BaseUrl;
            Model = value.Model;
        }
        SaveSettings();
    }

    partial void OnModeChanged(WorkMode value)
    {
        OnPropertyChanged(nameof(IsScratchMode));
        OnPropertyChanged(nameof(IsUpgradeMode));
        OnPropertyChanged(nameof(ModeHint));
        if (value == WorkMode.FromScratch && !string.IsNullOrEmpty(ReferenceChsPath))
            LoadReferenceChs(null);
        SaveSettings();
    }

    partial void OnLastOutputDirChanged(string? value) => OnPropertyChanged(nameof(HasLastOutput));

    public void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        LogText += line;
        FileLogger.Append("Main", msg);
    }

    public void LoadInputMod(string path)
    {
        InputModPath = path;
        try
        {
            using var src = ModSource.Open(path);
            var modFolder = src.FindModFolder();
            var metaPath = Path.Combine(modFolder, "meta.lsx");
            int entries = CountEnglishEntries(src.ContentDir, modFolder);
            string desc = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            if (File.Exists(metaPath))
            {
                var meta = MetaEditor.ReadModuleInfo(metaPath);
                desc = $"{meta.Name} ({meta.Folder})";
                if (string.IsNullOrEmpty(Author)) Author = meta.Author;
                if (string.IsNullOrEmpty(ReferenceChsPath))
                    VersionText = Bg3Version.Decode(meta.Version64).ToString();
                // auto-suggest a friendly output filename; user can override
                OutputFileName = TranslationPipeline.DefaultOutputBaseName(meta);
            }
            InputSummary = $"当前: {desc}  ({entries} entries)";
            Log($"已载入 mod: {desc} ({entries} 条)");
        }
        catch (Exception ex)
        {
            InputSummary = $"无法读取: {Path.GetFileName(path)}";
            Log($"读取 mod 失败: {ex.Message}");
        }
    }

    public void LoadReferenceChs(string? path)
    {
        ReferenceChsPath = path;
        if (string.IsNullOrEmpty(path))
        {
            ReferenceSummary = "拖旧 CHS .pak/.zip/文件夹 或留空";
            return;
        }
        try
        {
            using var src = ModSource.Open(path);
            var modFolder = src.FindModFolder();
            var metaPath = Path.Combine(modFolder, "meta.lsx");
            string desc = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            if (File.Exists(metaPath))
            {
                var meta = MetaEditor.ReadModuleInfo(metaPath);
                desc = $"{meta.Name} ({meta.Folder})";
                Author = meta.Author;
                VersionText = Bg3Version.Decode(meta.Version64).ToString();
            }
            ReferenceSummary = $"当前: {desc}  [升级模式 ✓]";
            Log($"已载入参考 CHS: {desc}");
        }
        catch (Exception ex)
        {
            ReferenceSummary = $"无法读取: {Path.GetFileName(path)}";
            Log($"读取参考包失败: {ex.Message}");
        }
    }

    private static int CountEnglishEntries(string contentDir, string modFolder)
    {
        var locDir = TranslationPipeline.FindLocaDir(contentDir, modFolder, "English");
        if (locDir == null) return 0;
        int n = 0;
        foreach (var f in TranslationPipeline.EnumerateLocaFiles(locDir))
        {
            try { n += LocaIO.Load(f).Count; } catch { }
        }
        return n;
    }

    [RelayCommand]
    private void ClearReference()
    {
        LoadReferenceChs(null);
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(LastOutputDir) && Directory.Exists(LastOutputDir))
            Process.Start("explorer.exe", LastOutputDir);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenEditor()
    {
        try
        {
            EditorContext? ctx = null;
            // Prefer the live stage dir (review mode); if it's gone (non-review mode after pack), fall back to the produced pak.
            if (!string.IsNullOrEmpty(StageDirAfterRun) && Directory.Exists(StageDirAfterRun) && !string.IsNullOrEmpty(ModFolderAfterRun) && MetaAfterRun is not null)
                ctx = EditorContext.FromStaged(StageDirAfterRun, ModFolderAfterRun!, TargetLangFolder, MetaAfterRun);
            else if (!string.IsNullOrEmpty(LastOutputDir))
            {
                var pak = Directory.GetFiles(LastOutputDir, "*.pak").FirstOrDefault();
                if (pak != null) ctx = EditorContext.FromPak(pak, TargetLangFolder);
            }
            if (ctx is null) { Log("无可编辑内容,请先跑一次翻译"); return; }
            var w = new EditorWindow();
            w.LoadContext(ctx);
            w.SetOutputDir(OutputDir);
            w.SetEmitZip(EmitBg3mmZip);
            w.Show();
        }
        catch (Exception ex) { Log($"打开编辑器失败: {ex.Message}"); }
    }

    [RelayCommand]
    private void ViewFailures()
    {
        if (string.IsNullOrEmpty(StageDirAfterRun) || string.IsNullOrEmpty(ModFolderAfterRun) || MetaAfterRun is null)
        {
            Log("无可查看的 stage 目录(失败列表已丢失,需重跑并勾选「翻译完先打开编辑器」)");
            return;
        }
        try
        {
            var ctx = EditorContext.FromStaged(StageDirAfterRun, ModFolderAfterRun, TargetLangFolder, MetaAfterRun);
            var w = new EditorWindow();
            w.LoadContext(ctx);
            w.SetOutputDir(OutputDir);
            w.SetEmitZip(EmitBg3mmZip);
            w.JumpToFilter("失败");
            w.Show();
        }
        catch (Exception ex) { Log($"查看失败列表失败: {ex.Message}"); }
    }

    [RelayCommand]
    private void ManageCache()
    {
        try
        {
            using var cache = new TranslationCache();
            var stats = cache.Stats();
            var total = stats.Sum(s => s.Count);
            Log($"缓存:{total} 条,跨 {stats.Count} 个 mod");
            foreach (var s in stats.Take(10))
                Log($"  · {(string.IsNullOrEmpty(s.ModUuid) ? "(全局)" : s.ModUuid[..8])} → {s.Count} 条");
            if (System.Windows.MessageBox.Show($"清空全部 {total} 条翻译缓存?(下次翻译相同条目要重新调 API)", "确认清缓存",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes)
            {
                var n = cache.Clear();
                Log($"✓ 已清 {n} 条");
            }
        }
        catch (Exception ex) { Log($"管理缓存失败: {ex.Message}"); }
    }

    [RelayCommand]
    private void OpenExistingChsForEdit()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "BG3 mod (*.pak;*.zip)|*.pak;*.zip|所有文件|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var ctx = EditorContext.FromPak(dlg.FileName, TargetLangFolder);
            var w = new EditorWindow();
            w.LoadContext(ctx);
            w.SetOutputDir(OutputDir);
            w.SetEmitZip(EmitBg3mmZip);
            w.Show();
        }
        catch (Exception ex) { Log($"打开已有 CHS 失败: {ex.Message}"); }
    }

    public bool CanRun => !IsRunning && !string.IsNullOrEmpty(InputModPath) && !string.IsNullOrEmpty(OutputDir);

    [RelayCommand]
    public async Task DryRunAsync()
    {
        if (string.IsNullOrEmpty(InputModPath)) { Log("先拖入 mod"); return; }
        ITranslator? translator = null;
        try
        {
            translator = new OpenAICompatibleTranslator(new OpenAIOptions { BaseUrl = BaseUrl, ApiKey = ApiKey, Model = Model });

            using var src = ModSource.Open(InputModPath);
            var modFolder = src.FindModFolder();
            var locDir = TranslationPipeline.FindLocaDir(src.ContentDir, modFolder, "English");
            if (locDir == null) { Log("[DRY] 找不到 English 目录"); return; }

            var allSources = new List<string>();
            foreach (var f in TranslationPipeline.EnumerateLocaFiles(locDir))
            {
                try
                {
                    foreach (var e in LocaIO.Load(f))
                        if (!string.IsNullOrWhiteSpace(e.Text)) allSources.Add(e.Text);
                }
                catch { }
            }
            if (allSources.Count == 0) { Log("[DRY] 没有非空英文条目"); return; }

            var rng = new Random();
            var picks = allSources.OrderBy(_ => rng.Next()).Take(5).ToList();

            IsRunning = true;
            Log($"[DRY] 试译 {picks.Count} 条 (translator={translator.Name})…");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var results = await translator.TranslateBatchAsync(picks, TargetLangFolder, null, cts.Token);
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Translation != null) Log($"[DRY] {Trim80(r.Source)}\n   → {Trim80(r.Translation)}");
                else Log($"[DRY] {Trim80(r.Source)}\n   ✗ {r.Error}");
            }
        }
        catch (Exception ex) { Log($"[DRY] 失败: {ex.Message}"); }
        finally
        {
            (translator as IDisposable)?.Dispose();
            IsRunning = false;
        }
    }

    private static string Trim80(string s) => s.Length <= 80 ? s : s[..80] + "…";

    [RelayCommand]
    public async Task RunAsync()
    {
        if (string.IsNullOrEmpty(InputModPath))
        {
            Log("错误: 未选择输入 mod");
            return;
        }
        if (Mode == WorkMode.Upgrade && string.IsNullOrEmpty(ReferenceChsPath))
        {
            Log("错误: 升级模式需要旧汉化包(切到「从零翻译」或拖入旧 CHS)");
            return;
        }
        if (!Bg3VersionExt.TryParseSafe(VersionText, out var ver))
        {
            Log($"错误: 版本号解析失败: {VersionText}");
            return;
        }

        ITranslator translator;
        try
        {
            translator = new OpenAICompatibleTranslator(new OpenAIOptions
            {
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                Model = Model,
            });
        }
        catch (Exception ex)
        {
            Log($"翻译器初始化失败: {ex.Message}");
            return;
        }

        Directory.CreateDirectory(OutputDir);

        var cfg = new PipelineConfig(
            InputModPath: InputModPath,
            ReferenceChsPath: Mode == WorkMode.Upgrade ? ReferenceChsPath : null,
            OutputDir: OutputDir,
            Author: string.IsNullOrEmpty(Author) ? "Unknown" : Author,
            ChsVersion: ver,
            TargetLangFolder: TargetLangFolder,
            TargetLangCode: TargetLangCode,
            Translator: translator,
            EmitBg3mmZip: true,
            KeepStageDir: true,
            OutputBaseName: string.IsNullOrWhiteSpace(OutputFileName) ? null : OutputFileName);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressValue = 0;
        ProgressMax = 1;
        ProgressText = "准备…";
        LastOutputDir = null;

        var progress = new Progress<PipelineProgress>(p =>
        {
            ProgressMax = Math.Max(1, p.Total);
            ProgressValue = p.Done;
            var pct = p.Total > 0 ? (int)(100.0 * p.Done / p.Total) : 0;
            ProgressText = $"{pct}% ({p.Done}/{p.Total}) {p.Stage} {p.Message}".Trim();
            if (!string.IsNullOrEmpty(p.Message))
                Log($"[{p.Stage}] {p.Message}");
        });

        try
        {
            Log($"开始: {Path.GetFileName(InputModPath)} → {OutputDir}");
            var result = await Task.Run(() => TranslationPipeline.RunAsync(cfg, progress, _cts.Token), _cts.Token);
            StageDirAfterRun = result.StageDir;
            ModFolderAfterRun = result.ModFolder;
            MetaAfterRun = result.Meta;
            LastFailedCount = result.Failed;

            Log($"✓ 完成 — pak: {result.PakPath}");
            if (!string.IsNullOrEmpty(result.Bg3mmZipPath)) Log($"✓ BG3MM zip: {result.Bg3mmZipPath}");
            LastOutputDir = OutputDir;
            try { Process.Start("explorer.exe", OutputDir); } catch { }

            // Editor reads from the kept stage dir; takes ownership so it cleans up on Dispose
            try
            {
                EmbeddedEditor?.Context?.Dispose();
                var ctx = EditorContext.FromStaged(result.StageDir, result.ModFolder, TargetLangFolder, result.Meta, ownsStageDir: true);
                var vm = new EditorViewModel { OutputDir = OutputDir, EmitBg3mmZip = true };
                vm.Load(ctx);
                EmbeddedEditor = vm;
            }
            catch (Exception ex) { Log($"加载对照视图失败: {ex.Message}"); }
            Log($"统计: 官方 {result.FromOfficial} / 参考 {result.FromReference} / 缓存 {result.FromCache} / 新译 {result.Translated} / 失败 {result.Failed}{(result.UpgradeMode ? " [升级模式]" : "")}");
            if (result.Failed > 0)
                Log($"⚠️ {result.Failed} 条翻译失败(占位符校验未通过)— 点 [🔍 查看失败] 在编辑器中审查");
            if (result.Verification is { } v)
            {
                Log($"验证: {v.Verdict}");
                if (v.IsHollowShell)
                    Log("🚨🚨🚨 这个翻译包是空壳!装进游戏看到的全是英文。请检查 API 配置 + 点 [🧪 试译 5 条] 排查后重跑。");
                else if (v.IsLowQuality)
                    Log("🚨 翻译率 < 50%,出包前强烈建议进编辑器手动核对。");
            }
        }
        catch (OperationCanceledException)
        {
            Log("已取消");
        }
        catch (Exception ex)
        {
            Log($"✗ 失败: {ex.Message}");
        }
        finally
        {
            (translator as IDisposable)?.Dispose();
            IsRunning = false;
            ProgressText = "";
            _cts?.Dispose();
            _cts = null;
        }
    }
}

internal static class Bg3VersionExt
{
    public static bool TryParseSafe(string s, out Bg3Version v)
    {
        try { v = Bg3Version.Parse(s); return true; }
        catch { v = default; return false; }
    }
}
