import { env } from 'cloudflare:workers';
import { authorize, summary, parseCheckIn, readSmallJson, identifierHash } from '@/lib/reporting';
export const dynamic = 'force-dynamic';
export async function GET(request: Request) {
  const headers = { 'Cache-Control': 'no-store' };
  if (!authorize(request.headers.get('authorization'), env.REPORTING_READ_KEY)) return new Response(null, { status: 401, headers });
  if (!env.DB) return new Response(null, { status: 503, headers });
  try { return Response.json(await summary(env.DB, Math.floor(Date.now() / 1000)), { headers }); }
  catch { return new Response(null, { status: 503, headers }); }
}

// Owner-authenticated removal supports data requests and removes temporary test reports.
export async function DELETE(request: Request) {
  const headers = { 'Cache-Control': 'no-store' };
  if (!authorize(request.headers.get('authorization'), env.REPORTING_READ_KEY)) return new Response(null, { status: 401, headers });
  if (!env.DB) return new Response(null, { status: 503, headers });
  try {
    const value = parseCheckIn(await readSmallJson(request));
    if (!value) return new Response(null, { status: 400, headers });
    const id = identifierHash(value.installationId);
    await env.DB.batch([env.DB.prepare('DELETE FROM activity WHERE installation=?').bind(id), env.DB.prepare('DELETE FROM installations WHERE id=?').bind(id)]);
    return new Response(null, { status: 204, headers });
  } catch { return new Response(null, { status: 503, headers }); }
}
