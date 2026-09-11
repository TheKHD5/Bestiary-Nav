# Bestiary Nav update bot

A Discord bot posts a single announcement in the community's Updates channel whenever commits are pushed to `main`. A push matching a published stable release tag uses the release notes and update instructions. Other pushes show up to ten commit summaries and a comparison link. Local commits, tags alone, development branches, and pull requests do not post.

GitHub Actions runs the bot only when needed. There is no separate hosting bill or always-running process. The bot may appear offline between announcements; it uses Discord's HTTP API, not a persistent Gateway connection. No message-content or member intents are required, and announcements never ping users or roles.

## One-time connection

1. In the [Discord Developer Portal](https://discord.com/developers/applications), create an application named **Bestiary Nav Updates**. Use `assets/icon.png` for its icon and bot avatar.
2. On **Bot**, generate/copy its token. Store it directly as the GitHub Actions repository secret **DISCORD_BOT_TOKEN** in [repository secrets](https://github.com/TheKHD5/Bestiary-Nav/settings/secrets/actions). Never put the token in chat, a commit, or a workflow file.
3. Install the bot in the Bestiary Nav community with the `bot` scope. Give it **View Channel**, **Send Messages**, and **Embed Links** in the Updates channel. These permissions total `19456`; Administrator is unnecessary. An invite URL has this form, using the application's actual client ID:
   `https://discord.com/oauth2/authorize?client_id=APPLICATION_ID&scope=bot&permissions=19456`
4. Copy the Updates channel's ID (Discord Developer Mode → right-click channel → Copy Channel ID). Store it as the GitHub Actions repository variable **DISCORD_UPDATES_CHANNEL_ID** in [repository variables](https://github.com/TheKHD5/Bestiary-Nav/settings/variables/actions).
5. Publish `.github/workflows/discord-updates.yml` and the two scripts alongside it. In Actions → **Discord updates**, run the workflow with **dry_run** enabled to preview it, then disabled to post a connection test.

The account installing the bot needs permission to manage the server. Keep the bot's send permissions limited to the intended Updates channel. The application token is available only to the posting step, never to checkout or tests. The workflow grants GitHub's token only read access and pins its actions to reviewed commits.

## Behavior and troubleshooting

- `node --test tools/discord-updates.test.mjs` tests formatting, event selection, release matching, channel targeting, retries, and credential-safe failures without sending messages.
- Normal pushes made with Git or a personal access token trigger the workflow. GitHub suppresses follow-up workflows for pushes made using another workflow's `GITHUB_TOKEN`; such automation must explicitly dispatch this workflow or use an appropriately scoped GitHub App/PAT.
- The same push uses a stable Discord nonce, preventing duplicates during short retries. Discord retains nonce deduplication only for a few minutes; manually rerunning an old successful job can post again. Check the channel before rerunning an uncertain delivery.
- HTTP 401 usually means an invalid bot token; 403 means missing channel permissions; 404 usually means an incorrect/inaccessible channel. The workflow deliberately omits response bodies and credentials from logs.
- Rate limits and transient server/network failures receive bounded retries. Permanent errors fail the workflow, without repeated posts. The workflow does not modify the repository or create another plugin release.
- Posting to an announcement channel does not automatically crosspost to other servers.

References: [Discord messages](https://docs.discord.com/developers/resources/message#create-message), [Discord rate limits](https://docs.discord.com/developers/topics/rate-limits), [GitHub push events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#push).
