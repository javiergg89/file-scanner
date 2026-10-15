using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FileScanner;

// Context menus for the rows in Recommendations, the breakdown and Duplicates.
// ContextRequested fires on right-click, long press, the Menu key and Shift+F10; RightTapped fires only for the pointer.
public sealed partial class MainWindow
{
    // At the pointer if there is one; from the keyboard, anchored to the focused element.
    static void ShowMenu(MenuFlyout menu, FrameworkElement target, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
    }

    // Handled on the list: with the keyboard, focus is on the ListViewItem, which is the parent of the item template.
    void Rec_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: Recommendation rec } source) ShowMenu(RecMenu(rec), source, args);
    }

    void Entry_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: EntryItem item } source) ShowMenu(EntryMenu(item.Entry), source, args);
    }

    void DuplicateFile_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: DuplicateFileItem item } source) ShowMenu(DuplicateFileMenu(item), source, args);
    }

    MenuFlyout RecMenu(Recommendation rec)
    {
        var first = rec.Nodes.FirstOrDefault() ?? rec.Targets.FirstOrDefault()?.Node;
        var menu = new MenuFlyout();
        menu.Items.Add(ItemAsync(Strings.Map.ViewContentsItem, "", () => OpenDetailAsync(rec)));
        if (first is not null)
        {
            menu.Items.Add(Item(Strings.Map.ShowInChartItem, "", () => ShowInChart(first)));
            menu.Items.Add(Item(Strings.Map.OpenInExplorerItem, "", () => Shell.Open(first.Path)));
        }
        menu.Items.Add(Item(Strings.Map.CopyPathsItem, "", () => CopyText(string.Join(Environment.NewLine, rec.Nodes.Select(n => n.Path).Concat(rec.Targets.Select(t => t.Path)).Distinct()))));
        menu.Items.Add(new MenuFlyoutSeparator());
        if (rec.Risk != Risk.Manual) menu.Items.Add(Item(Strings.Map.CheckAllItem, "", rec.SelectAll));
        menu.Items.Add(Item(Strings.Map.UncheckAllItem, "", rec.Clear));
        if (rec.TargetCount > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemAsync(Strings.Map.DeleteCheckedItem, "", () => DeleteTargetsAsync([.. rec.Targets], rec.Title)));
        }
        return menu;
    }

    MenuFlyout EntryMenu(Entry entry)
    {
        var menu = new MenuFlyout();
        if (entry.Folder is { } node)
        {
            menu.Items.Add(ItemAsync(Strings.Map.EnterItem, "", () => EnterEntryAsync(entry)));
            menu.Items.Add(Item(Strings.Map.ShowInChartItem, "", () => ShowInChart(node)));
            menu.Items.Add(Item(Strings.Map.OpenInExplorerItem, "", () => Shell.Open(entry.Path)));
        }
        else
        {
            menu.Items.Add(Item(Strings.Map.OpenItem, "", () => OpenFile(entry.Path)));
            menu.Items.Add(Item(Strings.Map.ShowInExplorerItem, "", () => Shell.Reveal(entry.Path)));
        }
        menu.Items.Add(Item(Strings.Map.CopyPathItem, "", () => CopyText(entry.Path)));
        menu.Items.Add(new MenuFlyoutSeparator());
        // A Temp or cache base folder is emptied and itself left in place, the same as with its checkbox.
        if (entry.Folder is { } folder && _owner is { KeepFolders: true, Risk: not Risk.Manual } owner && owner.Nodes.Any(n => Paths.Same(n.Path, entry.Path)))
            menu.Items.Add(ItemAsync(Strings.Map.DeleteContentsItem, "", () => DeleteTargetsAsync([new Target(entry.Path, true, folder, 0)], string.Format(CultureInfo.CurrentCulture, Strings.Map.ContentsOf, entry.Path))));
        else
            menu.Items.Add(ItemAsync(Strings.Map.DeleteItem, "", () => DeleteAsync([entry.Path], entry.Path, entry.Size)));
        return menu;
    }

    MenuFlyout DuplicateFileMenu(DuplicateFileItem item)
    {
        string path = item.File.Path;
        var menu = new MenuFlyout();
        menu.Items.Add(Item(Strings.Map.OpenItem, "", () => OpenFile(path)));
        menu.Items.Add(Item(Strings.Map.ShowInExplorerItem, "", () => Shell.Reveal(path)));
        menu.Items.Add(Item(Strings.Map.CopyPathItem, "", () => CopyText(path)));
        if (_root?.FindNearest(path) is { } folder)
            menu.Items.Add(Item(Strings.Map.ShowFolderInChartItem, "", () => ShowInChart(folder)));
        AddExclusionItems(menu, item);
        return menu;
    }
}
