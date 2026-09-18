# Bestiary Nav usage services

Source snapshots for the two deployed Sites applications. No credentials, Site IDs,
local databases or compiled output are included. The original starter license
notices are preserved.

- `collector/`: public privacy page and POST `/api/check-in`; D1 database; owner-authenticated GET `/api/summary` returns aggregate statistics only.
- `dashboard/`: owner-private Sites application. Every page and API request requires
  Sites sign-in and the site's owner-only access policy. The browser never receives
  the reporting service credential.

Both use the Sites Vinext starter with its locked dependencies. Initialize each as
a separate Site, preserve the declared storage bindings, and deploy through Sites.
The collector needs the `DB` D1 binding and its generated migrations. It uses
schema-only migrations; runtime handlers never create or alter tables.

Set `REPORTING_READ_KEY` to the same independently generated, strong secret on both
Sites using the Sites environment-variable tool, marked secret. Set `COLLECTOR_URL`
on the dashboard to the collector's HTTPS origin. This credential also authorizes
DELETE `/api/summary` for owner-directed removal of a reporting ID's records and
temporary test cleanup; it must never be included in the plugin or client bundle.

The collector's audience is public so opted-in plugins can send check-ins. Its
statistics endpoint still requires the server credential. The dashboard audience
must remain owner-private. Never use a public site's mere successful sign-in as
proof of ownership.

## Checks

- Collector: `node --test tests/reporting.test.mjs` on Node 24; checks the actual
  generated SQL against isolated in-memory SQLite, including deduplication,
  activity windows, expiry, authentication, input filtering and abuse limits.
- Both projects: install locked dependencies, `npx tsc --noEmit`, then `npm run build`.
- Plugin: the normal C# checks also cover opt-in, payload minimization, retries,
  ID reset, opt-out and unloading.

Live deployment checks on 2026-09-18 confirmed unauthenticated stats return 401,
extra fields return 400, valid reports return 204, duplicate reports count once,
and GitHub totals load. The temporary reporting ID was deleted after the check.
The dashboard's private deployment completed successfully; browser interaction
testing was not performed.

## Metric definitions and limits

- Active now: latest receipt within ten minutes.
- Daily/monthly active: latest receipt within 24 hours / 30 days.
- Reporting installations: distinct random identities seen within 90 days.
- Daily chart: distinct identities per UTC day for the last 30 calendar days.
- Versions: latest reported version for identities active in the last 30 days.
- Downloads: GitHub `latest.zip` download totals from retained public releases;
  cached for ten minutes. Failed refreshes retain the previous snapshot.

These are estimates of consenting installations, not people. Reinstalls, reset IDs,
disabled reporting and forged reports affect accuracy. Public telemetry has no
secret client credential; putting one in an open-source plugin would not make the
reports trustworthy. Request size limits, strict schemas, hashed expiring network
rate limits and per-ID check-in spacing reduce abuse but cannot eliminate it.

Records and expired rate-limit buckets are pruned on at most hourly intervals when
traffic arrives. Dashboard calculations independently exclude expired installations.
No raw IPs or user identifiers from the game are retained by the application.
