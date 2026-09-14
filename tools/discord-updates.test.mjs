import test from 'node:test';
import assert from 'node:assert/strict';
import { announcement, releaseForCommit, sendAnnouncement, REPOSITORY } from './discord-updates.mjs';

const after = 'a'.repeat(40);
const before = 'b'.repeat(40);
const channelId = '123456789012345678';
const token = 'test-only-token';
const event = () => ({ repository: { full_name: REPOSITORY }, ref: 'refs/heads/main', before, after,
  commits: [{ id: after, message: 'Improve uncaptured labels\nDetailed body' }] });
const ok = () => new Response(JSON.stringify({ id: '234567890123456789' }), { status: 200 });
const index = [{ DownloadLinkUpdate: `https://github.com/${REPOSITORY}/releases/download/v0.6.0/latest.zip` }];

test('one push announcement summarizes commits and links the whole comparison', () => {
  const result = announcement(event());
  assert.equal(result.embeds.length, 1);
  assert.match(result.embeds[0].url, new RegExp(`${before}\\.\\.\\.${after}$`));
  assert.match(result.embeds[0].description, /Improve uncaptured labels/);
  assert.doesNotMatch(result.embeds[0].description, /Detailed body/);
  assert.deepEqual(result.allowed_mentions, { parse: [] });
});

test('branches, tags, deletions and unsupported events do not announce', () => {
  for (const ref of ['refs/heads/codex/work', 'refs/tags/v0.6.0']) assert.equal(announcement({ ...event(), ref }), null);
  assert.equal(announcement({ ...event(), deleted: true }), null);
  assert.equal(announcement(event(), { eventName: 'pull_request' }), null);
  assert.throws(() => announcement({ ...event(), repository: { full_name: 'another/repo' } }), /Unexpected repository/);
  assert.throws(() => announcement({ ...event(), after: 'not-a-sha' }), /Invalid push commit/);
});

test('commit content cannot inject links or mentions into message behavior', () => {
  const source = event();
  source.commits[0].message = '@everyone [click](https://example.invalid) **oops**';
  source.commits[0].url = 'https://example.invalid';
  const result = announcement(source);
  assert.ok(result.embeds[0].description.includes('\\[click\\]\\(https://example.invalid\\)'));
  assert.deepEqual(result.allowed_mentions.parse, []);
  assert.ok(result.embeds[0].url.startsWith(`https://github.com/${REPOSITORY}/compare/`));
});

test('large pushes stay inside Discord embed limits', () => {
  const source = event();
  source.commits = Array.from({ length: 100 }, () => ({ id: after, message: '*'.repeat(1000) }));
  const embed = announcement(source).embeds[0];
  assert.ok(embed.description.length <= 4096);
  assert.ok(embed.title.length + embed.description.length + embed.footer.text.length <= 6000);
  assert.equal(embed.description.split('\n').filter(line => line.startsWith('• ')).length, 3);
  assert.ok(embed.description.length < 1000);
});

test('push retry nonce is stable; different pushes and manual tests are distinct', () => {
  const a = announcement(event());
  assert.equal(a.nonce, announcement(event()).nonce);
  assert.ok(a.nonce.length <= 25);
  assert.equal(a.enforce_nonce, true);
  assert.notEqual(a.nonce, announcement({ ...event(), after: 'c'.repeat(40) }).nonce);
  assert.notEqual(a.nonce, announcement(event(), { eventName: 'workflow_dispatch', runId: '1' }).nonce);
});

test('release notes are bounded and ordinary updates do not claim a release', () => {
  const result = announcement(event(), { release: { tag_name: 'v0.6.0', body: 'x'.repeat(9000), draft: false, prerelease: false } });
  assert.equal(result.embeds[0].title, 'Bestiary Nav v0.6.0 is available');
  assert.ok(result.embeds[0].description.length <= 4096);
  assert.match(result.embeds[0].description, /\/xlplugins/);
  assert.match(announcement(event()).embeds[0].title, /project update/);
  assert.match(announcement(event(), { release: { tag_name: 'v0.6.0', draft: true } }).embeds[0].title, /project update/);
});

test('release announcements prefer short installer bullets over full validation notes', () => {
  const release = { tag_name: 'v0.6.2', body: '- Very long full details\n\nLocal Validation: 817 checks\nValidation limits: live checks pending' };
  const embed = announcement(event(), { release, releaseSummary:
    '- Auto Capture for eligible BST targets.\n- Configurable HP threshold.\n\nFull details on GitHub repo:\nhttps://github.com/TheKHD5/Bestiary-Nav' }).embeds[0];
  assert.match(embed.description, /Auto Capture/);
  assert.match(embed.description, /Configurable HP/);
  assert.match(embed.description, /Full details on GitHub repo/);
  assert.doesNotMatch(embed.description, /Validation|validation|817|Very long/);
  assert.ok(embed.description.length < 450);
});

test('fallback release notes exclude validation headings, paragraphs, and section bullets', () => {
  for (const heading of ['Local validation:', '**Validation limits:**', '## Local Validation', '- Validation:']) {
    const embed = announcement(event(), { release: { tag_name: 'v0.6.2',
      body: `# Release\n- New feature.\n\n${heading}\n- Private test details.\nAnother validation paragraph.` } }).embeds[0];
    assert.match(embed.description, /New feature/);
    assert.doesNotMatch(embed.description, /validation|Private|paragraph/i);
  }
});

