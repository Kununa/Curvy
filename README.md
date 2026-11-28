<div align="center">

<img src="src/Curvy.App/Assets/Icon/curvy.svg" alt="Curvy icon" width="128" height="128">

# Curvy

**AutoEQ headphone corrections for [Equalizer APO](https://sourceforge.net/projects/equalizerapo/), without the fiddling.**

Simpler than Peace, but not too dumbed down.

[![Latest release](https://img.shields.io/github/v/release/Kununa/Curvy?label=release)](https://github.com/Kununa/Curvy/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Kununa/Curvy/total)](https://github.com/Kununa/Curvy/releases)
[![Release build](https://img.shields.io/github/actions/workflow/status/Kununa/Curvy/release.yml?label=build)](https://github.com/Kununa/Curvy/actions/workflows/release.yml)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)

### [⬇ Download for Windows](https://github.com/Kununa/Curvy/releases/latest/download/Curvy.App-win-Setup.exe)

</div>

<p align="center">
  <img src="docs/screenshot.png" alt="Curvy showing a headphone profile: AutoEQ correction, profile preference and total response on the graph" width="820">
</p>

## Features

- 🎧 **Profiles**: one per headphone, an AutoEQ correction plus your own preference on top. Switch
  from the app or from the tray menu.
- 🔎 **AutoEQ search**: find your headphones by name on the profile page (measurement source and rig
  shown per result) and apply the correction in one click. The index is cached, so it works offline
  too.
- 🎚️ **Preferences**: per profile, plus a universal one applied on top of every profile (a profile
  can ignore it). Each is either *Simple* (bass, treble, tilt) or *Parametric* (peak and shelf
  bands, draggable on the graph, optionally with the frequency locked). "Copy AutoEQ frequencies"
  sets up flat bands at the AutoEQ filters so you only adjust gains.
- 🔇 **Preamp**: Auto (computed so nothing clips), −6 dB, −12 dB or a custom value, with a clipping
  warning.
- 🗂️ **Tray app**: global EQ on/off, profile switching, start with Windows (straight to the tray).
- 📄 **Output tab**: shows exactly what Curvy sends to Equalizer APO.
- 🔄 **Automatic updates**: new versions download in the background and install when you click
  "Restart to update".

Output only: no microphone features.

## Install

1. Install [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) and enable it for your
   output device.
2. Download [`Curvy.App-win-Setup.exe`](https://github.com/Kununa/Curvy/releases/latest/download/Curvy.App-win-Setup.exe)
   and run it. It installs per user (no admin prompt) and adds a Start menu entry.

Requires Windows 10 or 11 (x64). A portable zip is on the [Releases](https://github.com/Kununa/Curvy/releases) page too.

> [!NOTE]
> The installer isn't code-signed yet, so Windows SmartScreen may warn on first run: click
> **More info → Run anyway**.

## How it works with Equalizer APO

Curvy writes its whole EQ to `<Equalizer APO>\config\Curvy\curvy.txt` and adds a single
`Include: Curvy\curvy.txt` line to `config.txt`. Before its first change it saves
`config.txt.curvy-backup`.

- On first run it lists the other enabled lines in `config.txt` (for example from Peace) and offers
  to turn them off, so effects don't stack. Turned-off lines are commented out with `# [Curvy] `,
  never deleted, and can be turned back on under Settings.
- If another app removes Curvy's include, Curvy puts it back (and stops if that keeps happening).
- **Settings → Restore original config.txt** puts the backup back and disconnects Curvy until you
  reconnect.

## Your data

Profiles and settings live in `%LocalAppData%\Curvy` (the AutoEQ cache too). The app itself is
installed to `%LocalAppData%\Curvy.App`.

Uninstalling (Windows Settings → Apps) removes the app, the "start with Windows" entry and Curvy's
`curvy.txt`, and restores `config.txt` from the backup, so the EQ doesn't linger. Your profiles in
`%LocalAppData%\Curvy` are kept; delete that folder to remove them too.

## Building from source

<details>
<summary>Build, run and test</summary>

Needs the [.NET 11 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build Curvy.slnx
dotnet run --project src/Curvy.App          # or, with hot reload:
cd src/Curvy.App; dotnet watch run
dotnet test --project tests/Curvy.Core.Tests
```

Release builds are Native AOT, so they need the MSVC linker (Visual Studio Build Tools with
"Desktop development with C++"). The tests run that way too, as in the release workflow:

```powershell
dotnet publish tests/Curvy.Core.Tests -c Release -r win-x64 -o artifacts/tests
./artifacts/tests/Curvy.Core.Tests.exe
```

For development without touching your real setup, point Curvy at scratch folders:

```powershell
$env:CURVY_APO_CONFIG_DIR = "$env:TEMP\apo-config"   # instead of Equalizer APO's config folder
$env:CURVY_DATA_DIR       = "$env:TEMP\curvy-data"   # instead of %LocalAppData%\Curvy
```

</details>

<details>
<summary>Project layout</summary>

| Path | What |
|---|---|
| `src/Curvy.Core` | Engine: filters and response math, Equalizer APO config handling, profiles and settings storage, AutoEQ client |
| `src/Curvy.App` | WinUI 3 app built with [Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor) (UI in C#, no XAML), tray, Velopack installer and updates |
| `src/Curvy.App/Assets/Icon` | Icon sources (SVG); `tools/icons/build_icons.py` renders the `.ico` files |
| `tests/Curvy.Core.Tests` | TUnit tests for the engine |

</details>

<details>
<summary>Releasing</summary>

Push a version tag; the [Release workflow](.github/workflows/release.yml) runs the tests, builds the
[Velopack](https://velopack.io) installer and publishes a GitHub release, which installed copies
pick up as an update:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

A suffix (`v0.2.0-beta.1`) makes it a pre-release, which installed copies ignore. Versions must
increase.

</details>

## Credits

- Headphone corrections and measurement data: [AutoEQ](https://github.com/jaakkopasanen/AutoEq) by
  Jaakko Pasanen and the measurement sources it credits.
- [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) does the actual audio processing.
