# Bestiary Nav

Find your next capture. Bestiary Nav connects Final Fantasy XIV's Master's Bestiary to beast locations, duties, and nearby uncaptured targets.

- **Click to find:** highlight an approximate spawn area on the game map, clear and check the target duty in Duty Finder, or show quest guidance. While queued, only highlight the duty. Joining stays manual.
- **Plan your collection:** see progress by area and duty, save favorites, and use **Where next?** for a level-based suggestion.
- **Auto Navigate:** teleport, mount, and fly to spawn areas where available with Lifestream and vnavmesh. Underground Ghost/Bogy uses a ground route; land before starting it.
- **Make it yours:** choose Modern, Classic, or Compact styling, seven color palettes, and window opacity. Resize settings and collection windows from the corner.
- **Control chat output:** choose which messages appear and their local chat channels, or mute them all.
- **Auto Capture:** optionally cast Capture on your eligible BST main target in combat, with an HP threshold and no recast while your existing mark is active.
- **Capture runs:** optionally travel to a selected entry, apply Capture, and fight with Rotation Solver Reborn's BST rotation (7.5.6.8+). Wait at least three seconds after each defeat, then retry in the same area if still uncaptured. Defend against enemies attacking you, then resume the saved destination or spawn-area patrol. Enable under **Capture**; stop with `/bnav stop`. Dungeon entry stays manual.
- **Search the spawn area:** capture runs walk the configured circle when no eligible beast is nearby. Widen **Search radius** for larger habitats.
- **Heal between fights:** optionally wait for full HP before new Capture and Levelling pulls. Existing fights and defense continue.
- **Levelling (Experimental):** fight any eligible enemy type within the selected level range and patrol circle, move on as BST levels up, and stop at a target level. Toggle it beside **Capture all available** above the Bestiary. Optional chocobo, food refresh, combat FATEs, Notorious Monster filtering, and auto respawn. The initial area catalog covers ARR targets up to level 49; live FATEs supplement it.
- **Capture all available:** toggle a batch from the Bestiary toolbar or Capture settings. Collect eligible overworld beasts at your current BST level, retry the selected entry until capture is confirmed, and stop when none remain. `/bnav stop` cancels the batch and retries.
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

- [Plugin source](BestiaryNav/): Bestiary integration, collection planner, settings, labels, map circles, travel, Auto Capture, and Rotation Solver capture runs.
- [Collection data](BestiaryNav/collection.json): acquisition levels and gourd information for all 50 beasts.
- [Farming areas](BestiaryNav/farming-areas.json): sourced enemy levels and spawn coordinates for farming.
- [Spawn-area notes](BestiaryNav/SPAWN-AREAS.md): circle coordinates, radius, and validation details.
- [Acquisition locations](BestiaryNav/LOCATIONS.md): map, duty, and quest destinations for all 50 beasts.
- [Installer images](assets/): icon, in-game previews, and original image sources.
- [Repository index](pluginmaster.json): installer details, images, and update links.
- [Release notes](releases/) and [publishing guide](RELEASING.md).
- [Tools](tools/): location data, image preparation, and release packaging.
- [Discord update bot](tools/DISCORD-UPDATES.md): automatic project announcements through GitHub Actions.
- [MIT License](LICENSE).
