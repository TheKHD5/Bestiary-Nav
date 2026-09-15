# Levelling target species

Patrol groups choose destinations. `FarmingOptions.AreaTargets` independently saves Target/Ignore decisions by TerritoryType and BNpcName ID. Without a saved filter, every nonzero species ID is allowed. **Ignore all** changes the default for newly discovered species too; checking a species then enables only that exception. These choices affect new Levelling pulls, including FATE targets. Defense against an enemy attacking the player or companion remains available.

`farming-targets.json` seeds the checklist before travel with known species in the 16 zones used by `farming-areas.json`. It includes ordinary and special/event spawns. Live combatant sightings extend this list once per second, even while Levelling is off. Saved overrides remain selectable after reload. No network requests run in the plugin.

The catalog is a zone-wide reference, not an exhaustive spawn map: some listed enemies may be absent, event-specific, or outside the patrol circle. Displayed levels combine catalog ranges and live sightings. Actual enemy levels, the current patrol circle and floor, targetability, and the FATE/Notorious Monster settings still determine whether a new pull is eligible. Ignoring a species does not create an alternative route to another species elsewhere in the zone.

## Sources and refresh

- [GarlandTools mob index](https://www.garlandtools.org/db/doc/browse/en/2/mob.json)
- [GarlandTools location index](https://www.garlandtools.org/db/doc/core/en/3/data.json)
- [Source field definitions](https://github.com/ufx/GarlandTools/blob/master/Garland.Data/Modules/Mobs.cs)

Run `tools/update-farming-targets.ps1` with PowerShell 7.2+ to refresh the bundled index. GarlandTools' combined mob ID is `BNpcBase * 10000000000 + BNpcName`; its zone field is a **PlaceName** ID. Runtime lookup maps TerritoryType to PlaceName and resolves display names through the game's BNpcName sheet. Instances are excluded from the seed data. Review data changes and run BestiaryNav.Checks before publishing.
