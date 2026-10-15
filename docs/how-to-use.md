English · [Español](how-to-use-es_ES.md)

# How to use File Scanner

## Choose what to scan

The app scans the system drive (usually C:) when it opens. To change that:

1. Click the button in the title bar that shows what is scanned (*C:* at first).
2. Check *Home folder*, *Program Files*, *Program Files (x86)*, *ProgramData* or a whole drive (*Entire drive C: · 120 GB used*).
3. Click *Scan*.

When the scanned folders are on more than one drive, the chart starts at *This PC*, with one branch per drive.

To scan any other folder, including a network share, use one of these:

- *Choose folder…* in the same menu.
- Drag the folder onto the window. A dropped file shows *Only folders can be scanned. Drop a folder.*
- Right-click the folder or a drive in File Explorer and choose *Scan with File Scanner*. The installer adds this item, in the language chosen during setup.
- Run `FileScanner.exe "D:\Photos"` from a terminal.

The app then scans only that folder. If the folder does not exist, it scans the system drive instead. The folder stays in the scope menu until you close the app, so you can combine it with the other choices and click *Scan* again.

## Read the top figures

The row under the title bar has four figures.

| Figure | Meaning |
|---|---|
| *Free after cleanup* | Free space on the drive of your home folder plus the checked items on that drive. Checked items on other drives show as "+2 GB on D:". Items moved to the Recycle Bin use space until you empty it. |
| *Safe to delete* | Total of the *Safe to delete* recommendations. |
| *Review first and program leftovers* | Total of the *Review first* and *Possible program leftovers* recommendations. |
| *Not scanned* | Space used on the drive outside the scanned folders (system and programs). |

## Browse folders

The left panel has six tabs: *Rings*, *Map*, *List*, *Types*, *Largest* and *Duplicates*.

In *Rings* and *Map*, click a segment or tile to open that folder, or click *Files* to choose files to delete in the right panel. To go up, click the center of the rings, press Backspace or use the mouse back button. The info button next to *Disk usage* shows these steps. Hover a segment or tile and its path replaces the path bar at once, with its size and number of files in the box under it on the left. The box on the right shows the totals of the open folder.

