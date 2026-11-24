# Curvy — Design

Date: 2026-11-24
Status: approved

## Intent and why

Curvy is a Windows desktop GUI for [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) focused on AutoEQ headphone correction.

The user currently has Peace installed. Peace can do a lot but is overloaded. Curvy should cover the three things the user actually does, and do them well:

1. **Find AutoEQ profiles** for a headphone without leaving the app.
2. **Switch profiles easily**, including from the system tray.
3. **Apply personal preference on top of AutoEQ**, both per headphone and universally.

Curvy is simplified compared to Peace but **not dumbed down**: the user can see the real filters, edit them precisely, and understand what is being sent to Equalizer APO.

## Goals

- Search the AutoEQ database by headphone name, pick a measurement source/target, and start a profile from it.
- Build and save profiles. A profile may start from an AutoEQ result or from scratch.
- Two preference levels:
  - **Profile preference**: saved inside the profile, together with its AutoEQ correction.
  - **Universal preference**: separate from all profiles, applied on top of whichever profile is active.
- Preference editing offers both **tone controls** (bass shelf, treble shelf, tilt) and **free parametric bands** (peaking, low shelf, high shelf; frequency, gain, Q).
- One graph that shows each layer and the combined result.
- Automatic preamp so the combined curve never clips.
- Tray icon with a menu to switch profiles, toggle EQ off/on, and open the main window. Runs in the tray when the window is closed.

## Non-goals (for now)

- Anything microphone or capture related. Curvy is output only.
- Linking profiles to output devices / auto-switching on device change.
- Global hotkeys (planned later; keep the "apply profile" path callable from anywhere so hotkeys are easy to add).
- Per-device configuration. Curvy applies to whatever devices Equalizer APO is installed on; device selection stays in APO's own DeviceSelector.
- Peace feature parity: no speaker routing, no per-channel EQ, no graphic-EQ preset banks, no VST hosting.
- Importing Peace `.peace` presets.
- Cross-platform support.

## Anti-goals (failure even if it "works")

- A cluttered, Peace-like wall of controls.
- Hiding what is actually applied: the user must always be able to see the real filter list and preamp.
- Clipping because the preamp was wrong.
- Destroying the user's existing Equalizer APO setup in a way that can't be undone.
- Needing admin rights or UAC prompts during normal use.

## Constraints

- **Windows only**, Equalizer APO must be installed. Its install path comes from the registry (`HKLM\SOFTWARE\EqualizerAPO`, value `InstallPath`, falling back to `ConfigPath`); on this machine it is `C:\Program Files\EqualizerAPO`.
- The APO `config` folder grants `BUILTIN\Users` full control, so Curvy runs unelevated. If the folder isn't writable, Curvy reports that clearly and doesn't try to elevate.
- Equalizer APO reloads `config.txt` (and included files) automatically when they change. Curvy only writes files and never talks to the audio engine directly.
- **Tech stack (decided by the user):**
  - C# on **.NET 11**.
  - **WinUI 3** through **Microsoft UI Reactor** (`Microsoft.UI.Reactor`, currently in public preview). The project is created from the **Reactor templates** (`winapp new -t reactor …` or `dotnet new reactor …`) so that it includes the template's agent instruction file. Pick the template variant that fits (e.g. `reactor-navview`).
  - **Unpackaged** app, since a tray app that starts with Windows is easier to ship that way.
  - Ordinary WinUI 3 controls and WinUI 3 charting/graph components may be used inside Reactor.
- Reactor is a preview, and its API may change. To limit the damage, all non-UI logic lives in a **separate UI-free class library** (see Architecture) so the UI could be rebuilt without touching the core.

- **Audience:** for now only the user's own machine, but it may be released publicly later. So build it **release-ready** without doing release work now:
  - no hard-coded user paths;
  - AutoEQ attribution and license note in an About section;
  - graceful handling of GitHub rate limits;
  - versioned settings and profile formats.

  Deferred until a release is decided: installer, code signing, self-contained distribution, and the update mechanism. During development an unpackaged build in a folder, plus the start-with-Windows Run key, is enough.

## Approaches considered

| Approach | Trade-off | Outcome |
|---|---|---|
| C# / .NET + WPF + WPF-UI Fluent theme | Mature, easy tray, but WPF look rather than true WinUI | Not chosen |
| C# / .NET + WinUI 3 via Reactor | Native Windows 11 UI, declarative React-style state, built-in tray hooks; preview risk | **Chosen by user** |
| Tauri (Rust + web UI) | Full freedom over styling, tiny binary; two languages, Rust not installed, less native feel | Not chosen |
| Python + PySide6 | Fast to prototype; awkward packaging, non-native look | Not chosen |
| Electron | Too heavy for an app that lives in the tray | Dropped |

## How sound is built

```
Output = AutoEQ correction        (from the profile, optional)
       + Profile preference        (from the profile: tone controls + bands)
       + Universal preference      (global: tone controls + bands)
       + Preamp                    (auto, negative, computed from the total)
```

