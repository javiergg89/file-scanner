using System.Globalization;
using System.Text.Json;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace FileScanner;

// Duplicate search exclusions: folders, extensions and groups marked as not duplicates.
// Saved immediately and applied to the shown results without a new search.
// Removing an exclusion does not bring back what it hid; that needs a new search, and the summary says so.
public sealed partial class MainWindow
{
    DuplicateExclusions _exclusions = new();

    void InitExclusions()
    {
        try
        {
            _exclusions = DuplicateExclusions.Load(DuplicateExclusions.DefaultFile);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Warning, string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.LoadFailed, ex.Message));
        }
        UpdateExclusionsButton();
    }

    void UpdateExclusionsButton() =>
        ExclusionsButton.Content = _exclusions.Count == 0 ? Strings.Duplicates.ExclusionsButton : string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.ExclusionsButtonCount, _exclusions.Count);

    void SaveExclusions()
    {
        try
        {
            // Demo mode applies the change but writes nothing.
            if (!App.Demo) _exclusions.Save(DuplicateExclusions.DefaultFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Error, string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.SaveFailed, ex.Message));
        }
        UpdateExclusionsButton();
        ApplyExclusions();
    }

    // Removes excluded items from the results and unmarks copies that are no longer shown.
    void ApplyExclusions()
    {
        var kept = WithoutExclusions(_duplicates.Select(g => g.Group));
        var shown = kept.SelectMany(g => g.Files).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var marked = _duplicateSelection.Targets.Where(t => shown.Contains(t.Path)).ToList();
        if (marked.Count != _duplicateSelection.TargetCount) _duplicateSelection.SetFiles(marked);
        ShowDuplicates(kept, replace: false);
    }

    // Drops ignored groups, copies in excluded folders or with excluded extensions,
    // and groups left with one copy. Order is unchanged.
    List<DuplicateGroup> WithoutExclusions(IEnumerable<DuplicateGroup> groups) => groups
        .Where(g => !_exclusions.Ignores(g.Hash))
        .Select(g => g with { Files = [.. g.Files.Where(f => !_exclusions.ExcludesFile(f.Path))] })
        .Where(g => g.Files.Count > 1)
        .ToList();

    void IgnoreGroup(DuplicateGroup group)
    {
        _exclusions.Ignore(new IgnoredGroup(group.Hash, Path.GetFileName(group.Files[0].Path), group.Size));
        SaveExclusions();
    }

    void IgnoreGroup_Click(object sender, RoutedEventArgs e) =>
        IgnoreGroup(((DuplicateGroupItem)((FrameworkElement)sender).DataContext).Group);

    // Exclusion items for a copy's context menu.
    void AddExclusionItems(MenuFlyout menu, DuplicateFileItem item)
    {
        string path = item.File.Path;
        menu.Items.Add(new MenuFlyoutSeparator());
        if (Path.GetDirectoryName(path) is { } folder)
        {
            var excludeFolder = Item(string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.ExcludeFolderItem, Path.GetFileName(folder)), "\uE8F8", () =>
            {
                _exclusions.AddFolder(folder);
                SaveExclusions();
            });
            ToolTipService.SetToolTip(excludeFolder, folder);
            menu.Items.Add(excludeFolder);
        }
        if (DuplicateExclusions.NormalizeExtension(Path.GetExtension(path)) is { } extension)
            menu.Items.Add(Item(string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.ExcludeExtensionItem, extension), "\uE8C9", () =>
            {
                _exclusions.AddExtension(extension);
                SaveExclusions();
            }));
        menu.Items.Add(Item(Strings.Duplicates.IgnoreGroupItem, "\uE894", () => IgnoreGroup(item.Group.Group)));
    }

    async void Exclusions_Click(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Spacing = 16, Width = 640 };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = Strings.Duplicates.DialogTitle,
            CloseButtonText = Strings.Duplicates.CloseButton,
            Content = new ScrollViewer { Content = body, MaxHeight = 520 },
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        // Rebuilding destroys the focused control, so focus moves to the section that changed.
        void Rebuild(int focus)
        {
            body.Children.Clear();
            body.Children.Add(FolderSection(() => Rebuild(0)));
            body.Children.Add(ExtensionSection(() => Rebuild(1)));
            body.Children.Add(Section(Strings.Duplicates.GroupsTitle, Strings.Duplicates.GroupsHint,
                _exclusions.Groups.Select(g => ($"{g.Name} · {DirNode.Format(g.Size)}", Remove(() => _exclusions.Groups.Remove(g), () => Rebuild(2)))),
                Strings.Duplicates.NoGroups, null));
            if (focus < 0) return;
            var controls = Controls((Panel)body.Children[focus]).ToList();
            var target = controls.OfType<TextBox>().FirstOrDefault() ?? controls.FirstOrDefault() ?? Controls(body).FirstOrDefault();
            target?.Focus(FocusState.Keyboard);
        }
        Rebuild(-1);
        await dialog.ShowAsync();
        // Button lambdas capture Rebuild and, through it, body; clearing body on close breaks that cycle.
        body.Children.Clear();
    }

    static IEnumerable<Control> Controls(Panel panel) => panel.Children.SelectMany(child => child switch
    {
        Control control => [control],
        Panel inner => Controls(inner),
        _ => Enumerable.Empty<Control>(),
    });

    Action Remove(Action remove, Action rebuild) => () =>
    {
        remove();
        // What it hid is not in the shown results; it shows up again only after a new search.
        if (_duplicateSkipped is not null) _exclusionRemoved = true;
        SaveExclusions();
        rebuild();
    };

    StackPanel FolderSection(Action rebuild)
    {
        var box = new TextBox { Header = Strings.Duplicates.FolderHeader, PlaceholderText = Strings.Duplicates.FolderPlaceholder, MinWidth = 380 };
        var error = ErrorText();
        var browse = new Button { Content = Strings.Duplicates.BrowseButton, VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += async (_, _) =>
        {
            var picked = await new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id).PickSingleFolderAsync();
            if (picked is not null) box.Text = picked.Path;
        };
        void Add()
        {
            if (DuplicateExclusions.NormalizeFolder(box.Text) is not { } folder)
            {
                ShowError(error, Strings.Duplicates.FolderInvalid);
                return;
            }
            _exclusions.AddFolder(folder);
            SaveExclusions();
            rebuild();
        }
        var add = new Button { Content = Strings.Duplicates.AddButton, VerticalAlignment = VerticalAlignment.Bottom };
        add.Click += (_, _) => Add();
        OnEnter(box, Add);
        var adder = new StackPanel { Spacing = 4 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(box);
        row.Children.Add(browse);
        row.Children.Add(add);
        adder.Children.Add(row);
        adder.Children.Add(error);
        return Section(Strings.Duplicates.FoldersTitle, Strings.Duplicates.FoldersHint,
            _exclusions.Folders.Select(f => (f, Remove(() => _exclusions.Folders.Remove(f), rebuild))), Strings.Duplicates.NoFolders, adder);
    }

    StackPanel ExtensionSection(Action rebuild)
    {
        var box = new TextBox { Header = Strings.Duplicates.ExtensionsHeader, PlaceholderText = "mp4, ifc, iso…", MinWidth = 380 };
        var error = ErrorText();
        void Add()
        {
            var parts = box.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
            // All or nothing: if any part is invalid, none is added and the text stays in the box to fix.
            var rejected = parts.Where(p => DuplicateExclusions.NormalizeExtension(p) is null).Select(p => string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.Quoted, p)).ToList();
            if (rejected.Count > 0)
            {
                ShowError(error, string.Format(CultureInfo.CurrentCulture, 
                    rejected.Count == 1 ? Strings.Duplicates.ExtensionInvalidOne : Strings.Duplicates.ExtensionInvalidMany, string.Join(", ", rejected)));
                return;
            }
            foreach (var extension in parts.Select(DuplicateExclusions.NormalizeExtension).OfType<string>()) _exclusions.AddExtension(extension);
            SaveExclusions();
            rebuild();
        }
        var add = new Button { Content = Strings.Duplicates.AddButton, VerticalAlignment = VerticalAlignment.Bottom };
        add.Click += (_, _) => Add();
        OnEnter(box, Add);
        var adder = new StackPanel { Spacing = 4 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(box);
        row.Children.Add(add);
        adder.Children.Add(row);
        adder.Children.Add(error);
        return Section(Strings.Duplicates.TypesTitle, Strings.Duplicates.TypesHint,
            _exclusions.Extensions.Select(x => (x, Remove(() => _exclusions.Extensions.Remove(x), rebuild))), Strings.Duplicates.NoTypes, adder);
    }

    static void OnEnter(TextBox box, Action action) => box.KeyDown += (_, e) =>
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        action();
    };

    TextBlock ErrorText()
    {
        var error = new TextBlock { Style = (Style)Root.Resources["ErrorText"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        return error;
    }

    // Screen readers announce the error only after a LiveRegionChanged event.
    static void ShowError(TextBlock error, string message)
    {
        error.Text = message;
        error.Visibility = Visibility.Visible;
        (FrameworkElementAutomationPeer.FromElement(error) ?? FrameworkElementAutomationPeer.CreatePeerForElement(error))
            ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // Title, hint, one row per item with its Remove button (or empty when there are none) and, if given, the add row.
    static StackPanel Section(string title, string hint, IEnumerable<(string Text, Action Remove)> items, string empty, UIElement? adder)
    {
        var section = new StackPanel { Spacing = 6 };
        section.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        section.Children.Add(new TextBlock { Text = hint, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var list = items.ToList();
        if (list.Count == 0) section.Children.Add(new TextBlock { Text = empty, Opacity = 0.6, FontStyle = Windows.UI.Text.FontStyle.Italic });
        foreach (var (text, remove) in list)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var quit = new Button { Content = Strings.Duplicates.RemoveButton };
            // Every button has the same text; the accessible name says what it removes.
            AutomationProperties.SetName(quit, string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.RemoveName, text));
            quit.Click += (_, _) => remove();
            Grid.SetColumn(quit, 1);
            row.Children.Add(quit);
            section.Children.Add(row);
        }
        if (adder is not null) section.Children.Add(adder);
        return section;
    }
}
