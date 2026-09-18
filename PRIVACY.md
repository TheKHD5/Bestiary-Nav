# Optional usage reporting

Usage reporting is **off by default**, including when updating an existing installation.
Use `/bnav config` → **Privacy** → **Share optional usage statistics** to opt in or out.
The plugin works the same with reporting disabled or the service unavailable.

While enabled and the plugin is loaded, a check-in is sent about every five minutes:

- A random, locally generated installation ID. It is not derived from any personal or hardware information.
- The plugin version.

The server records the receipt time. No character or account names/IDs, world,
location, inventory, combat actions, feature usage, or other gameplay information
is sent. No account or sign-in is needed for reporting.

The maintainer uses a private, authenticated dashboard to understand adoption,
recent activity and plugin versions. These are estimates of participating
installations, not counts of individual people or all plugin users. Download
counts come separately from GitHub and include updates and repeat downloads.

## Controls and retention

- Turning reporting off stops future check-ins and cancels pending client requests.
  A request already in flight may still reach the server.
- **Reset reporting ID** creates a new random identity when enabled, or clears it
  when disabled. It does not enable reporting or delete earlier server records.
  Resets and reinstalls can count as separate installations.
- The database retains a hash of the ID, first/last receipt times, latest version,
  and daily activity for 90 days. Expired records are removed by hourly cleanup
  when the service receives traffic; inactive services delete expired data on
  their next cleanup. Statistics exclude expired installations immediately.
- The application stores no raw IP addresses. Hosting/network infrastructure
  necessarily processes IPs. Short-lived keyed IP hashes are used only for abuse
  limits, expire after ten minutes, and are removed during the same cleanup.

Reports use HTTPS at:
https://bestiary-nav-reporting.khalidalsuwaidi68.chatgpt.site

The public service provides privacy information and a write-only check-in endpoint.
Statistics require a separate server credential, held only in hosting secrets.
The dashboard is a separate owner-private Site. Neither dashboard credentials nor
statistics are distributed with the plugin or stored in this repository.

## Implementation

Client: `BestiaryNav/UsageReporting.cs`. It sends at most one request at a time,
uses a ten-second timeout, waits about five minutes even after failures, follows
no redirects, sends no cookies, and stops on plugin unload. Reporting performs no
native/game reads and does not block the framework thread.

Collector and dashboard source are included under `services/` for inspection.
The services use Sites hosting and D1 storage. See their deployment notes there.
