# Bestiary Nav

Find your next capture. Bestiary Nav connects Final Fantasy XIV's Master's Bestiary to beast locations, duties, and nearby uncaptured targets.

- **Click to find:** open a beast's map flag, target duty, or quest guidance.
- **Auto Navigate:** optionally teleport and walk to outdoor capture areas with Lifestream and vnavmesh.
- **Spot uncaptured beasts:** labels above models and beside enemy-list rows; green at or below your level, red above it.
- **Track nearby targets:** count each beast name once and clear its labels after capture.
- **Take over anytime:** optionally cancel travel by moving manually.
- **Simple controls:** `/bnav` opens Master's Bestiary; the gear beside Auto Navigate or `/bnav config` opens settings. `/bnav stop` cancels travel.

## Installation

1. Open Dalamud Settings with `/xlsettings`.
2. Under **Experimental → Custom Plugin Repositories**, paste this URL, press **+**, and save:

   ```text
   https://raw.githubusercontent.com/TheKHD5/Bestiary-Nav/main/pluginmaster.json
   ```

3. Open `/xlplugins`, search **Bestiary Nav**, and install.
4. Use `/bnav config` to choose your options, then `/bnav` to open the Bestiary.

Install **Lifestream** and **vnavmesh** separately to use Auto Navigate. Future updates appear in the Plugin Installer.

## Project files

- [Plugin source](BestiaryNav/): Bestiary integration, compact settings, markers, and navigation.
- [Acquisition locations](BestiaryNav/LOCATIONS.md): map, duty, and quest destinations for all 50 beasts.
- [Installer images](assets/): icon, in-game previews, and original image sources.
- [Repository index](pluginmaster.json): installer details, images, and update links.
- [Release notes](releases/) and [publishing guide](RELEASING.md).
- [Tools](tools/): location data, image preparation, and release packaging.
- [MIT License](LICENSE).