- Each layer has an on/off toggle (for A/B comparison). Toggles for AutoEQ and profile preference are stored on the profile. The universal toggle is global.
- A profile has an **"ignore universal preference"** flag (default off). This is for headphones where the universal taste doesn't suit, e.g. an already bright headphone. (Proposed by Claude, accepted.)
- **Master EQ off** bypasses everything, which is what the tray "EQ off" toggle does.

### Tone controls (both preference levels)

- **Bass**: low shelf. Gain in dB; a fixed default corner frequency (e.g. 105 Hz, Q 0.7), adjustable in an expandable "details" area.
- **Treble**: high shelf. Gain in dB; default corner e.g. 10 kHz, Q 0.7, adjustable in details.
- **Tilt**: a gentle tilt around a pivot (e.g. 1 kHz), in dB/octave or as ±dB at the extremes. The builder picks the implementation (e.g. a pair of opposing low-Q shelves) as long as the graph shows the real result.
- Default ranges about ±12 dB, with 0.1 dB steps.

### Parametric bands (both preference levels)

- Types: peaking (PK), low shelf (LSC), high shelf (HSC).
- Fields: enabled, type, frequency (20 Hz to 20 kHz), gain (dB), Q.
- No fixed band limit; add and remove freely.
- Bands can be edited numerically and also by dragging handles on the graph.

### Preamp

- Auto mode (default): preamp = −(max of the combined curve over 20 Hz to 20 kHz), rounded down to 0.1 dB, and never positive. It is recomputed on every change.
- The preamp is shown at all times. An optional manual offset (extra headroom) may be added. Turning auto off is left to the builder.

## AutoEQ

- **Source:** the AutoEq GitHub repository (`jaakkopasanen/AutoEq`), `results/` tree. Each result has a `… ParametricEQ.txt` in Equalizer APO format (`Preamp:` line plus `Filter n: ON PK Fc … Hz Gain … dB Q …` lines, including LSC/HSC).
- **Index:** the full list of headphones, measurement sources (oratory1990, crinacle, Rtings, …) and targets is fetched once and cached locally. Refresh happens on demand and periodically (builder picks the interval, e.g. weekly). Search works offline from the cache once the index has been fetched.
- **Fetching a result:** the selected result's `ParametricEQ.txt` is downloaded when needed and its filters are **copied into the profile**. A profile therefore works offline and doesn't change if AutoEQ updates upstream. The profile also stores where the correction came from (headphone name, source, target, file path/URL, fetch date) so the user can re-fetch or switch source/target later.
- **Search UI:** type to search by name with fuzzy, case-insensitive matching. Results are grouped by headphone. For a headphone with several results, all source/target combinations are shown, with a sensible default preselected (prefer oratory1990, then Harman-style targets). The builder picks the exact ranking rule.
- AutoEQ's own preamp line is ignored; Curvy computes its own over all layers.
- The builder picks how to fetch the index (e.g. `results/INDEX.md` in the repo, or the GitHub tree API), but should handle GitHub rate limits and offline gracefully.

## Profiles

- Fields: id, name, optional AutoEQ correction (filter list + provenance), AutoEQ enabled toggle, profile preference (tone + bands), profile preference enabled toggle, ignore-universal flag, created/modified timestamps.
- Actions: create (from AutoEQ search or blank), rename, duplicate, delete, apply.
- Exactly one profile is **active** at a time, or none. With no active profile (first run, or after deleting the active one), `curvy.txt` contains only the universal preference, if enabled, plus its preamp. The tray menu shows a "No profile" entry for this state, separate from "EQ off".
- Edits to the active profile are applied live, with a short debounce.

## Equalizer APO integration

- Curvy writes its output to its own file in the APO config folder, e.g. `config\Curvy\curvy.txt`. It is fully regenerated on each change and contains a header comment ("Written by Curvy, hand edits will be overwritten"), the preamp, and the filter list.
- Curvy ensures `config.txt` contains `Include: Curvy\curvy.txt`.
- **Coexisting with other tools.** The user's current `config.txt` is:
  ```
  Include: peace.txt
  Include: fletcher.txt
  ```
  If Curvy simply added its include, its EQ would stack with the others. So on first run Curvy shows the other active lines in `config.txt` and recommends disabling them. Disabling comments them out with a Curvy marker, and nothing is deleted. A backup of the original `config.txt` is written before the first change. Fletcher has already been removed by the user, so its include is a stale line that is handled the same way.
- **Peace stays installed** (user decision) and may rewrite `config.txt`. Curvy therefore watches `config.txt` while running and also checks it on every apply. If Curvy's include is missing, Curvy re-adds it. If another EQ include has been re-enabled, Curvy shows a non-blocking warning ("Peace re-enabled its EQ, so effects may stack") with a one-click "disable it again". Curvy never silently fights another tool in a loop: it repairs its own include, and only the user's click disables others.
- **Clipping guarantee scope:** the auto preamp covers Curvy's own chain only. Other includes the user chooses to keep enabled are outside that guarantee, and the APO section in settings says so.
- A small "Equalizer APO" section in settings lists the other `config.txt` lines with enable/disable toggles and a "restore original config.txt" action. This is the non-dumbed-down escape hatch. (Proposed by Claude.)
- Writes are atomic (write a temp file, then replace) so APO never reads a half-written file.
- The **master EQ off** toggle writes an empty or bypassed `curvy.txt` and leaves `config.txt` alone.

