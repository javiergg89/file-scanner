using System.ComponentModel;
using FileScanner.Core;
using Microsoft.UI.Xaml;

namespace FileScanner;

// Breakdown row. IsChecked is recomputed from the recommendation after each change (null = partly checked).
public sealed partial class EntryItem(Entry entry) : INotifyPropertyChanged
{
    public Entry Entry { get; } = entry;
    public string Glyph => Entry.IsDirectory ? "" : "";
    public string SizeText => DirNode.Format(Entry.Size);
    public Visibility FolderVisibility => Entry.IsDirectory ? Visibility.Visible : Visibility.Collapsed;
    public bool? IsChecked { get; private set; }

    // Always notifies: a click changes the CheckBox value, and it has to return to the model value even if that did not change.
    public void Update(Coverage coverage)
    {
        IsChecked = coverage switch
        {
            Coverage.Full => true,
            Coverage.Partial => null,
            _ => false,
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
    }

    // Narrator names the ListView row with ToString(); without this it reads the class name.
    public override string ToString() => $"{Entry.Name}, {SizeText}";

    public event PropertyChangedEventHandler? PropertyChanged;
}
