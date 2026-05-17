using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BG3LocTool.App;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void AnyDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static string? FirstDroppedPath(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        return paths.FirstOrDefault();
    }

    private void InputDrop_Drop(object sender, DragEventArgs e)
    {
        var p = FirstDroppedPath(e);
        if (p is not null) Vm.LoadInputMod(p);
    }

    private void RefDrop_Drop(object sender, DragEventArgs e)
    {
        var p = FirstDroppedPath(e);
        if (p is not null) Vm.LoadReferenceChs(p);
    }

    private void BrowseInputFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "BG3 mod (*.pak;*.zip)|*.pak;*.zip|所有文件|*.*" };
        if (dlg.ShowDialog() == true) Vm.LoadInputMod(dlg.FileName);
    }

    private void BrowseInputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) Vm.LoadInputMod(dlg.FolderName);
    }

    private void BrowseRefFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "BG3 mod (*.pak;*.zip)|*.pak;*.zip|所有文件|*.*" };
        if (dlg.ShowDialog() == true) Vm.LoadReferenceChs(dlg.FileName);
    }

    private void BrowseRefFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) Vm.LoadReferenceChs(dlg.FolderName);
    }

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
}
