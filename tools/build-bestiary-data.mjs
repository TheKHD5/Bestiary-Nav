// Deterministic build from reviewed factual snapshots; no runtime network dependency.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const project = fileURLToPath(new URL('../BestiaryNav/', import.meta.url));
const read = name => fs.readFile(path.join(project, 'sources', name), 'utf8').then(JSON.parse);
const [roster, acquisition, mapData, dutyData] = await Promise.all([
  read('roster.json'), read('acquisition.json'), read('map-rows.json'), read('duty-rows.json'),
]);
const normalize = text => text.trim().toLowerCase().replace(/^the /, '');
const mapByName = new Map(mapData.maps.map(m => [normalize(m.name), m]));
const monsters = roster.beasts.map(beast => {
  const capture = acquisition.beasts.find(a => a.bestiaryNumber === beast.bestiaryNumber);
  if (!capture || capture.displayName !== beast.displayName) throw new Error(`Identity mismatch: ${beast.bestiaryNumber}`);
  const entry = { bestiaryNumber: beast.bestiaryNumber, displayName: beast.displayName,
    classification: beast.classification, captureTarget: capture.captureTarget,
    navigationKind: capture.capturePoint ? 'map' : beast.bestiaryNumber === 1 ? 'quest' : 'duty',
    acquisitionNote: '', sources: [roster.source, capture.beastSource, acquisition.source], locations: [], duty: null };
  if (entry.navigationKind === 'quest') {
    entry.acquisitionNote = 'Use the Cu Sith Gourd awarded by Strangers in the Wood, the Beastmaster starting quest. Cu Sith has no wild capture point or target duty.';
    entry.sources.push('https://ffxiv.consolegameswiki.com/wiki/Strangers_in_the_Wood');
  } else if (entry.navigationKind === 'duty') {
    const matches = dutyData.duties.filter(d => normalize(d.name) === normalize(capture.area));
    if (matches.length !== 1) throw new Error(`Ambiguous/missing duty: ${capture.area}`);
    const duty = matches[0];
    entry.duty = { contentFinderConditionId: duty.id, territoryTypeId: duty.territoryTypeId,
      name: capture.area, note: capture.detail.replace(capture.area, '').trim().replace(/^\(|\)$/g, ''),
      source: `${dutyData.sourceBase}/${duty.id}?fields=Name,TerritoryType,ContentType&version=${dutyData.gameVersion}` };
    entry.acquisitionNote = `Capture ${capture.captureTarget} in ${capture.area}. ${capture.gourd === '—' ? '' : 'Gourd alternative: ' + capture.gourd}`.trim();
    entry.sources.push(entry.duty.source);
  } else {
    const map = mapByName.get(normalize(capture.area));
    if (!map) throw new Error(`Missing verified map: ${capture.area}`);
    // A finer point is preferred only when close to the acquisition guide's point
    // and its published minimum enemy level does not exceed the guide's target level.
    const finer = (capture.enemyLocations ?? []).filter(p => p.area === capture.area && p.point &&
      (!Number.isInteger(p.point.x) || !Number.isInteger(p.point.y)) &&
      Number.parseInt(p.level) <= capture.minimumLevel &&
      Math.hypot(p.point.x - capture.capturePoint.x, p.point.y - capture.capturePoint.y) <= 1)
      .sort((a, b) => Math.hypot(a.point.x - capture.capturePoint.x, a.point.y - capture.capturePoint.y) -
        Math.hypot(b.point.x - capture.capturePoint.x, b.point.y - capture.capturePoint.y))[0];
    const point = finer?.point ?? capture.capturePoint;
    if (![point.x, point.y].every(n => Number.isFinite(n) && n >= 1 && n <= 1 + 4100 / map.sizeFactor))
      throw new Error(`Off-map coordinate: ${beast.displayName}`);
    const source = finer ? capture.enemySource + (capture.enemyRevision ? `?oldid=${capture.enemyRevision}` : '') : acquisition.source;
    entry.locations.push({ territoryTypeId: map.territoryTypeId, mapId: map.id, x: point.x, y: point.y,
      area: capture.area, note: `${capture.area} (X:${point.x}, Y:${point.y}); level ${capture.minimumLevel}+.`, source,
      precision: finer ? 'reported-to-1-decimal' : 'reported-to-whole-map-unit' });
    entry.acquisitionNote = 'Reported capture-area coordinates; enemies can move or be temporarily absent.';
    entry.sources.push(capture.enemySource, `${mapData.sourceBase}/${map.id}?fields=SizeFactor,TerritoryType,PlaceName.Name&version=${mapData.gameVersion}`);
  }
  entry.sources = [...new Set(entry.sources.filter(Boolean))];
  return entry;
});
if (monsters.length !== 50 || new Set(monsters.map(m => m.bestiaryNumber)).size !== 50)
  throw new Error('Expected 50 distinct Bestiary records');
