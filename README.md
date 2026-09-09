# Bestiary Nav

A lightweight, MIT-licensed Dalamud plugin for Final Fantasy XIV's Master's Bestiary.

- Left-click a numbered Bestiary entry to open its acquisition map flag or target duty in Duty Finder.
- Optionally teleport and walk to outdoor capture areas using Lifestream and vnavmesh.
- Enable nearby uncaptured beast markers above models and beside existing enemy-list rows.
- Green markers indicate enemies at or below your current level; red markers indicate higher-level enemies.
- Count unique nearby beasts and remove their markers when capture records update.
- Use `/bnav` for settings and to open Master's Bestiary when capture records need loading.

Includes navigation data for all 50 beasts, with quest guidance for Cu Sith. Current plugin version: **0.5.3**.

The **Auto Navigate** checkbox above the top-left corner of Master's Bestiary toggles automatic travel on entry clicks. It follows the Bestiary window and shares its setting with `/bnav` settings.

## Install

No Dev Plugin Location is needed. In Dalamud Settings (`/xlsettings`), open **Experimental → Custom Plugin Repositories**, add this URL with the **+** button, and save:

```text
https://raw.githubusercontent.com/TheKHD5/Bestiary-Nav/main/pluginmaster.json
```

Open `/xlplugins`, search for **Bestiary Nav**, and install it. Use `/bnav` for settings. The same repository supplies future updates through the Plugin Installer.

If switching from the development build, disable the dev copy and remove its **Dev Plugin Location** before installing from the repository. Keep the existing configuration. Automatic travel requires **Lifestream** and **vnavmesh** installed separately; their status appears in Bestiary Nav settings.

Install files and checksums are available under [GitHub Releases](https://github.com/TheKHD5/Bestiary-Nav/releases). This is a custom repository distribution.

## Build from source

Requires the .NET 10 SDK and Dalamud API 15 development libraries installed by XIVLauncher. Native integrations are gated to the verified game and Dalamud versions in [binding.json](BestiaryNav/binding.json).

```powershell
git clone https://github.com/TheKHD5/Bestiary-Nav.git
cd Bestiary-Nav
dotnet build .\BestiaryNav\BestiaryNav.csproj -c Release
```

Add `BestiaryNav/bin/Release/BestiaryNav.dll` to Dalamud's development plugin locations. Enable it in Installed Dev Plugins, then use `/bnav` to configure it. Rebuild and reload the plugin after source changes.

## Checks

```powershell
dotnet run --project .\BestiaryNav.Checks -c Release
dotnet run --project .\BestiaryNav.NativeChecks -c Release
```

The native checks require Windows and the installed Dalamud development DLLs. They use allocated test fixtures in a separate process and do not access the game.

Live testing confirmed entry navigation, map flags, enemy-list alignment, model markers, level colors, unique counts, and marker removal after capturing a Lost Lamb. Automatic travel's dependency connections and teleport-to-walking handoff were also confirmed in-game. The automatic Bestiary-opening path still needs a separate fresh-login check.

## Project files

- [Plugin documentation](BestiaryNav/README.md): architecture, APIs, configuration and commands.
- [Acquisition locations](BestiaryNav/LOCATIONS.md): sourced map and duty destinations.
- [Native Bestiary binding](BestiaryNav/NATIVE-BINDING.md): verified events and patch validation.
- [Capture markers](BestiaryNav/CAPTURE-MARKERS.md): capture-state reading, row layout, coverage and tests.
- [Automatic travel](BestiaryNav/AUTO-TRAVEL.md): dependencies, controls, routing and cancellation.
- [Release process](RELEASING.md): packaging, repository index generation and publishing.
- `tools/`: development scripts for researching and rebuilding location data; no network access is needed at plugin runtime.

Licensed under the [MIT License](LICENSE).
