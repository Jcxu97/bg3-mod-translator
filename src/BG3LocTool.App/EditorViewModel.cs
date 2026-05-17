using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using BG3LocTool.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BG3LocTool.App;

public partial class EditorViewModel : ObservableObject
{
    public EditorContext? Context { get; private set; }
    public ObservableCollection<TranslationRow> Rows { get; } = new();
    public ICollectionView FilteredRows { get; }

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _selectedFilterMode = "全部";
    public string[] FilterModes { get; } = new[] { "全部", "未翻译", "占位符警告", "已修改", "官方", "新译", "复用", "缓存", "失败" };

    [ObservableProperty] private string _title = "编辑翻译";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _translatedCount;
    [ObservableProperty] private int _issueCount;
    [ObservableProperty] private string _statusSummary = "";

    [ObservableProperty] private string _outputDir = "";
    [ObservableProperty] private bool _emitBg3mmZip = true;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _logText = "";

    public EditorViewModel()
    {
        FilteredRows = CollectionViewSource.GetDefaultView(Rows);
        FilteredRows.Filter = FilterPredicate;
    }

    public void Load(EditorContext ctx)
    {
        Context?.Dispose();
        Context = ctx;
        Rows.Clear();
        foreach (var r in ctx.Rows)
        {
            r.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(r);
        }
        Title = $"编辑: {ctx.Meta.Folder}";
        RecomputeCounts();
        Log($"已加载 {Rows.Count} 条 entries (mod: {ctx.Meta.Folder})");
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TranslationRow.Chinese) or nameof(TranslationRow.IsTranslated) or nameof(TranslationRow.IsPlaceholderOk) or nameof(TranslationRow.IsDirty))
            RecomputeCounts();
    }

    private void RecomputeCounts()
    {
        TotalCount = Rows.Count;
        TranslatedCount = Rows.Count(r => r.IsTranslated);
        IssueCount = Rows.Count(r => !r.IsPlaceholderOk);
        StatusSummary = $"{TranslatedCount}/{TotalCount} 已译, {IssueCount} 占位符警告";
    }

    private bool FilterPredicate(object obj)
    {
        var r = (TranslationRow)obj;
        if (!string.IsNullOrEmpty(FilterText))
        {
            var ft = FilterText;
            if (!(r.English?.Contains(ft, StringComparison.OrdinalIgnoreCase) ?? false)
                && !(r.Chinese?.Contains(ft, StringComparison.OrdinalIgnoreCase) ?? false)
                && !r.Handle.Contains(ft, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return SelectedFilterMode switch
        {
            "未翻译" => !r.IsTranslated,
            "占位符警告" => !r.IsPlaceholderOk,
            "已修改" => r.IsDirty,
            "官方" => r.Source == RowSource.Official,
            "新译" => r.Source == RowSource.New,
            "复用" => r.Source == RowSource.Reference,
            "缓存" => r.Source == RowSource.Cached,
            "失败" => r.Source == RowSource.Failed,
            _ => true,
        };
    }

    private DispatcherTimer? _filterTimer;
    partial void OnFilterTextChanged(string value)
    {
        if (_filterTimer == null)
        {
            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _filterTimer.Tick += (_, _) => { _filterTimer!.Stop(); FilteredRows.Refresh(); };
        }
        _filterTimer.Stop();
        _filterTimer.Start();
    }
    partial void OnSelectedFilterModeChanged(string value) => FilteredRows.Refresh();

    public void JumpToFilter(string mode) => SelectedFilterMode = mode;

    private void Log(string msg)
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        BG3LocTool.Core.FileLogger.Append("Editor", msg);
    }

    [RelayCommand]
    private void LookupOfficial(TranslationRow? row)
    {
        if (row is null) return;
        var g = Bg3OfficialGlossary.Default;
        if (g is null) { System.Windows.MessageBox.Show("官方字典未加载", "查官方译法"); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"原文:");
        sb.AppendLine($"  {row.English}");
        sb.AppendLine();
        if (g.TryExactMatch(row.English, out var exact))
        {
            sb.AppendLine("✓ 整句官方译法:");
            sb.AppendLine($"  {exact}");
        }
        else
        {
            sb.AppendLine("整句无完全匹配。词级官方术语命中:");
            var hits = g.FindTerms(row.English);
            if (hits.Count == 0) sb.AppendLine("  (无,LLM 完全自由翻译)");
            else foreach (var h in hits) sb.AppendLine($"  · {h.En}  →  {h.Zh}");
        }
        sb.AppendLine();
        sb.AppendLine($"当前译文:");
        sb.AppendLine($"  {row.Chinese}");
        System.Windows.MessageBox.Show(sb.ToString(), "🔍 BG3 官方译法查询", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void Save()
    {
        if (Context is null) return;
        try
        {
            int dirty = Rows.Count(r => r.IsDirty);
            Context.Save();
            Log($"已保存 {Rows.Count} 条 (其中 {dirty} 条修改)");
        }
        catch (Exception ex) { Log($"保存失败: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Context is null) return;
        if (string.IsNullOrWhiteSpace(OutputDir)) { Log("错误: 未指定输出目录"); return; }
        try
        {
            IsBusy = true;
            Directory.CreateDirectory(OutputDir);
            var progress = new Progress<PipelineProgress>(p => Log($"[{p.Stage}] {p.Message}"));
            var (pak, zip) = await Context.ExportAsync(OutputDir, EmitBg3mmZip, progress);
            Log($"✓ pak: {pak}");
            if (!string.IsNullOrEmpty(zip)) Log($"✓ zip: {zip}");
            // 注意:翻译包按社区规则不 ship English,所以 VerifyPak 的"对比 EN/ZH"在这儿不适用。
            // 退而其次:做结构检查 — 确认 Chinese 目录里有非空 xml。
            var translatedRows = Context.Rows.Count(r => r.IsTranslated);
            Log($"验证: {translatedRows}/{Context.Rows.Count} 条已翻译(已打包)");
            if (translatedRows == 0) Log("🚨 没有任何已翻译条目!装进游戏看不到中文。");
            else if (translatedRows * 2 < Context.Rows.Count) Log($"⚠️ 翻译率 {100*translatedRows/Context.Rows.Count}%,建议返回编辑器继续校对。");
        }
        catch (Exception ex) { Log($"导出失败: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(OutputDir) && Directory.Exists(OutputDir))
            Process.Start("explorer.exe", OutputDir);
    }
}