const database = { schemaVersion: 2, idSpace: 'masters-bestiary-number', researchedOn: '2026-09-09',
  gameDataVersion: mapData.gameVersion, nativeIdMappingVerified: false, monsters };
await fs.writeFile(path.join(project, 'locations.json'), JSON.stringify(database, null, 2) + '\n');
const counts = Object.fromEntries(['map', 'duty', 'quest'].map(k => [k, monsters.filter(m => m.navigationKind === k).length]));
const lines = ['# Bestiary Nav destinations', '',
  'Names and published Bestiary numbers come from [Icy Veins](' + roster.source + '). Capture targets/areas are cross-checked against the [community Bestiary](' + acquisition.source + ') and individual enemy pages. Territory, map and duty IDs use XIVAPI game data version `' + mapData.gameVersion + '`.', '',
  '**These are published capture-area coordinates, not guaranteed stationary spawn positions.** Decimal precision is preserved only where a source publishes it. Native selection-to-number mapping remains unverified. Quest/duty entries have no invented X/Y.', '',
  '| No. | Beast | Capture target | Navigation | Destination |', '|---|---|---|---|---|'];
for (const m of monsters) {
  const p = m.locations[0];
  const target = p ? `${p.area}: **${p.x}, ${p.y}** (territory ${p.territoryTypeId}, map ${p.mapId})` :
    m.duty ? `${m.duty.name} (ContentFinderCondition **${m.duty.contentFinderConditionId}**); ${m.duty.note}` : 'Strangers in the Wood → Cu Sith Gourd';
  const source = p?.source ?? m.duty?.source ?? 'https://ffxiv.consolegameswiki.com/wiki/Strangers_in_the_Wood';
  lines.push(`| ${m.bestiaryNumber} | ${m.displayName} | ${m.captureTarget || 'Quest reward'} | ${m.navigationKind} | [${target}](${source}) |`);
}
lines.push('', '## Data decisions', '',
  '- Coeurl uses Master Coeurl in Upper La Noscea (9, 21), supported by the acquisition wiki and FFXIV Collect, instead of assuming every coeurl in Icy Veins\' listed Outer La Noscea area is an equivalent capture target.',
  '- Adamantoise uses the explicitly documented Giant Tortoise in Central Thanalan (24, 30); conflicting Western Thanalan reports were not merged into that location.',
  '- Ice Golem targets the Snowcloak duty. The acquisition wiki identifies Wandil, its first boss; some lists use the generic Ice Golem name. No unrelated overworld Ice Golem coordinate is substituted.',
  '- Duty coordinates were omitted intentionally in favor of the requested Duty Finder behavior. For example, Doctore\'s published (14, 30) does not fit the current Halatali map extent; a zone name alone is insufficient to select an old/rebuilt map.',
  '- Cu Sith is a quest reward. Its action prints acquisition guidance; it does not create a fake capture flag or open an unrelated duty.', '',
  'Raw research pages are cached locally and not distributed. `sources/` contains factual snapshots and source URLs. No guide prose, skill descriptions, or portraits are copied into the plugin.');
await fs.writeFile(path.join(project, 'LOCATIONS.md'), lines.join('\n') + '\n');
console.log(JSON.stringify({ total: monsters.length, ...counts, gameDataVersion: mapData.gameVersion }));
