# Writes the winget manifest for one release. The release workflow attaches the result to the
# GitHub release, from where the package source picks it up (see README.md in this folder).
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $InstallerPath,
    [Parameter(Mandatory)] [string] $OutFile
)

$ErrorActionPreference = 'Stop'

$installer = Get-Item $InstallerPath
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash

$manifest = @"
PackageIdentifier: xelossan.EdgeLink
PackageVersion: $Version
PackageLocale: en-US
Publisher: xelossan
PackageName: EdgeLink
PackageUrl: https://github.com/xelossan/EdgeLink
License: Not specified
ShortDescription: Custom cursor transitions between monitors on Windows 11.
Moniker: edgelink
Installers:
  - Architecture: x64
    InstallerType: portable
    InstallerUrl: https://github.com/xelossan/EdgeLink/releases/download/v$Version/$($installer.Name)
    InstallerSha256: $hash
    Commands:
      - EdgeLink
ManifestType: singleton
ManifestVersion: 1.6.0
"@

# winget expects LF or CRLF consistently and UTF-8 without a byte order mark
[IO.File]::WriteAllText($OutFile, ($manifest -replace "`r", '') + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $OutFile for $Version ($hash)"
