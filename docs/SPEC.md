# Curvy: specification

What Curvy is meant to be, the decisions behind it, and how it is built. For using the app, see the
[README](../README.md).

## Purpose

Curvy is a Windows GUI for [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) focused on
[AutoEQ](https://github.com/jaakkopasanen/AutoEq) headphone corrections. Peace can do a lot but is
overloaded; Curvy covers three things and does them well:

1. **Find AutoEQ corrections** for a headphone without leaving the app.
2. **Switch profiles easily**, including from the system tray.
3. **Apply personal preference on top of AutoEQ**, per headphone and universally.

Simplified compared to Peace, but **not dumbed down**: the real filters are visible and editable, and
the app shows exactly what it sends to Equalizer APO.

## Requirements

- **Windows only**, because Equalizer APO is Windows only.
- **Output only**: nothing microphone or capture related.
- **Profiles**: a profile starts from an AutoEQ correction or from scratch, and is saved as one unit
  together with its own preference.
- **Two preference levels**:
  - *Profile preference*: saved in the profile, with its AutoEQ correction. Off by default.
  - *Universal preference*: separate from all profiles, applied on top of the active one. A profile
    can ignore it.
- **Preference modes**, mutually exclusive (tabs):
  - *Simple*: bass, treble and tilt only, kept easy (no filter details).
  - *Parametric*: peak, low-shelf and high-shelf bands (frequency, gain, Q), with sensible default
    bands, draggable on the graph. Each band's frequency can be locked so dragging only changes its
    gain. "Copy AutoEQ frequencies" creates flat bands at the active correction's frequencies.
- **Graph**: one graph with each layer (AutoEQ, profile preference, universal, total), toggles per
  layer for the graph only.
- **Preamp**: Auto (no clipping), −6 dB, −12 dB or custom, with a clipping warning.
- **Tray**: switch profiles, global EQ on/off, open the window; the app keeps running in the tray
  when the window is closed. Optional start with Windows, straight to the tray.
- **Global on/off switch** in the window and the tray.
- **AutoEQ search** on the profile page; results show measurement source and rig (rig data from the
  AutoEQ GitHub repo).
- **Never destroy the user's Equalizer APO setup**: back up first, disable instead of delete,
  restorable from Settings and on uninstall.
- **No admin rights** in normal use.

### Not now

- Linking profiles to output devices or switching on device change.
- Global hotkeys (planned later; profile switching is callable from anywhere so they are easy to add).
- Peace feature parity: speaker routing, per-channel EQ, graphic-EQ banks, VST hosting, `.peace` import.
- Cross-platform support.

### Anti-goals

- A cluttered, Peace-like wall of controls.
- Hiding what is actually applied.
- Clipping because the preamp was wrong.
- Changes to the Equalizer APO setup that can't be undone.

## Tech stack

- C# on **.NET 11**.
- **WinUI 3** through [Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor)
  (preview), created from the Reactor templates. UI in pure C#, no XAML.
- **Unpackaged** app (tray app that starts with Windows).
- All non-UI logic in a UI-free class library (`Curvy.Core`), so the UI could be rebuilt if
  Reactor's API changes.
- Own WinUI graph control (no charting library), to keep the app small.
- **Native AOT** release builds (trimmed, source-generated JSON); the tests run as Native AOT too.
- Installer and updates with [Velopack](https://velopack.io), published to GitHub Releases by a
  GitHub Actions workflow. Updates are read anonymously from the public repo: no tokens in the app.

## How it works with Equalizer APO

Equalizer APO's install path comes from the registry (`HKLM\SOFTWARE\EqualizerAPO`, `InstallPath`,
falling back to `ConfigPath`). Its `config` folder is writable by users, so Curvy runs unelevated;
if it isn't writable Curvy says so instead of elevating. Equalizer APO reloads its config files
itself, so Curvy only ever writes files.

- Curvy writes its whole EQ to `<Equalizer APO>\config\Curvy\curvy.txt` and adds a single
  `Include: Curvy\curvy.txt` line to `config.txt`.
- Before its first change it saves `config.txt.curvy-backup`.
- On first run it lists the other enabled lines in `config.txt` (for example from Peace) and offers
  to turn them off, so effects don't stack. Turned-off lines are commented out with `# [Curvy] `,
  never deleted, and can be turned back on under Settings. Other tools' lines are only changed on a
  user click.
- If another app removes Curvy's include, Curvy puts it back (and stops if that keeps happening).
- **Settings → Restore original config.txt** puts the backup back and disconnects Curvy until the
  user reconnects.
- With no active profile only the universal preference is applied.
- The graph and the auto preamp are computed at 48 kHz.

## AutoEQ

- The AutoEQ index is cached in the data folder, so search works offline; it can be refreshed from
  Settings. GitHub rate limits are handled gracefully.
- A correction's filters are copied into the profile (offline, stable), with its source, rig and
  fetch date kept.

## Data and uninstall

- Profiles, settings and the AutoEQ cache: `%LocalAppData%\Curvy` (versioned JSON formats).
- The app: `%LocalAppData%\Curvy.App` (per-user install, no admin).
- Uninstalling removes the app, the "start with Windows" entry and `curvy.txt`, and restores
  `config.txt` from the backup. The data folder is kept.

## Updates

Installed copies check GitHub Releases once per start (and on demand under Settings → About). A
found update downloads in the background and is offered as "Restart to update" in the window, the
tray menu and Settings; nothing restarts without a click. Pre-releases are ignored. Dev builds
don't update.

## Building from source

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

### Project layout

| Path | What |
|---|---|
| `src/Curvy.Core` | Engine: filters and response math, Equalizer APO config handling, profiles and settings storage, AutoEQ client |
| `src/Curvy.App` | WinUI 3 app (Reactor, UI in C#), tray, Velopack installer hooks and updates |
| `src/Curvy.App/Assets/Icon` | Icon sources (SVG); `tools/icons/build_icons.py` renders the `.ico` files |
| `tests/Curvy.Core.Tests` | TUnit tests for the engine |

### Releasing

Push a version tag; the [Release workflow](../.github/workflows/release.yml) runs the tests, builds the
Velopack installer and publishes a GitHub release, which installed copies pick up as an update:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

A suffix (`v0.2.0-beta.1`) makes it a pre-release, which installed copies ignore. Versions must
increase.

The original design and implementation plan are in [`docs/superpowers`](superpowers).
