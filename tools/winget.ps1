# Writes the three winget manifest files for a release, ready to submit to github.com/microsoft/winget-pkgs.
#
#   .\tools\release.ps1 -Upload      builds, signs and uploads the zip; publish the draft release on GitHub
#   .\tools\winget.ps1               writes artifacts\winget\<version>\*.yaml from that zip
#
# Then, with winget's own tools:
#
#   winget validate artifacts\winget\0.5.0
#   winget install --manifest artifacts\winget\0.5.0      a test install from the local files
#   wingetcreate submit artifacts\winget\0.5.0            opens the pull request (winget install wingetcreate)
#
# The release must be published first: winget's checks download the zip from the release's URL and compare it
# with the SHA-256 written here. The panel is a portable program in a zip, which winget installs by unpacking the
# exe and adding the command "xamppcontrol".
param(
    [string] $Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$repository = "ClearanceClarence-Software/Modern-XAMPP-Control-Panel"
$identifier = "ClearanceClarence.ModernXamppControlPanel"

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root "XamppControl.csproj"))).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
}
$tag = "v$Version"
$zipName = "ModernXamppControlPanel-$tag-win-x64.zip"
$zip = Join-Path $root "artifacts\$zipName"
if (-not (Test-Path $zip)) { throw "No $zipName in artifacts\. Run .\tools\release.ps1 first." }

# The same file that was uploaded, so the hash matches what winget downloads.
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToUpperInvariant()
$url = "https://github.com/$repository/releases/download/$tag/$zipName"

$folder = Join-Path $root "artifacts\winget\$Version"
New-Item $folder -ItemType Directory -Force | Out-Null

$installer = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: $identifier
PackageVersion: $Version
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: XamppControl.exe
  PortableCommandAlias: xamppcontrol
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $hash
ManifestType: installer
ManifestVersion: 1.6.0
"@

$locale = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: $identifier
PackageVersion: $Version
PackageLocale: en-US
Publisher: CC Software
PublisherUrl: https://github.com/ClearanceClarence-Software
PublisherSupportUrl: https://github.com/$repository/issues
PackageName: Modern XAMPP Control Panel
PackageUrl: https://github.com/$repository
License: MIT
LicenseUrl: https://github.com/$repository/blob/main/LICENSE
ShortDescription: A replacement for the control panel that ships with XAMPP on Windows.
Description: |-
  Keeps the layout of the XAMPP Control Panel (module rows, Start/Stop, Admin, Config, Logs) and adds clean
  MySQL shutdowns, readable errors, port conflict fixes, PHP and MariaDB versions side by side, HTTPS for local
  sites, Xdebug and Mailpit. One self-contained exe; XAMPP itself is not included and is not changed unless
  you ask. Unofficial, not affiliated with Apache Friends.
Tags:
- apache
- mariadb
- mysql
- php
- xampp
- local-development
- web-server
ReleaseNotesUrl: https://github.com/$repository/releases/tag/$tag
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

$versionFile = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: $identifier
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

# winget wants UTF-8 without a byte order mark.
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $folder "$identifier.installer.yaml"), $installer + "`n", $utf8)
[System.IO.File]::WriteAllText((Join-Path $folder "$identifier.locale.en-US.yaml"), $locale + "`n", $utf8)
[System.IO.File]::WriteAllText((Join-Path $folder "$identifier.yaml"), $versionFile + "`n", $utf8)

Write-Host "Wrote the manifests to artifacts\winget\$Version"
Write-Host "  winget validate artifacts\winget\$Version"
Write-Host "  winget install --manifest artifacts\winget\$Version"
Write-Host "  wingetcreate submit artifacts\winget\$Version"
