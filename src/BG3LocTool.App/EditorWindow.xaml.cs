using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BG3LocTool.Core;
using Microsoft.Win32;

namespace BG3LocTool.App;

public partial class EditorWindow : Window
{
    public static readonly RoutedCommand FocusSearchCommand = new();
    public static readonly RoutedCommand NextFailureCommand = new();

    private EditorViewModel Vm => (EditorViewModel)DataContext;

    public EditorWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Vm.Context?.Dispose();
    }

    public void LoadContext(EditorContext ctx) => Vm.Load(ctx);
    public void SetOutputDir(string dir) => Vm.OutputDir = dir;
    public void SetEmitZip(bool v) => Vm.EmitBg3mmZip = v;
    public void JumpToFilter(string mode) => Vm.JumpToFilter(mode);

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (Directory.Exists(Vm.OutputDir)) dlg.InitialDirectory = Vm.OutputDir;
        if (dlg.ShowDialog() == true) Vm.OutputDir = dlg.FolderName;
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }

    private void OnFocusSearch(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnNextFailure(object sender, ExecutedRoutedEventArgs e)
    {
        var view = Vm.FilteredRows;
        if (view == null) return;
        var items = view.Cast<TranslationRow>().ToList();
        if (items.Count == 0) return;

        int startIdx = EditorGrid.SelectedItem is TranslationRow cur ? items.IndexOf(cur) + 1 : 0;
        for (int offset = 0; offset < items.Count; offset++)
        {
            var idx = (startIdx + offset) % items.Count;
            var r = items[idx];
            if (!r.IsPlaceholderOk || r.Source == RowSource.Failed)
            {
                EditorGrid.SelectedItem = r;
                EditorGrid.CurrentItem = r;
                EditorGrid.ScrollIntoView(r);
                return;
            }
        }
    }
}
