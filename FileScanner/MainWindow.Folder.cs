using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FileScanner;

// Scan any folder (network ones too): "Elegir carpeta…" in "Qué escanear", drag it onto the window,
// or start the app with its path (Explorer context menu). Only that folder is scanned.
public sealed partial class MainWindow
{
    async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        ScopeFlyout.Hide();
        var picked = await new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id).PickSingleFolderAsync();
        if (picked is null) return;
        ChooseFolder(picked.Path);
        await ScanAsync();
    }

    // Drop is offered only for files or folders; whether it is a folder is known only on drop.
    void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.Caption = Strings.Views.DropToScan;
    }

    async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.OfType<StorageFolder>().FirstOrDefault() is not { } folder)
        {
            ShowStatus(InfoBarSeverity.Informational, Strings.Views.DropNotFolder);
            return;
        }
        ChooseFolder(folder.Path);
        await ScanAsync();
    }

    // The scope becomes that folder alone; it stays as a check box in "Qué escanear" to combine with others.
    void ChooseFolder(string path)
    {
        if (path.Length > 3) path = path.TrimEnd('\\');
        if (!ScopeChoices().Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            FolderPanel.Children.Add(new CheckBox { Content = path, Tag = path });
        _scope = [path];
        UpdateScopeText();
    }
}
