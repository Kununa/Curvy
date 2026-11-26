<#
.SYNOPSIS
  Builds a Curvy installer with Velopack.

.EXAMPLE
  ./tools/installer/pack.ps1 -Version 0.1.0

  Produces artifacts/releases/:
    Curvy.App-win-Setup.exe     the installer to hand out (per-user, no admin, no prompts)
    Curvy.App-win-Portable.zip  portable build
    *.nupkg + releases.win.json update packages, for a future update feed

.NOTES
  vpk comes from the local tool manifest (dotnet-tools.json) and must match the Velopack package
  version in Curvy.App.csproj. The app installs to %LocalAppData%\Curvy.App; profiles and settings
  stay in %LocalAppData%\Curvy and survive uninstalling.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [ValidateSet('win-x64', 'win-arm64')] [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$publish = Join-Path $root "artifacts\publish\$Runtime"
$releases = Join-Path $root 'artifacts\releases'
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'dotnet tool restore failed' }

    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish src/Curvy.App/Curvy.App.csproj -c Release -r $Runtime --self-contained `
        -p:Platform=$platform -p:Version=$Version -o $publish
    if ($LASTEXITCODE) { throw 'dotnet publish failed' }

    # Pack id Curvy.App = install folder %LocalAppData%\Curvy.App (never "Curvy": the uninstaller
    # deletes the whole install folder, and %LocalAppData%\Curvy holds the user's profiles).
    # Start menu shortcut only: Curvy lives in the tray, a desktop icon would be clutter.
    dotnet vpk pack `
        --packId Curvy.App `
        --packVersion $Version `
        --packDir $publish `
        --mainExe Curvy.App.exe `
        --runtime $Runtime `
        --packTitle Curvy `
        --icon src/Curvy.App/Assets/curvy.ico `
        --shortcuts StartMenuRoot `
        --outputDir $releases
    if ($LASTEXITCODE) { throw 'vpk pack failed' }

    Write-Host "Installer: $(Join-Path $releases 'Curvy.App-win-Setup.exe')"
}
finally {
    Pop-Location
}
