# Builds dist\FileScanner-Setup-<version>.exe from the publish\ folder (run dotnet publish first, see README).
# The wizard's license page shows the app's MIT license and the full text of Microsoft's Windows App SDK
# terms, which the installing user must be able to read before accepting them.
param(
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$msLicense = Join-Path $root 'publish\licenses\WindowsAppSDK-license.txt'
if (-not (Test-Path $msLicense)) { throw "No existe $msLicense. Ejecuta antes dotnet publish (ver README)." }

# With BOM: Inno Setup reads a LicenseFile .txt without BOM as ANSI and would garble the "á" in González.
$license = Join-Path $root 'obj\setup-license.txt'
New-Item -ItemType Directory -Force (Split-Path $license) | Out-Null
$text = (Get-Content (Join-Path $PSScriptRoot 'license-agreement.txt') -Raw -Encoding utf8) +
    "`r`n`r`n" +
    (Get-Content $msLicense -Raw -Encoding utf8)
[IO.File]::WriteAllText($license, $text, [Text.UTF8Encoding]::new($true))

# ISCC.exe is in Program Files (x86) for an all-users install (Chocolatey) or in LOCALAPPDATA for a per-user one.
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'No se encuentra ISCC.exe de Inno Setup 6.' }

& $iscc "/DVersion=$Version" "/DLicense=$license" (Join-Path $PSScriptRoot 'FileScanner.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
