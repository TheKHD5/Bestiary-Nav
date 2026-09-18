import { env } from 'cloudflare:workers';
import { getChatGPTUser } from '../../chatgpt-auth';
export const dynamic = 'force-dynamic';
export async function GET() {
  if (!await getChatGPTUser()) return Response.json({ error: 'Sign in to view this dashboard.' }, { status: 401 });
  const headers = { 'Cache-Control': 'no-store' };
  if (!env.COLLECTOR_URL || !env.REPORTING_READ_KEY) return Response.json({ error: 'The reporting service is being connected.' }, { status: 503, headers });
  try {
    // Workers supports "manual", not "error". The !ok check rejects redirects
    // without following them or forwarding the server credential elsewhere.
    const response = await fetch(`${env.COLLECTOR_URL}/api/summary`, { headers: { Authorization: `Bearer ${env.REPORTING_READ_KEY}` }, signal: AbortSignal.timeout(10000), redirect: 'manual' });
    if (!response.ok) {
      console.error('reporting_upstream_status', response.status);
      throw new Error('Reporting unavailable');
    }
    return Response.json(await response.json(), { headers });
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown failure';
    console.error('reporting_connection_failed', message.replaceAll(env.REPORTING_READ_KEY, '[redacted]').slice(0, 240));
    return Response.json({ error: 'The reporting service is temporarily unavailable. Try refreshing shortly.' }, { status: 503, headers });
  }
}
