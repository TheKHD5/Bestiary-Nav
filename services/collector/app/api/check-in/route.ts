import { env } from 'cloudflare:workers';
import { admit, parseCheckIn, prune, readSmallJson, recordCheckIn } from '@/lib/reporting';
export const dynamic = 'force-dynamic';
const headers = { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' };
export async function POST(request: Request) {
  if (!env.DB || !env.REPORTING_READ_KEY) return new Response(null, { status: 503, headers });
  let value;
  try { value = parseCheckIn(await readSmallJson(request)); } catch { return new Response(null, { status: 400, headers }); }
  if (!value) return new Response(null, { status: 400, headers });
  const now = Math.floor(Date.now() / 1000);
  try {
    if (!await admit(env.DB, env.REPORTING_READ_KEY, request.headers.get('cf-connecting-ip') || 'unknown', now)) return new Response(null, { status: 429, headers: { ...headers, 'Retry-After': '300' } });
    await recordCheckIn(env.DB, value, now); await prune(env.DB, now);
    return new Response(null, { status: 204, headers });
  } catch { return new Response(null, { status: 503, headers }); }
}
