import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

export const REPOSITORY = 'TheKHD5/Bestiary-Nav';
const REPO_URL = `https://github.com/${REPOSITORY}`;
const SHA = /^[a-f0-9]{40}$/i;
const snowflake = /^\d{17,20}$/;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const clip = (value, max) => String(value ?? '').slice(0, max);
const escape = value => String(value ?? '').replace(/[\\`*_~|<>\[\]()]/g, '\\$&');

function shortReleaseNotes(summary) {
  const bullets = [];
  for (const line of String(summary ?? '').split(/\r?\n/)) {
    const plain = line.replace(/^[\s#>*•-]+/, '').replace(/[*_`]/g, '').trim();
    // Never copy validation sections or their subsequent details into Discord.
    if (/^(?:local\s+)?validation\b|^local data\s*:/i.test(plain)) break;
    const match = line.match(/^[-*•]\s+(.+)/);
    if (!match) continue;
    const text = match[1].replace(/[*_`]/g, '').trim();
    bullets.push(`• ${escape(text.length > 110 ? text.slice(0, 107).trimEnd() + '…' : text)}`);
    if (bullets.length === 5) break;
  }
  return bullets.join('\n');
}

export function announcement(event, { eventName = 'push', sha, runId = 'local', release, releaseSummary } = {}) {
  if (event.repository?.full_name !== REPOSITORY) throw new Error('Unexpected repository.');
  if (eventName !== 'push' && eventName !== 'workflow_dispatch') return null;
  if (event.ref !== 'refs/heads/main' || event.deleted) return null;
  const after = event.after ?? sha;
  if (!SHA.test(after ?? '') || /^0+$/.test(after)) throw new Error('Invalid push commit.');
  const before = SHA.test(event.before ?? '') && !/^0+$/.test(event.before) ? event.before : null;
  const url = before ? `${REPO_URL}/compare/${before}...${after}` : `${REPO_URL}/commit/${after}`;
  const commits = Array.isArray(event.commits) && event.commits.length ? event.commits :
    event.head_commit ? [event.head_commit] : [];
  const lines = commits.slice(-3).map(commit => {
    const subject = escape(clip(String(commit.message ?? 'Project update').split(/\r?\n/)[0], 110));
    return `• ${subject}`;
  });
  let title = 'Bestiary Nav — project update';
  let description = (lines.length ? lines.join('\n') : 'New project changes have been pushed to GitHub.') +
    `\n\n[Full details on GitHub repo](${url})`;
  let targetUrl = url;
  if (release && !release.draft && !release.prerelease && /^v\d+\.\d+\.\d+(?:\.\d+)?$/.test(release.tag_name)) {
    title = `Bestiary Nav ${release.tag_name} is available`;
    targetUrl = `${REPO_URL}/releases/tag/${release.tag_name}`;
    description = (shortReleaseNotes(releaseSummary) || shortReleaseNotes(release.body) || 'New features and fixes are available.') +
      `\n\n[Full details on GitHub repo](${targetUrl})\nUpdate: \`/xlplugins\``;
  } else if (eventName === 'workflow_dispatch') {
    title = 'Bestiary Nav — update bot connected';
    description = 'The update bot is connected. Future project updates pushed to `main` will be announced in this channel.';
  }
  const key = eventName === 'workflow_dispatch' ? `manual:${runId}` : `push:${after}`;
  return {
    allowed_mentions: { parse: [] },
    nonce: createHash('sha256').update(`${REPOSITORY}:${key}`).digest('hex').slice(0, 24),
    enforce_nonce: true,
    embeds: [{
      title: clip(title, 256), description: clip(description, 4096), url: targetUrl,
      color: release ? 0x62b77c : 0xc5a45b,
      thumbnail: { url: 'https://cdn.jsdelivr.net/gh/TheKHD5/Bestiary-Nav@v0.5.6/assets/icon.png' },
      footer: { text: `Bestiary Nav • main • ${after.slice(0, 7)}` },
    }],
  };
}

