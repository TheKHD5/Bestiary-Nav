import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import { parseCheckIn, authorize, readSmallJson, recordCheckIn, summary, prune, admit } from '../lib/reporting.ts';

const id = '9ec5c2ce-1d0b-4b04-8a63-e198b7c1f5a0';
const second = '5fe5f923-a79a-4c10-9a6a-7e80c49e9f03';
const body = { installationId: id, pluginVersion: '0.6.15.0' };
const key = 'test-secret-for-server-only-summary-access-000000';
function database() {
  const sqlite = new DatabaseSync(':memory:');
  sqlite.exec(readFileSync(new URL('../drizzle/0000_dry_mandroid.sql', import.meta.url), 'utf8'));
  const prepare = sql => {
    let args = [];
    const result = { bind: (...values) => { args = values; return result; }, first: async () => sqlite.prepare(sql).get(...args) ?? null,
      all: async () => ({ results: sqlite.prepare(sql).all(...args) }), run: async () => sqlite.prepare(sql).run(...args) };
    return result;
  };
  return { prepare, batch: async entries => { sqlite.exec('BEGIN'); try { const result = await Promise.all(entries.map(e => e.run())); sqlite.exec('COMMIT'); return result; } catch (e) { sqlite.exec('ROLLBACK'); throw e; } }, sqlite };
}
test('schema rejects identifying extras and malformed IDs or versions', () => {
  assert.deepEqual(parseCheckIn(body), body);
  for (const value of [null, [], {}, { ...body, characterName: 'Must reject' }, { ...body, installationId: '1234' }, { ...body, pluginVersion: 'latest' }, { ...body, pluginVersion: '<script>' }]) assert.equal(parseCheckIn(value), null);
});
test('private summary requires the server-only credential', () => {
  assert.equal(authorize(null, key), false);
  assert.equal(authorize('Bearer wrong', key), false);
  assert.equal(authorize(`Bearer ${key}`, undefined), false);
  assert.equal(authorize(`Bearer ${key}`, key), true);
});
test('request parsing enforces content type and byte limit', async () => {
  const request = payload => new Request('https://example.invalid/api/check-in', { method: 'POST', headers: { 'content-type': 'application/json' }, body: payload });
  assert.deepEqual(await readSmallJson(request(JSON.stringify(body))), body);
  await assert.rejects(readSmallJson(request(' '.repeat(513))));
  await assert.rejects(readSmallJson(new Request('https://example.invalid', { method: 'POST', body: 'text' })));
});
test('check-ins deduplicate, update activity windows, prune and return no IDs', async () => {
  const db = database(); let now = 1790000000;
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => Response.json([{ tag_name: 'v0.6.15', html_url: 'https://github.com/TheKHD5/Bestiary-Nav/releases/tag/v0.6.15', draft: false, prerelease: false, assets: [{ name: 'latest.zip', download_count: 12 }, { name: 'checksums.txt', download_count: 5 }] }]);
  try {
    let result = await summary(db, now);
    assert.equal(result.reportingInstallations, 0); assert.equal(result.activeNow, 0); assert.equal(result.downloads.total, 12);
    await recordCheckIn(db, body, now); await recordCheckIn(db, body, now + 5);
    result = await summary(db, now + 6);
    assert.equal(result.reportingInstallations, 1); assert.equal(result.activeNow, 1); assert.equal(result.dailyActive, 1);
    assert.equal(result.days.reduce((n, d) => n + d.active, 0), 1);
    assert.equal(JSON.stringify(result).includes(id), false);
    await recordCheckIn(db, { ...body, installationId: second }, now + 7);
    result = await summary(db, now + 601);
    assert.equal(result.activeNow, 1); assert.equal(result.dailyActive, 2);
    await recordCheckIn(db, { ...body, pluginVersion: '0.6.16.0' }, now + 90000);
    result = await summary(db, now + 90001);
    assert.equal(result.dailyActive, 1); assert.equal(result.monthlyActive, 2);
    assert.ok(result.versions.some(v => v.version === '0.6.16.0'));
    assert.equal(result.days.filter(d => d.active).length, 2);
    const stored = db.sqlite.prepare('SELECT id FROM installations').all(); assert.ok(stored.every(r => r.id !== id && r.id.length === 64));
    await prune(db, now + 92 * 86400);
    result = await summary(db, now + 92 * 86400);
    assert.equal(result.reportingInstallations, 0); assert.equal(result.monthlyActive, 0);
  } finally { globalThis.fetch = originalFetch; db.sqlite.close(); }
});
test('rate limits use opaque expiring buckets and do not store IPs', async () => {
  const db = database(); const ip = '192.0.2.1';
  try {
    for (let i = 0; i < 120; i++) assert.equal(await admit(db, key, ip, 1790000000), true);
    assert.equal(await admit(db, key, ip, 1790000000), false);
    assert.equal(await admit(db, key, ip, 1790000060), true);
    const rows = db.sqlite.prepare('SELECT * FROM request_limits').all(); assert.equal(rows.length, 2); assert.equal(JSON.stringify(rows).includes(ip), false);
  } finally { db.sqlite.close(); }
});
