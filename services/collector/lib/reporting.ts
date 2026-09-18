import { createHash, createHmac, timingSafeEqual } from 'node:crypto';

const DAY = 86400;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
export function parseCheckIn(value: unknown): { installationId: string; pluginVersion: string } | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const v = value as Record<string, unknown>;
  if (Object.keys(v).sort().join(',') !== 'installationId,pluginVersion' || typeof v.installationId !== 'string' || !UUID.test(v.installationId) || typeof v.pluginVersion !== 'string' || !/^\d{1,4}\.\d{1,4}\.\d{1,4}(?:\.\d{1,4})?$/.test(v.pluginVersion)) return null;
  return { installationId: v.installationId, pluginVersion: v.pluginVersion };
}
export function authorize(header: string | null, secret: string | undefined) {
  if (!secret || secret.length < 32 || !header || header.length > 256) return false;
  return timingSafeEqual(createHash('sha256').update(header).digest(), createHash('sha256').update(`Bearer ${secret}`).digest());
}
export async function readSmallJson(request: Request): Promise<unknown> {
  if (!(request.headers.get('content-type') || '').toLowerCase().startsWith('application/json')) throw new Error('Content type');
  if (Number(request.headers.get('content-length')) > 512) throw new Error('Body too large');
  const reader = request.body?.getReader(); if (!reader) throw new Error('Missing body');
  let bytes = 0; const chunks: Uint8Array[] = [];
  try {
    while (true) { const { value, done } = await reader.read(); if (done) break; bytes += value.length; if (bytes > 512) throw new Error('Body too large'); chunks.push(value); }
    const body = new Uint8Array(bytes); let offset = 0; for (const chunk of chunks) { body.set(chunk, offset); offset += chunk.length; }
    return JSON.parse(new TextDecoder().decode(body));
  } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
}
export const identifierHash = (id: string) => createHash('sha256').update(id).digest('hex');
export async function admit(db: D1Database, secret: string, ip: string, now: number) {
  // Short-lived HMAC buckets enforce abuse limits without storing raw IP addresses.
  const bucket = createHmac('sha256', secret).update(`rate:${Math.floor(now / 60)}:${ip}`).digest('hex');
  const row = await db.prepare('INSERT INTO request_limits(bucket,hits,expires) VALUES(?,1,?) ON CONFLICT(bucket) DO UPDATE SET hits=hits+1 RETURNING hits').bind(bucket, now + 600).first<{ hits: number }>();
  return !!row && row.hits <= 120;
}
export async function recordCheckIn(db: D1Database, value: { installationId: string; pluginVersion: string }, now: number) {
  const id = identifierHash(value.installationId);
  const existing = await db.prepare('SELECT last_seen FROM installations WHERE id=?').bind(id).first<{ last_seen: number }>();
  if (existing && existing.last_seen > now - 240) return;
  const day = new Date(now * 1000).toISOString().slice(0, 10);
  await db.batch([
    db.prepare('INSERT INTO installations(id,first_seen,last_seen,version) VALUES(?,?,?,?) ON CONFLICT(id) DO UPDATE SET last_seen=MAX(last_seen,excluded.last_seen),version=CASE WHEN excluded.last_seen >= last_seen THEN excluded.version ELSE version END').bind(id, now, now, value.pluginVersion),
    db.prepare('INSERT OR IGNORE INTO activity(day,installation) VALUES(?,?)').bind(day, id),
  ]);
}
export async function prune(db: D1Database, now: number) {
  const previous = await db.prepare("SELECT updated FROM service_cache WHERE key='prune'").first<{ updated: number }>();
  if (previous && previous.updated > now - 3600) return;
  await db.batch([
    db.prepare('DELETE FROM activity WHERE day < ?').bind(new Date((now - 90 * DAY) * 1000).toISOString().slice(0, 10)),
    db.prepare('DELETE FROM installations WHERE last_seen < ?').bind(now - 90 * DAY),
    db.prepare('DELETE FROM request_limits WHERE expires < ?').bind(now),
    db.prepare("INSERT INTO service_cache(key,value,updated) VALUES('prune','',?) ON CONFLICT(key) DO UPDATE SET updated=excluded.updated").bind(now),
  ]);
}
type Downloads = { total: number; releases: { version: string; downloads: number; url: string }[]; updatedAt: string };
export async function downloads(db: D1Database, now: number): Promise<Downloads | null> {
  const cache = await db.prepare("SELECT value,updated FROM service_cache WHERE key='downloads'").first<{ value: string; updated: number }>();
  if (cache && cache.updated > now - 600) return JSON.parse(cache.value);
  try {
    const releases: Downloads['releases'] = [];
    for (let page = 1; page <= 10; page++) {
      const response = await fetch(`https://api.github.com/repos/TheKHD5/Bestiary-Nav/releases?per_page=100&page=${page}`, { headers: { 'User-Agent': 'Bestiary-Nav-Insights', Accept: 'application/vnd.github+json' }, signal: AbortSignal.timeout(6000) });
      if (!response.ok) throw new Error('GitHub unavailable');
      const rows = await response.json() as { tag_name: string; html_url: string; draft: boolean; prerelease: boolean; assets: { name: string; download_count: number }[] }[];
      for (const r of rows) if (!r.draft && !r.prerelease) releases.push({ version: r.tag_name, url: r.html_url, downloads: r.assets.filter(a => a.name === 'latest.zip').reduce((n, a) => n + a.download_count, 0) });
      if (rows.length < 100) break;
      if (page === 10) throw new Error('Release list exceeded limit');
    }
    const result = { total: releases.reduce((n, r) => n + r.downloads, 0), releases, updatedAt: new Date(now * 1000).toISOString() };
    await db.prepare("INSERT INTO service_cache(key,value,updated) VALUES('downloads',?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated=excluded.updated").bind(JSON.stringify(result), now).run();
    return result;
  } catch { return cache ? JSON.parse(cache.value) : null; }
}
export async function summary(db: D1Database, now: number) {
  await prune(db, now);
  const counts = await db.prepare('SELECT COUNT(*) AS reportingInstallations,COALESCE(SUM(last_seen>=?),0) AS activeNow,COALESCE(SUM(last_seen>=?),0) AS dailyActive,COALESCE(SUM(last_seen>=?),0) AS monthlyActive FROM installations WHERE last_seen>=?').bind(now - 600, now - DAY, now - 30 * DAY, now - 90 * DAY).first();
  const fromDay = new Date((now - 29 * DAY) * 1000).toISOString().slice(0, 10);
  const daily = await db.prepare('SELECT day,COUNT(*) AS active FROM activity WHERE day>=? GROUP BY day ORDER BY day').bind(fromDay).all<{ day: string; active: number }>();
  const versions = await db.prepare('SELECT version,COUNT(*) AS installations FROM installations WHERE last_seen>=? GROUP BY version ORDER BY installations DESC,version DESC LIMIT 50').bind(now - 30 * DAY).all();
  const days = Array.from({ length: 30 }, (_, i) => { const day = new Date((now - (29 - i) * DAY) * 1000).toISOString().slice(0, 10); return { day, active: daily.results.find(r => r.day === day)?.active ?? 0 }; });
  return { ...counts, days, versions: versions.results, downloads: await downloads(db, now), updatedAt: new Date(now * 1000).toISOString() };
}