// A release announcement is allowed only when the published tag resolves to
// this push's exact commit. Ordinary code/docs pushes aren't called releases.
export async function releaseForCommit(after, index, githubToken, fetchFn = fetch) {
  const link = index?.[0]?.DownloadLinkUpdate;
  const tag = typeof link === 'string' && link.match(/^https:\/\/github\.com\/TheKHD5\/Bestiary-Nav\/releases\/download\/(v\d+\.\d+\.\d+(?:\.\d+)?)\/latest\.zip$/)?.[1];
  if (!tag || !SHA.test(after ?? '')) return undefined;
  async function get(path) {
    const response = await fetchFn(`https://api.github.com/repos/${REPOSITORY}/${path}`, {
      headers: { Accept: 'application/vnd.github+json', ...(githubToken ? { Authorization: `Bearer ${githubToken}` } : {}) },
      signal: AbortSignal.timeout(15000), redirect: 'error',
    });
    if (response.status === 404) return null;
    if (!response.ok) throw new Error('Could not verify GitHub release metadata.');
    return response.json();
  }
  let ref = (await get(`git/ref/tags/${tag}`))?.object;
  if (ref?.type === 'tag') ref = (await get(`git/tags/${ref.sha}`))?.object;
  if (ref?.type !== 'commit' || ref.sha !== after) return undefined;
  const release = await get(`releases/tags/${tag}`);
  return release && release.tag_name === tag && !release.draft && !release.prerelease ? release : undefined;
}

export async function sendAnnouncement(payload, { token, channelId, fetchFn = fetch, delay = sleep } = {}) {
  if (!token || /\s/.test(token)) throw new Error('Configure the DISCORD_BOT_TOKEN repository secret.');
  if (!snowflake.test(channelId ?? '')) throw new Error('Configure the DISCORD_UPDATES_CHANNEL_ID repository variable.');
  const url = `https://discord.com/api/v10/channels/${channelId}/messages`;
  // Keep the same nonce across retries. Never echo credentials or Discord error
  // bodies into public Actions logs; the status code is sufficient to diagnose.
  for (let attempt = 0; attempt < 3; attempt++) {
    let response;
    try {
      response = await fetchFn(url, {
        method: 'POST', headers: { Authorization: `Bot ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify(payload), signal: AbortSignal.timeout(15000), redirect: 'error',
      });
    } catch {
      if (attempt === 2) throw new Error('Discord request failed or timed out; delivery may have occurred. Check the channel before rerunning.');
      await delay(1000 * (attempt + 1));
      continue;
    }
    if (response.ok) {
      const message = await response.json();
      if (!snowflake.test(message.id ?? '')) throw new Error('Discord returned an invalid message receipt.');
      return message.id;
    }
    if (response.status === 429 && attempt < 2) {
      let seconds;
      try { seconds = Number((await response.json()).retry_after); } catch { /* handled below */ }
      if (!Number.isFinite(seconds) || seconds < 0 || seconds > 30) throw new Error('Discord rate limit is longer than this run can wait. Retry later.');
      await delay(Math.ceil(seconds * 1000) + 100);
      continue;
    }
    if (response.status >= 500 && attempt < 2) { await delay(1000 * (attempt + 1)); continue; }
    throw new Error(`Discord rejected the announcement (HTTP ${response.status}). Check the bot token and channel permissions.`);
  }
  throw new Error('Discord announcement was not delivered.');
}

async function main() {
  const event = JSON.parse(await readFile(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const eventName = process.env.GITHUB_EVENT_NAME;
  const sha = process.env.GITHUB_SHA;
  if (eventName === 'workflow_dispatch') event.ref = process.env.GITHUB_REF;
  // Validate event scope before fetching metadata or exposing the bot token.
  if (!announcement(event, { eventName, sha })) { console.log('No main-branch update to announce.'); return; }
  const index = JSON.parse(await readFile(new URL('../pluginmaster.json', import.meta.url), 'utf8'));
  const release = eventName === 'push' ? await releaseForCommit(event.after, index, process.env.GITHUB_TOKEN) : undefined;
  const payload = announcement(event, { eventName, sha, runId: process.env.GITHUB_RUN_ID, release,
    releaseSummary: release ? index?.[0]?.Changelog : undefined });
  if (process.env.DRY_RUN === 'true') { console.log(JSON.stringify(payload, null, 2)); return; }
  const id = await sendAnnouncement(payload, { token: process.env.DISCORD_BOT_TOKEN, channelId: process.env.DISCORD_UPDATES_CHANNEL_ID });
  console.log(`Discord announcement delivered (message ${id}).`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch(error => { console.error(error instanceof Error ? error.message : 'Announcement failed.'); process.exitCode = 1; });
}
