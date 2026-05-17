using CommunityToolkit.Mvvm.ComponentModel;

namespace BG3LocTool.App;

public partial class TranslationRow : ObservableObject
{
    public string SourceFile { get; init; } = "";
    public string Handle { get; init; } = "";
    public ushort Version { get; set; }
    public string English { get; init; } = "";
    public BG3LocTool.Core.RowSource? Source { get; init; }

    [ObservableProperty] private string _chinese = "";
    [ObservableProperty] private bool _isDirty;

    public bool IsTranslated => !string.IsNullOrEmpty(Chinese) && Chinese != English;
    public bool IsPlaceholderOk =>
        string.IsNullOrEmpty(Chinese) || BG3LocTool.Core.PlaceholderValidator.IsValid(English, Chinese);

    public string HandleShort => Handle.Length > 12 ? Handle[..12] + "…" : Handle;

    public string SourceBadge => Source switch
    {
        BG3LocTool.Core.RowSource.Official  => "🏛官方",
        BG3LocTool.Core.RowSource.New       => "🆕新译",
        BG3LocTool.Core.RowSource.Reference => "♻️复用",
        BG3LocTool.Core.RowSource.Cached    => "📋缓存",
        BG3LocTool.Core.RowSource.Failed    => "❌失败",
        _ => "",
    };

    partial void OnChineseChanged(string value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsTranslated));
        OnPropertyChanged(nameof(IsPlaceholderOk));
    }
}
