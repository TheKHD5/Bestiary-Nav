# Bestiary Nav

Find your next capture. Bestiary Nav connects Final Fantasy XIV's Master's Bestiary to beast locations, duties, and nearby uncaptured targets.

- **Click to find:** highlight an approximate spawn area on the game map, clear and check the target duty in Duty Finder, or show quest guidance. While queued, only highlight the duty. Joining stays manual.
- **Plan your collection:** see progress by area and duty, save favorites, and use **Where next?** for a level-based suggestion.
- **Auto Navigate:** optionally teleport and walk to outdoor capture areas with Lifestream and vnavmesh.
- **Location pop-up:** toggle navigation from Bestiary clicks, directly above the Bestiary. Turning it off also disables Auto Navigate and stops the current trip.
- **Spot uncaptured beasts:** customize label size, colors, range, and model/enemy-list visibility. Duty labels identify capture/gourd encounters.
- **Track nearby targets:** count each beast name once and clear its labels after capture.
- **Take over anytime:** see travel progress beside Auto Navigate and press Stop, or optionally cancel by moving manually.
- **Simple controls:** `/bnav` opens Master's Bestiary; the gear opens settings and the book opens your collection. Also available: `/bnav config`, `/bnav collection`, `/bnav next`, and `/bnav stop`.

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

- [Plugin source](BestiaryNav/): Bestiary integration, collection planner, settings, labels, map circles, and travel.
- [Collection data](BestiaryNav/collection.json): acquisition levels and gourd information for all 50 beasts.
- [Spawn-area notes](BestiaryNav/SPAWN-AREAS.md): circle coordinates, radius, and validation details.
- [Acquisition locations](BestiaryNav/LOCATIONS.md): map, duty, and quest destinations for all 50 beasts.
- [Installer images](assets/): icon, in-game previews, and original image sources.
- [Repository index](pluginmaster.json): installer details, images, and update links.
- [Release notes](releases/) and [publishing guide](RELEASING.md).
- [Tools](tools/): location data, image preparation, and release packaging.
- [MIT License](LICENSE).