test('release summaries cap both bullet count and individual length', () => {
  const result = announcement(event(), { release: { tag_name: 'v0.6.2', body:
    Array.from({ length: 30 }, (_, i) => `- Feature ${i}: ${'detail '.repeat(200)}`).join('\n') } });
  const text = result.embeds[0].description;
  assert.equal(text.split('\n').filter(line => line.startsWith('• ')).length, 5);
  assert.ok(text.length < 800);
  assert.doesNotMatch(text, /Feature 5:/);
  assert.deepEqual(result.allowed_mentions, { parse: [] });
});

test('a manual connection test does not claim new changes were pushed', () => {
  const embed = announcement(event(), { eventName: 'workflow_dispatch', runId: '42' }).embeds[0];
  assert.equal(embed.title, 'Bestiary Nav — update bot connected');
  assert.match(embed.description, /Future project updates/);
  assert.doesNotMatch(embed.description, /Improve uncaptured labels|New project changes have been pushed/);
});

test('new-branch and missing-commit payloads still link the head commit', () => {
  const source = { ...event(), before: '0'.repeat(40), commits: [] };
  assert.equal(announcement(source).embeds[0].url, `https://github.com/${REPOSITORY}/commit/${after}`);
  assert.match(announcement(source).embeds[0].description, /New project changes/);
});

test('release lookup requires exact tag commit and published stable release', async () => {
  const calls = [];
  const fake = async url => {
    calls.push(url);
    return new Response(JSON.stringify(url.includes('/git/ref/') ? { object: { type: 'commit', sha: after } } :
      { tag_name: 'v0.6.0', draft: false, prerelease: false, body: 'Release changes' }));
  };
  assert.equal((await releaseForCommit(after, index, '', fake)).tag_name, 'v0.6.0');
  assert.equal(calls.length, 2);
  assert.equal(await releaseForCommit(before, index, '', fake), undefined);
  assert.equal(calls.length, 3); // Mismatched tag never fetches release notes.
});

test('annotated tags are resolved and external download URLs cannot redirect GitHub requests', async () => {
  const urls = [];
  const fake = async url => {
    urls.push(url);
    return new Response(JSON.stringify(url.includes('/git/ref/') ? { object: { type: 'tag', sha: before } } :
      url.includes('/git/tags/') ? { object: { type: 'commit', sha: after } } :
      { tag_name: 'v0.6.0', draft: false, prerelease: false }));
  };
  assert.ok(await releaseForCommit(after, index, '', fake));
  assert.equal(urls.length, 3);
  assert.equal(await releaseForCommit(after, [{ DownloadLinkUpdate: 'https://example.invalid/latest.zip' }], '', fake), undefined);
  assert.equal(urls.length, 3);
});

test('bot uses the single configured channel with the expected authorization', async () => {
  const payload = announcement(event());
  const id = await sendAnnouncement(payload, { token, channelId, fetchFn: async (url, options) => {
    assert.equal(url, `https://discord.com/api/v10/channels/${channelId}/messages`);
    assert.equal(options.headers.Authorization, `Bot ${token}`);
    assert.equal(options.redirect, 'error');
    assert.deepEqual(JSON.parse(options.body), payload);
    return ok();
  } });
  assert.equal(id, '234567890123456789');
});

test('missing credentials and invalid channel IDs fail before networking', async () => {
  const fetchFn = () => { throw new Error('Should not run'); };
  await assert.rejects(sendAnnouncement(announcement(event()), { channelId, fetchFn }), /DISCORD_BOT_TOKEN/);
  await assert.rejects(sendAnnouncement(announcement(event()), { token, channelId: 'updates', fetchFn }), /DISCORD_UPDATES_CHANNEL_ID/);
});

test('rate limits wait before retrying the same nonce', async () => {
  const waits = []; const bodies = []; let attempts = 0;
  await sendAnnouncement(announcement(event()), { token, channelId, delay: async ms => waits.push(ms), fetchFn: async (_, options) => {
    bodies.push(options.body);
    return attempts++ === 0 ? new Response(JSON.stringify({ retry_after: 1.5 }), { status: 429 }) : ok();
  } });
  assert.deepEqual(waits, [1600]);
  assert.equal(bodies[0], bodies[1]);
});

test('transient errors retry a bounded number of times', async () => {
  let attempts = 0;
  await sendAnnouncement(announcement(event()), { token, channelId, delay: async () => {}, fetchFn: async () => {
    attempts++;
    if (attempts === 1) throw new Error('Network timeout');
    if (attempts === 2) return new Response('', { status: 503 });
    return ok();
  } });
  assert.equal(attempts, 3);
});

test('permanent errors never echo response bodies or retry', async () => {
  let attempts = 0;
  await assert.rejects(sendAnnouncement(announcement(event()), { token, channelId, fetchFn: async () => {
    attempts++; return new Response('sensitive-server-body', { status: 403 });
  } }), error => /HTTP 403/.test(error.message) && !/sensitive-server-body|test-only-token/.test(error.message));
  assert.equal(attempts, 1);
});

test('excessive rate-limit delays stop instead of waiting unboundedly', async () => {
  await assert.rejects(sendAnnouncement(announcement(event()), { token, channelId, fetchFn: async () =>
    new Response(JSON.stringify({ retry_after: 900 }), { status: 429 }) }), /rate limit/);
});
