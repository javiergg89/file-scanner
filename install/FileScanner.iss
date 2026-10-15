; File Scanner installer (Inno Setup 6). The release workflow builds it:
;
;   dotnet publish FileScanner\FileScanner.csproj -c Release -o publish -p:Version=1.2.3
;   install\build-installer.ps1 -Version 1.2.3
;
; Output goes to dist\. Installs per user without admin rights; deleting in system folders
; requires running the script as administrator.

#ifndef Version
  #define Version "0.0.0"
#endif
#define PublishDir "..\publish"
#define AppExe "FileScanner.exe"
; build-installer.ps1 generates License: the app's MIT license followed by Microsoft's full text.
#ifndef License
  #error Falta /DLicense. Compila con install\build-installer.ps1.
#endif

[Setup]
AppId={{9D3F6B2E-4C1A-4E8B-9A7D-2F5C8E1B6A40}
AppName=File Scanner
AppVersion={#Version}
AppPublisher=javiergg89
AppCopyright=Copyright (c) 2026 Javier Grimoin González. MIT License.
AppPublisherURL=https://github.com/javiergg89/file-scanner
AppSupportURL=https://github.com/javiergg89/file-scanner/issues
VersionInfoProductName=File Scanner
VersionInfoVersion={#Version}
; The app's MIT license plus Microsoft's Windows App SDK terms, which the installing user must accept
; (section 3.b.ii of those terms).
LicenseFile={#License}
DefaultDirName={autopf}\File Scanner
DisableProgramGroupPage=yes
; Per user by default; the dialog allows an all-users install with admin rights.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=FileScanner-Setup-{#Version}
UninstallDisplayIcon={app}\{#AppExe}
; Closes the running app on update; otherwise copying files in use fails.
CloseApplications=yes
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.ScanWith=Escanear con File Scanner
en.ScanWith=Scan with File Scanner

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\File Scanner"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\File Scanner"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

; "Scan with File Scanner" in the Explorer context menu for folders and drives. HKA is HKCU for a
; per-user install and HKLM for an all-users install. The app gets the path as its first argument.
[Registry]
Root: HKA; Subkey: "Software\Classes\Directory\shell\FileScanner"; ValueType: string; ValueName: ""; ValueData: "{cm:ScanWith}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\FileScanner"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"""
Root: HKA; Subkey: "Software\Classes\Directory\shell\FileScanner\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Drive\shell\FileScanner"; ValueType: string; ValueName: ""; ValueData: "{cm:ScanWith}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Drive\shell\FileScanner"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"""
Root: HKA; Subkey: "Software\Classes\Drive\shell\FileScanner\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,File Scanner}"; Flags: nowait postinstall skipifsilent

; The scan cache (the path list of the user folder), crash.log and the exclusions.
; In an all-users install, {localappdata} would be the profile of the admin who uninstalls.
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\FileScanner"; Check: not IsAdminInstallMode
