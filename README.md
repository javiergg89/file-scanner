# File Scanner

Shows where the space in your Windows home folder goes and helps you delete what you no longer need: package caches (npm, pnpm, NuGet, pip, Gradle), temporary files, crash dumps and `node_modules` folders in projects you have not touched for 30 days.

It is a WinUI 3 app with an acrylic backdrop, in English or Spanish. On the author's PC it scans 2 million files (365 GB) in about 5 seconds. Sizes are the space actually used on disk, so sparse files are not over-counted: one browser cache folder there declares 524 GB and uses 14 GB.

## Using it

The left panel shows the current folder as rings (default) or as a map. Click a segment or tile to open that folder; click the centre of the rings, press Backspace or use the mouse back button to go up.

The list under the rings and the map switches the colours between *Color by category* (the default) and *Color by age*. By age, each folder takes the colour of its most recent change anywhere inside it: less than 1 month, 1 to 6 months, 6 to 12 months, 1 to 3 years or more than 3 years, and the tooltip shows that date. A large red folder is data nobody has touched for years. Folders checked for deletion stay green in both modes.

The *List* tab shows the same folder as a table: one row per subfolder (plus *Files* for its loose files) with its size, a bar with its share of the folder, its number of files and its last change, largest first. Click a column header to sort by it; click it again to reverse the order. Click a row or press Enter to open that folder, right-click it for the same menu as the chart. It reads only the level shown, so it opens instantly on large trees.

To find a folder by name, type in the search box at the end of the path bar (or press Ctrl+F). It lists the 20 largest folders in the whole scan whose name contains the text, with their location and size; `*` and `?` match the whole name instead (`node_*`). Pick one, or press Enter for the first, to open it in the chart. The scan does not keep file names, so only folders are found.

The right panel lists recommendations in two groups, *Safe to delete* and *Review first*. Nothing is checked when the app opens. Check a whole recommendation, or click the arrow button at the end of its row (or double-click the row) to see its contents and check single files and subfolders. A folder with something unchecked inside it shows a partial check. Folders you add with *Add to "Chosen by you"* in the right-click menu of the rings or the map appear in a group of their own, *Chosen by you*; *Remove from "Chosen by you"* takes them out again.

*Copy script* puts a PowerShell script for the checked items on the clipboard, so you can read it before running it. *Delete…* asks whether to *Move to Recycle Bin* or *Delete permanently* the checked items, then says how many items it could not delete (in use or access denied) and shows the first one. If you cancel in the Windows dialog, the delete queue marks the job as canceled.

Right-click a segment, tile, recommendation or file for more options: open in File Explorer, copy the path, choose what to delete inside a folder, rescan a folder, delete.

To keep a record of sizes, for Excel or to attach to a ticket, right-click a segment, tile or row and choose *Export to CSV…*. It saves that folder and its subfolders up to three levels deep, largest first, with path, size in bytes, number of files and last change. The file is UTF-8 with a BOM and uses the list separator of the Windows region settings (";" with Spanish formats, "," with US ones), so Excel opens it with accents and columns intact. The column headers follow the app language. Loose files have no row of their own; they count in their folder's size.

Changes made outside the app, for example deleting a folder in File Explorer, appear within two seconds without a rescan.

The scan covers your home folder. The button in the title bar adds Program Files, Program Files (x86), ProgramData or whole drives (C:, D:…). With several drives the explorer starts at *This PC*, with one branch per drive.

To scan any other folder, including a network share, choose *Choose folder…* in that menu, drag the folder onto the window, or right-click it (or a drive) in File Explorer and choose *Scan with File Scanner* (added by the installer, in the language chosen during setup). The app then scans only that folder. It stays in the menu as a checkbox, so you can combine it with your home folder or the other choices and press *Scan*. From a terminal, `FileScanner.exe "D:\Fotos"` does the same.

The *Types* tab lists the space used by each file extension in the folder shown in the path bar (`.mp4`, `.iso`, `.psd`…), largest first, with its share of the folder and its number of files. Navigate with the path bar, the up button or Backspace and the list follows. The scan does not keep file names, so the tab reads the scanned folders again in the background each time the folder changes; sizes are measured the same way as in the scan.

The *Largest* tab lists the 100 largest files in the folder shown in the path bar, subfolders included, with their folder, date and size, so a big file buried deep in the tree is easy to spot. Like *Types*, it reads the scanned folders again in the background each time the folder changes; each reader keeps only its current top 100, so memory stays flat on drives with millions of files. Check a file to add it to *Chosen by you*; right-click it to open it, show it in File Explorer, copy its path, show its folder in the chart or delete it.

The *Duplicates* tab finds files with the same content in the scanned folders. It compares in steps, cheapest first: same size, then the first and last 64 KB, then a full SHA-256 only for what still matches. Hard links (one file under several names, as pnpm uses) count once because they take no extra space. Cloud-only OneDrive files are skipped so they are not downloaded. It ignores technical folders where copies are expected: AppData, `node_modules`, `.git`, `bin`, `obj`, `dist`, dot folders and Program Files. *Exclusions* lists folders and file types the search must skip, plus groups marked *Not a duplicate*; a skipped group is recognised by its content, so it stays hidden even if the copies move. Right-clicking a copy excludes its folder or its extension. Exclusions are saved in `%LOCALAPPDATA%\FileScanner\duplicates-exclusions.json` and apply at once to the results on screen. The tab has its own check marks and its own buttons (*Check extra copies*, *Uncheck all*, *Copy script*, *Delete…*), separate from the recommendations. *Check extra copies* keeps the oldest copy of each group and checks the rest. The app refuses to check the last copy of a group that is not already checked, counting what the recommendations will delete too.

