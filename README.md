English · [Español](README-es_ES.md)

# File Scanner

A Windows app that shows what takes up space in your home folder and deletes the caches, temporary files, crash dumps and old `node_modules` folders you check.

Step-by-step guide: [How to use File Scanner](docs/how-to-use.md).

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/rings.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/rings-light.png">
  <img src="docs/rings-light.png" alt="File Scanner showing disk usage as rings, with cleanup recommendations">
</picture>

On a Core i9-13900K with an NVMe SSD (Samsung 980 PRO) it scans 2 million files (365 GB) in about 5 seconds. Compressed, sparse and cloud-only files count at their size on disk, not their declared size.

## Features

- Rings, map and list views, colored by category or by age, with folder search (Ctrl+F).
- Cleanup recommendations: *Safe to delete*, *Review first* and *Possible program leftovers*.
- Delete to the Recycle Bin or permanently, or copy a PowerShell script to run yourself.
- Space by file type, the 100 largest files and duplicate files.
- English or Spanish, light or dark theme.

## Install

Download `FileScanner-Setup-x.y.z.exe` from [Releases](https://github.com/javiergg89/file-scanner/releases/latest) and run it. By default it installs for the current user, without administrator rights. Needs Windows 10 version 2004 or later, x64. It includes the .NET and Windows App SDK runtimes; nothing else needs to be installed.

Portable alternative: `FileScanner-x.y.z-portable.exe` is the whole app in one file, with no installer and no folder. Put it anywhere and run it. It has no Start menu entry or Explorer context menu, and it starts a little slower the first time (it unpacks itself to a temporary folder).

The installer is not code-signed, so SmartScreen may block the first run. Choose *More info*, then *Run anyway*. To check that this repository's release workflow built it, run `gh attestation verify FileScanner-Setup-x.y.z.exe -R javiergg89/file-scanner`.

## Development

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download); [`global.json`](global.json) sets the version. Packages come only from nuget.org and are pinned by `packages.lock.json`.

```powershell
dotnet build FileScanner.slnx
dotnet run --project FileScanner.Check    # core checks
dotnet run --project FileScanner          # the app
```

`FileScanner.Check` also takes `bench`, `leftovers` and `duplicates` (run with `-c Release -- <name>`). The app takes `--demo` to show invented data for screenshots.

`FileScanner/Core` has the scanner, the chart layout and the cleanup rules, with no UI code. `install` has the Inno Setup script.

## Release

Raise `<Version>` in [`Directory.Build.props`](Directory.Build.props) and push to `main`. The [`Release` workflow](.github/workflows/release.yml) runs the checks, builds the installer, tags the commit (`vX.Y.Z`) and publishes a GitHub release. The [`CI` workflow](.github/workflows/ci.yml) runs the checks on pull requests to `main`.

To build the installer locally you also need [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
dotnet publish FileScanner\FileScanner.csproj -c Release -o publish -p:Version=X.Y.Z
install\build-installer.ps1 -Version X.Y.Z
```

The installer is written to `dist\`.

## License

The source code is under the [MIT license](LICENSE). The installer also contains the .NET runtime (MIT) and the Windows App SDK runtime under Microsoft's own terms; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Security and privacy

The app makes no network connections. It reads the folders you choose to scan and the list of installed programs in the Windows registry, and deletes only what you check.

It stores its data in `%LOCALAPPDATA%\FileScanner` ([details](docs/how-to-use.md#files-the-app-stores)).

Report vulnerabilities as described in [SECURITY.md](SECURITY.md).