Right-click a segment or tile for more options: *Open* (only for folders with subfolders), *Open in File Explorer*, *Copy path*, *Export to CSV…*, *Choose what to delete here…*, *Add to "Chosen by you"* or *Remove from "Chosen by you"*, *Rescan this folder* and *Delete…*. On *Files* the menu has *Choose files to delete…* instead of *Choose what to delete here…*. Protected folders (see [Delete](#delete)) have no *Delete…* or *Chosen by you* items.

*List* shows the same folder as a table: one row per subfolder, plus *Files* for its loose files, with size, share of the folder, number of files and date modified, largest first. Click a column header to sort by it, and again to reverse the order. Click a row or press Enter or Space to open that folder. The *Files* row opens its files in the right panel. Right-click a row for the same menu as the chart.

### Color by age

The *Chart colors* list under the chart switches between *Color by category* (the default) and *Color by age*. By age, each folder takes the color of its most recent change anywhere inside it:

- Blue: less than 1 month.
- Purple: 1 to 6 months.
- Yellow: 6 to 12 months.
- Orange: 1 to 3 years.
- Red: more than 3 years.

In this mode the box on the left also shows that date. Folders checked for deletion are green in both modes.

The legend under the chart lists only the categories or ages on screen. Click one to highlight it in the chart and fade the rest; click it again to show all.

### Search for a folder

1. Click the search box to the right of the tabs, or press Ctrl+F.
2. Type part of a folder name. The box lists the 20 largest folders in the whole scan whose name contains the text, with location and size.
3. Pick one, or press Enter for the first, to open it in the chart.

With `*` or `?` the text must match the whole name: `node_*` finds `node_modules` but not `my_node_app`.

## Space by file type

*Types* lists the space used by each extension (`.mp4`, `.iso`, `.psd`…) in the folder shown in the path bar and its subfolders, largest first, with its share of the folder and its number of files. The list follows the folder you open.

## Largest files

*Largest* lists the 100 largest files in the folder shown in the path bar and its subfolders, with folder, date and size.

Check a file to add it to *Chosen by you*. Right-click it to *Open* it, *Show in File Explorer*, *Copy path*, *Show folder in chart*, *Add to "Chosen by you"* or *Remove from "Chosen by you"*, or *Delete…*. A file that another recommendation already deletes shows a partial check and the note *Will be deleted by "…"*.

## Recommendations

The right panel lists recommendations in groups:

- *Safe to delete* has caches that their tool downloads again by itself, temporary files, crash dumps, browser and app caches, GPU shader caches, the thumbnail cache and Windows Update downloads.
- *Review first* has data that takes time to recover: the pnpm store, NuGet packages, Maven, Cargo and Rust toolchains, the Android SDK, Ollama and Hugging Face models, Unreal and Unity caches, the Visual Studio Installer cache, Downloads, *Previous Windows installation* (`Windows.old`), and `node_modules` or build output (`target`, `obj`) in projects with no changes in 30 days.
- *Possible program leftovers* has folders that no installed program uses (see below).
- *Chosen by you* has folders and files you add yourself.
- *Has grown* has folders that grew since the saved scan (see below).

A group appears only when it has something. Nothing is checked when the app opens. Some titles say what to do first, for example close the browser, or that the item needs administrator rights.

Right-click a recommendation for *View contents*, *Show in chart*, *Open in File Explorer*, *Copy paths*, *Check all*, *Uncheck all* and *Delete checked items…*.

### Check what to delete

1. Check a whole recommendation. *Check safe items* checks all of *Safe to delete* and unchecks the rest.
2. To choose inside a recommendation, click the arrow button at the end of its row or double-click the row. The panel shows its contents.
3. Check single subfolders and files. A folder with something unchecked inside shows a partial check.
4. In this view the buttons become *Check all*, *Copy script (all)* and *Delete all checked…*. *Back to recommendations* (the arrow at the top) returns to the list.

Most recommendations empty a folder and keep it. In this view, right-click that folder and choose *Delete contents…* to empty it on its own.

The text above the buttons shows the checked total, for example "2.4 GB checked · 3 groups". *Uncheck all* clears everything.

To add a folder from the chart, right-click it and choose *Add to "Chosen by you"*. *Remove from "Chosen by you"* takes it out.

### Possible program leftovers

The app looks in Program Files, Program Files (x86) and ProgramData when you scan them, and always in AppData\Local, AppData\Local\Programs and AppData\Roaming. A folder there appears in this group when it has not changed for 60 days, holds at least 20 MB, and no installed program has its files there or matches its name or publisher.

Microsoft Store apps, portable programs and saved games can match these rules too. Open each folder before deleting it.

## Delete

1. Check the items, then click *Delete…*.
2. The dialog shows up to 5 paths, the number of items and the total size.
3. Choose *Move to Recycle Bin* (the default), *Delete permanently* or *Cancel*.

Deletes run one after another in a queue, and the app stays usable while they run. The queue button in the title bar shows progress; click it to see each job. *Clear finished* removes finished jobs. The Windows progress dialog shows the speed of each delete and lets you cancel it. A canceled job shows how many items were not deleted.

When some items could not be deleted (in use or access denied), the app says how many and shows the first one.

The app never deletes your profile, AppData and its Local, LocalLow and Roaming folders, Desktop, Documents, Pictures, Videos, Music, Downloads, Windows, Program Files, Program Files (x86), ProgramData or a drive root, nor a folder that contains one of them. It can delete items inside them.

Deleting inside Program Files, ProgramData or Windows needs administrator rights. The app starts without them, so when such a delete fails it offers *Restart as administrator*: Windows asks for consent and the app opens again, elevated, on the same folder. Then delete again. The settings menu has the same item. Running as administrator, the title says so, and dragging folders from File Explorer onto the window does not work (Windows blocks it). If something still cannot be deleted, the app offers *Copy command*; run that command in PowerShell as administrator.

### Copy a script instead

*Copy script* puts a PowerShell script for the checked items on the clipboard. Read it, then run it yourself. The script does not follow junctions or symbolic links, so it does not delete their targets.

### Empty the Recycle Bin

The *Recycle Bin* button in the title bar shows its size. Click it and confirm with *Empty*.

## Find duplicates

1. Open the *Duplicates* tab.
2. Pick a minimum size: *1 MB or more*, *10 MB or more* (the default) or *100 MB or more*.
3. Click *Find duplicates*. *Cancel* stops the search.

The search compares in steps: same size, then the first and last 64 KB, then a full SHA-256 for what still matches. Hard links count once because they take no extra space. Cloud-only OneDrive files are skipped so they are not downloaded.

It skips folders where copies are expected: AppData, Program Files, Program Files (x86), ProgramData, folders whose name starts with a dot (`.git`), and folders named `node_modules`, `bower_components`, `vendor`, `packages`, `bin`, `obj`, `dist`, `build`, `out`, `target`, `venv`, `env`, `__pycache__`, `site-packages`, `Pods`, `DerivedData`, `Windows`, `$Recycle.Bin` or `System Volume Information`.

The tab has its own check marks. *Check extra copies* checks every copy except the oldest in each group. *Uncheck all*, *Copy script* and *Delete…* work as in the recommendations.

The app refuses to check or delete the last copy of a group, counting what the recommendations will delete too.

### Exclusions

Click *Not a duplicate* on a group to hide it. Right-click a copy to exclude its folder or its extension, or to mark the group as *Not a duplicate*. Click *Exclusions* to see and edit the lists; the button shows how many there are, for example *Exclusions (3)*.

| Section | What it holds |
|---|---|
| *Excluded folders* | Full paths. Type one or use *Browse…*, then *Add*. Their subfolders are skipped too. |
| *Excluded file types* | Extensions separated by commas, only the last part (`gz`, not `tar.gz`). |
| *Groups that aren't duplicates* | Groups recognized by content, so they stay hidden even if the copies move. |

Exclusions apply at once to the results on screen. Search again to see files you excluded before.

## Export to CSV

1. Right-click a segment, tile or row.
2. Choose *Export to CSV…* and pick where to save.

The file has that folder and its subfolders up to three levels deep, largest first, with path, size in bytes, number of files and date modified. It is UTF-8 with a BOM, so Excel shows accents correctly. The separator is the list separator of your Windows region settings ("," for US, ";" for Spanish formats). Column headers follow the app language. Loose files have no row; they count in their folder's size.

## Live updates and rescans

The app watches the scanned folders. Changes made outside it, for example deleting a folder in File Explorer, appear within 2 seconds without a rescan. If too many changes happen at once, or a folder can't be watched, a notice asks you to rescan.

To read one folder again, right-click it and choose *Rescan this folder*. *Cancel* stops a scan that is running.

## Saved scans and growth

Each scan is saved, compressed, in `%LOCALAPPDATA%\FileScanner`. When you open the app with the same folders, it shows the saved scan at once and rescans in the background. Your check marks and the open folder stay.

After that rescan the app compares both scans:

- Folders that grew by 100 MB or more appear in *Has grown*, up to 10, largest growth first. A folder that grew only because of a subfolder already listed is left out.
- Hovering a segment or tile shows its growth in the box on the left, for example "+2.3 GB since the scan 3 days ago".

Click a row in *Has grown*, or press Enter or Space, to open that folder in the chart. Right-click it for the chart's menu.

Growth is measured against the saved scan loaded at startup. Rescans keep that baseline. Scanning a different set of folders starts a new baseline. Each set of folders has its own saved scan; saved scans unused for 30 days are deleted.

## Settings

The gear button in the title bar opens the settings. *Appearance* is *Use system setting* (the default), *Light* or *Dark*, and applies at once. *Language* is *English* (the default) or *Español*, and applies after a restart; the app offers *Restart now*.

Settings are saved in `%LOCALAPPDATA%\FileScanner\settings.json`. Delete that file to go back to the defaults.

## Keyboard and mouse

| Key | Action |
|---|---|
| Ctrl+F | Search folders |
| Backspace or mouse back button | Up one level in the chart or in the open recommendation |
| Tab, then arrow keys | Move to the chart, then between first-level segments or tiles |
| Shift+F10 or Menu key | Open the context menu |
| Enter or Space | Open the focused segment or tile, or the selected row in *List* or *Has grown* |

## Files the app stores

All in `%LOCALAPPDATA%\FileScanner`:

- The saved scans.
- `settings.json`.
- `duplicates-exclusions.json`.
- `crash.log`, written only if the app closes because of an error.

Uninstalling deletes this folder. After an install for all users, it stays in each user's profile. The app itself installs to `%LOCALAPPDATA%\Programs\File Scanner` (Program Files for an install for all users).

## Limits

- The scan does not keep file names. *Types* and *Largest* read the folders again, and the search finds only folders.
- *Rescan* reads all scanned folders again, including the ones that did not change.
- In demo mode (`--demo`) the app scans nothing and does not change or open anything on disk. Delete, export, open, copying to the clipboard, emptying the Recycle Bin, rescanning a folder and saving settings are turned off. Duplicate exclusions apply but are not saved.