Each scan is saved, compressed, under `%LOCALAPPDATA%\FileScanner`. The next time you open the app with the same folders it shows that copy at once and rescans in the background, keeping your check marks and the folder you are in. The background rescan still reads the whole disk: skipping unchanged folders would need the NTFS change journal, which requires administrator rights.

When the app opens a saved scan, it compares it with the new one. Folders that grew by 100 MB or more appear in *Has grown* at the end of the recommendations, up to 10, largest growth first; a folder that grew only because of a subfolder already in the list is left out. Click a row, or press Enter, to open that folder in the chart; right-click it for the chart's menu. The tooltip of each segment or tile also shows its growth, for example "+2.3 GB since the scan 3 days ago". The comparison is always against the saved scan that was loaded, so rescanning the same folders keeps it; choosing other folders starts a new one. No extra history is kept on disk.

A third group, *Possible program leftovers*, lists folders in Program Files, ProgramData and AppData that no installed program uses: no entry in Windows' installed programs list has its files there or matches the folder name or publisher, the folder has not changed for 60 days and it holds at least 20 MB. Portable programs match these rules too, so check each folder before deleting it. Deleting inside Program Files or ProgramData needs administrator rights: copy the script and run it in an elevated PowerShell.

## Settings

The gear button in the title bar opens the settings menu:

- *Appearance*: *Use system setting* (the default, follows light or dark mode in Windows), *Light* or *Dark*. The change applies at once.
- *Language*: *English* (the default) or *Español*. Texts, number formats and dates change after a restart; the app offers *Restart now*.

Each change is saved in `%LOCALAPPDATA%\FileScanner\settings.json` and applies on every later start. Delete that file to go back to the defaults.

## Install

Download `FileScanner-Setup-x.y.z.exe` from [Releases](https://github.com/javiergg89/file-scanner/releases/latest) and run it. It installs for the current user under `%LOCALAPPDATA%\Programs\File Scanner` and does not need administrator rights. To update, run a newer setup. Needs Windows 10 version 2004 or later, x64.

The installer is not code-signed, so Windows SmartScreen may block the first run. Choose **More info**, then **Run anyway**. To check that the installer was built by this repository's release workflow, run `gh attestation verify FileScanner-Setup-x.y.z.exe -R javiergg89/file-scanner` with the [GitHub CLI](https://cli.github.com).

Uninstalling also deletes `%LOCALAPPDATA%\FileScanner` (the saved scan, the settings, `crash.log` and the duplicate exclusions). After an install for all users, that folder stays in each user's profile.

## Development

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download); [`global.json`](global.json) sets the version. NuGet packages are restored only from nuget.org and pinned by `packages.lock.json`; after changing a package reference, build once and commit the updated lock files.

```powershell
dotnet build FileScanner.slnx
dotnet run --project FileScanner.Check                       # core checks
dotnet run --project FileScanner.Check -c Release -- bench   # scan benchmark on your home folder
dotnet run --project FileScanner.Check -c Release -- leftovers   # possible program leftovers on this PC
dotnet run --project FileScanner.Check -c Release -- duplicates  # duplicates of 10 MB or more in your home folder
dotnet run --project FileScanner                             # the app
```

| Folder | Content |
|---|---|
| `FileScanner` | The app |
| `FileScanner/Core` | Scanner, rings and map layout, cleanup rules; no UI code |
| `FileScanner.Check` | Checks for `Core` and the scan benchmark |
| `install` | Inno Setup script, the script that builds the installer and the license text it shows |

## Release

Raise `<Version>` in [`Directory.Build.props`](Directory.Build.props) and push the commit to `main`:

```powershell
git commit -am "Version 0.2.0"
git push
```

The [`CI` workflow](.github/workflows/ci.yml) builds and runs the checks on every push to `main` and every pull request. The [`Release` workflow](.github/workflows/release.yml) runs the checks, builds the installer, tags the commit `v0.2.0` and attaches the installer to a GitHub release. A push that leaves the version unchanged publishes nothing.

To build the installer on your PC you also need [Inno Setup 6](https://jrsoftware.org/isinfo.php) (the workflow uses 6.7.1):

```powershell
dotnet publish FileScanner\FileScanner.csproj -c Release -o publish -p:Version=0.2.0
install\build-installer.ps1 -Version 0.2.0
```

The installer is written to `dist\`. The script joins the MIT license and the full Windows App SDK terms into the text of the installer's license page.

## License

The source code is under the [MIT license](LICENSE).

The installer also contains the .NET runtime (MIT) and the Windows App SDK runtime, which Microsoft licenses under its own [Microsoft Software License Terms](THIRD-PARTY-NOTICES.md). Those terms are shown during setup and copied to the `licenses` folder of the installation, together with the .NET runtime license and notices. Details in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Security and privacy

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

If the app closes because of an error, the error is written to `%LOCALAPPDATA%\FileScanner\crash.log` on your PC; nothing is sent anywhere. The app makes no network connections. It reads your home folder (and Program Files, ProgramData or whole drives if you add them), reads the list of installed programs from the Windows registry, and deletes only what you check.
