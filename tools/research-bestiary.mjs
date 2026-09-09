// Fetch factual roster and capture-point evidence. Raw pages stay in the ignored workspace cache.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const cache = path.join(root, '.cache/bestiary-nav');
const out = path.join(root, 'BestiaryNav/sources');
await fs.mkdir(cache, { recursive: true });
await fs.mkdir(out, { recursive: true });
const decode = x => x.replace(/&#x([0-9a-f]+);/gi, (_, n) => String.fromCodePoint(parseInt(n, 16)))
  .replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(+n))
  .replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&apos;|&#39;/g, "'");
const text = x => decode(x.replace(/<[^>]+>/g, ' ')).replace(/\s+/g, ' ').trim();
const tables = x => [...x.matchAll(/<table\b[^>]*>[\s\S]*?<\/table>/gi)].map(m => m[0]);
const rows = x => [...x.matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr>/gi)].map(m =>
  [...m[1].matchAll(/<td\b[^>]*>([\s\S]*?)<\/td>/gi)].map(c => c[1]));
const wiki = 'https://ffxiv.consolegameswiki.com';
const links = x => [...x.matchAll(/href="(\/wiki\/[^"#]+)"/g)].map(m => wiki + decode(m[1]));
const coords = x => {
  const m = text(x).match(/X:\s*([\d.]+)\s*,?\s*Y:\s*([\d.]+)/i);
  return m ? { x: +m[1], y: +m[2] } : null;
};
async function page(url) {
  const file = path.join(cache, encodeURIComponent(url) + '.html');
  try { return await fs.readFile(file, 'utf8'); } catch {}
  const response = await fetch(url, { headers: { 'User-Agent': 'BestiaryNav/0.2 research' }, signal: AbortSignal.timeout(30000) });
  if (!response.ok) throw new Error(`${response.status} ${url}`);
  const html = await response.text();
  await fs.writeFile(file, html);
  return html;
}
const icyUrl = 'https://www.icy-veins.com/ffxiv/beastmaster-pve-dps-beast-summary#known-beasts';
const icyHtml = await page(icyUrl);
const icy = rows(tables(icyHtml).find(t => t.includes('Cu Sith'))).filter(c => c.length >= 4)
  .map(c => ({ bestiaryNumber: +text(c[0]), displayName: text(c[1]), classification: text(c[2]), listedArea: text(c[3]) }));
if (icy.length !== 50 || icy.some((r, i) => r.bestiaryNumber !== i + 1)) throw new Error('Unexpected Icy Veins roster');
const masterUrl = wiki + "/wiki/Master%27s_Bestiary";
const masterHtml = await page(masterUrl);
const acquisition = rows(tables(masterHtml)[0]).filter(c => c.length === 6).map(c => ({
  bestiaryNumber: +text(c[0]), displayName: text(c[1]), beastSource: links(c[1])[0],
  captureTarget: text(c[2]), enemySource: links(c[2])[0] ?? null,
  area: text(c[3].match(/<a\b[^>]*>([\s\S]*?)<\/a>/i)?.[1] ?? ''), areaSource: links(c[3])[0] ?? null,
  capturePoint: coords(c[3]), detail: text(c[3]), minimumLevel: +text(c[4]),
  gourd: text(c[5]), gourdSources: [...new Set(links(c[5]))],
}));
if (acquisition.length !== 50 || acquisition.some((r, i) => r.displayName !== icy[i].displayName))
  throw new Error('Bestiary sources disagree on identities');
let next = 0;
await Promise.all(Array.from({ length: 4 }, async () => {
  while (next < acquisition.length) {
    const entry = acquisition[next++];
    if (!entry.enemySource) continue;
    try {
      const html = await page(entry.enemySource);
      entry.enemyRevision = html.match(/title=[^"&]*&amp;oldid=(\d+)/)?.[1] ?? null;
      const relevant = tables(html).filter(t => /Coordinates/.test(t) && /Zone/.test(t));
      entry.enemyLocations = relevant.flatMap(t => rows(t)).filter(c => c.length >= 3).map(c => ({
        area: text(c[0]), point: coords(c[1]), level: text(c[2]),
      }));
    } catch (error) { entry.researchError = error.message; }
  }
}));
await fs.writeFile(path.join(out, 'roster.json'), JSON.stringify({ source: icyUrl, retrievedOn: '2026-09-09', beasts: icy }, null, 2) + '\n');
await fs.writeFile(path.join(out, 'acquisition.json'), JSON.stringify({ source: masterUrl, retrievedOn: '2026-09-09', beasts: acquisition }, null, 2) + '\n');
for (const a of acquisition) console.log(JSON.stringify({ n: a.bestiaryNumber, name: a.displayName, target: a.captureTarget,
  area: a.area, guide: a.capturePoint, points: a.enemyLocations?.filter(l => l.area === a.area), error: a.researchError }));
