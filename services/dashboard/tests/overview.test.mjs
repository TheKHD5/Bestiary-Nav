import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { Miniflare } from 'miniflare';
import ts from 'typescript';

// Run the actual route in workerd, whose fetch options differ from Node's.
const route = await readFile(new URL('../app/api/overview/route.ts', import.meta.url), 'utf8');
async function exercise({ signedIn = true, redirect = false } = {}) {
  let summaryRequests = 0;
  let redirectedRequests = 0;
  const server = createServer((request, response) => {
    if (request.url === '/api/summary') {
      summaryRequests++;
      assert.equal(request.headers.authorization, 'Bearer test-only-key');
      if (redirect) { response.writeHead(302, { Location: '/unexpected' }); response.end(); }
      else { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ activeNow: 7 })); }
    } else { redirectedRequests++; response.end('must not follow'); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const source = route.replace("import { getChatGPTUser } from '../../chatgpt-auth';", `async function getChatGPTUser() { return ${signedIn ? '{ id: "test-owner" }' : 'null'}; }`).replaceAll('export ', '');
  const script = ts.transpileModule(`${source}\nexport default { fetch: GET };`, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
  const runtime = new Miniflare({ modules: true, script, compatibilityDate: '2026-05-15', bindings: { COLLECTOR_URL: `http://127.0.0.1:${server.address().port}`, REPORTING_READ_KEY: 'test-only-key' } });
  try {
    const response = await runtime.dispatchFetch('http://localhost/api/overview');
    return { status: response.status, body: await response.json(), summaryRequests, redirectedRequests };
  } finally { try { await runtime.dispose(); } finally { await new Promise(resolve => server.close(resolve)); } }
}

test('Workers runtime serves authenticated reporting data', async () => {
  const result = await exercise();
  assert.equal(result.status, 200);
  assert.deepEqual(result.body, { activeNow: 7 });
  assert.equal(result.summaryRequests, 1);
});
test('upstream redirects fail closed without forwarding the credential', async () => {
  const result = await exercise({ redirect: true });
  assert.equal(result.status, 503);
  assert.equal(result.summaryRequests, 1);
  assert.equal(result.redirectedRequests, 0);
});
test('unauthenticated requests never contact the collector', async () => {
  const result = await exercise({ signedIn: false });
  assert.equal(result.status, 401);
  assert.equal(result.summaryRequests, 0);
});
