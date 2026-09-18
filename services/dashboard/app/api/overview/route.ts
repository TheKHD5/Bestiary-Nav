import { env } from 'cloudflare:workers';
import { getChatGPTUser } from '../../chatgpt-auth';
export const dynamic = 'force-dynamic';
export async function GET() {
  if (!await getChatGPTUser()) return Response.json({ error: 'Sign in to view this dashboard.' }, { status: 401 });
  const headers = { 'Cache-Control': 'no-store' };
  if (!env.COLLECTOR_URL || !env.REPORTING_READ_KEY) return Response.json({ error: 'The reporting service is being connected.' }, { status: 503, headers });
  try {
    const response = await fetch(`${env.COLLECTOR_URL}/api/summary`, { headers: { Authorization: `Bearer ${env.REPORTING_READ_KEY}` }, signal: AbortSignal.timeout(10000), redirect: 'error' });
    if (!response.ok) throw new Error('Reporting unavailable');
    return Response.json(await response.json(), { headers });
  } catch {
    return Response.json({ error: 'The reporting service is temporarily unavailable. Try refreshing shortly.' }, { status: 503, headers });
  }
}