## App shell and UI

- **Main window** with these areas (the builder picks the exact layout and navigation, e.g. NavigationView):
  - **Profiles**: list of profiles, active marker, create/duplicate/rename/delete.
  - **Profile editor**: AutoEQ section (source info, toggle, change source/target, re-fetch), profile preference (tone controls + bands), "ignore universal" flag.
  - **Universal preference**: the same editor (tone + bands) for the universal layer.
  - **AutoEQ search**: search box, results, "create profile from this".
  - **Settings**: start with Windows, Equalizer APO integration (other includes, restore), AutoEQ cache refresh.
- **Graph** (shared by the editors):
  - Log-frequency x-axis from 20 Hz to 20 kHz, dB y-axis.
  - Lines: AutoEQ correction, profile preference, universal preference, combined total. Each can be shown or hidden. The combined total is the most prominent.
  - Draggable band handles for the layer being edited.
  - The curve is computed from the real biquad responses (RBJ cookbook formulas, as used by Equalizer APO), not approximations.
  - The builder picks the implementation: a WinUI 3 chart component or a custom Win2D canvas.
- **Filter view**: a read-only text view of exactly what is written to `curvy.txt`, the "what's actually applied" view.
- **Tray**:
  - Icon with tooltip showing the active profile name.
  - Menu: list of profiles (active one checked; click applies), "EQ on/off" toggle, "Open Curvy", "Exit".
  - Left-click opens the main window.
  - Closing the window keeps Curvy running in the tray. Only "Exit" quits.
- **Start with Windows**: optional, off by default. When enabled, Curvy starts minimised to the tray. The builder picks the mechanism; a `HKCU\…\Run` entry is enough for an unpackaged app.
- Single instance: launching Curvy again focuses the existing instance.
- Theme follows Windows light/dark.

## Architecture

- `Curvy.Core`: a UI-free class library, unit tested. It contains:
  - The filter model and biquad response math.
  - Layer combination, preamp computation, and tone-control → filter conversion.
  - AutoEQ ParametricEQ parsing, index fetching, and caching.
  - Profile and settings persistence.
  - The Equalizer APO config reader/writer, including include management, backup and atomic write.
- `Curvy.App`: the Reactor/WinUI 3 app from the Reactor template. It contains the UI, tray, and window lifecycle, and calls into Core.
- `Curvy.Core.Tests`: tests for the math, parsing, config writing (against temp folders, never the real APO folder), and preamp.
- Profile, settings and the AutoEQ cache are stored under `%LocalAppData%\Curvy\` as JSON. One file per profile is preferred so profiles are easy to back up and share. The builder decides the exact layout.

## Decided by the user

- Purpose: profile switching, AutoEQ search, preference on top of AutoEQ. Simplified from Peace but not dumbed down.
- Output only; ignore microphones.
- Preference = tone controls + parametric bands (option "C").
- Per-headphone preference is saved in the profile together with its AutoEQ. The universal preference is separate.
- Profiles can start from AutoEQ or blank.
- Tray-based switching. Hotkeys later. No device linking for now.
- .NET 11, WinUI 3 via Reactor, Reactor templates, WinUI 3 components allowed.
- Personal use for now, may be released later.
- Peace stays installed. Fletcher is removed.

## Claude's calls (correctable)

- AutoEQ filters are copied into the profile (offline, stable) and their provenance is kept.
- Per-layer toggles and the "ignore universal" flag on profiles.
- Auto preamp over the combined curve.
- Curvy owns `config\Curvy\curvy.txt` plus one include. Other includes are commented out on request, with a backup, never deleted.
- Unpackaged app, data stored in `%LocalAppData%\Curvy`.
- Default AutoEQ source preference: oratory1990, then Harman-style targets.
- Tone control default frequencies (bass shelf ~105 Hz, treble shelf ~10 kHz, tilt pivot ~1 kHz).
- No active profile → only the universal preference is applied.
- The graph and preamp are computed at 48 kHz.
- Curvy repairs its own include automatically but only disables other tools' includes on a user click.

## Left to the builder

- Exact Reactor template variant and navigation layout.
- Graph implementation: a WinUI 3 chart component or Win2D.
- Tilt implementation details.
- AutoEQ index fetch mechanism, cache format, refresh interval, ranking of results.
- Persistence file layout, JSON schema, versioning.
- Debounce timing, error and empty states.
- Single-instance mechanism.

## First milestone (de-risk the preview stack)

A thin vertical slice that proves the Reactor-specific parts:

1. The app is created from the Reactor template on .NET 11, unpackaged, and builds and runs.
2. The tray icon and menu work. Closing the window keeps the app alive, and "Exit" quits.
3. A graph shows a hard-coded filter set's real response, and one band can be dragged.
4. Core writes `config\Curvy\curvy.txt` and the include, and APO picks it up.

If Reactor blocks any of these, the fallback is plain WinUI 3 for the UI layer only. Core is unaffected.
