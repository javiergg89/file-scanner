using System.ComponentModel;
using System.Globalization;
using FileScanner.Core;

namespace FileScanner;

// Group of copies in the Duplicates tab. Files go from oldest to newest.
public sealed class DuplicateGroupItem
{
    public DuplicateGroupItem(DuplicateGroup group, Func<string, string> display)
    {
        Group = group;
        Files = [.. group.Files.Select(f => new DuplicateFileItem(f, this, display(f.Path)))];
    }

    public DuplicateGroup Group { get; }
    public List<DuplicateFileItem> Files { get; }
    public string Title => Files.Count == 1
        ? string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.GroupTitleOne, Path.GetFileName(Group.Files[0].Path), DirNode.Format(Group.Size))
        : string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.GroupTitleMany, Path.GetFileName(Group.Files[0].Path), Files.Count, DirNode.Format(Group.Size));
    public string WastedText => string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.Recoverable, DirNode.Format(Group.Wasted));

    // Narrator names the ListView row with ToString(); without this it reads the class name.
    public override string ToString() => Title;
}

// IsChecked: true if checked in this tab; null (indeterminate) if another recommendation deletes it,
// e.g. all of Downloads, and CoveredBy names it; false if it is not deleted.
public sealed partial class DuplicateFileItem(DuplicateFile file, DuplicateGroupItem group, string displayPath) : INotifyPropertyChanged
{
    public DuplicateFile File { get; } = file;
    public DuplicateGroupItem Group { get; } = group;
    public string DisplayPath { get; } = displayPath;
    public string DateText => File.LastWriteUtc.ToLocalTime().ToString(Strings.Duplicates.DateFormat, CultureInfo.CurrentCulture);
    public bool? IsChecked { get; private set; }
    public string? CoveredBy { get; private set; }
    public string CoveredText => CoveredBy is null ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.DeletedBy, CoveredBy);

    // Always notifies: a click changes the CheckBox value, and it has to return to the model value even if that did not change.
    public void Update(bool inSelection, string? coveredBy)
    {
        CoveredBy = coveredBy;
        IsChecked = inSelection ? true : coveredBy is null ? false : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoveredText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
