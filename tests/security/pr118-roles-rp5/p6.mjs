// Probe 6: staleness (an edit applies at the next refresh), claims after an edit, response headers of the role endpoints. Needs s0 + tokens.
import fs from 'node:fs';
import { call, loginFull, refreshWith, decode, etag, expect, short, PROBE_PW } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
const F = JSON.parse(fs.readFileSync(`${out}/fixtures.json`, 'utf8'));
const rid = F.R.reader.id;

const a = await loginFull('reader', PROBE_PW);
console.log('login perms claim:', JSON.stringify(decode(a.token).perm));
expect('old token reads roles', await call('GET', '/api/roles', a.token), 200);
let tag = etag(await call('GET', '/api/roles/' + rid, T.admin));
expect('admin replaces Reader -> [stock.read]', await call('PUT', `/api/roles/${rid}/permissions`, T.admin, { permissions: ['stock.read'] }, { 'If-Match': tag }), 200);
expect('OLD token still reads roles (window open)', await call('GET', '/api/roles', a.token), 200);
const rf = await refreshWith(a.refresh);
console.log('refresh ->', rf.status, 'new perms claim:', JSON.stringify(decode(rf.json.accessToken).perm), '| userinfo perms:', JSON.stringify(rf.json.user?.permissions));
expect('NEW token cannot read roles', await call('GET', '/api/roles', rf.json.accessToken), 403);
expect('NEW token reads stock', await call('GET', '/api/stock', rf.json.accessToken), 200);
const replay = await refreshWith(a.refresh);
console.log('replay of the used refresh token ->', short(replay));
tag = etag(await call('GET', '/api/roles/' + rid, T.admin));
expect('restore Reader', await call('PUT', `/api/roles/${rid}/permissions`, T.admin, { permissions: ['roles.read'] }, { 'If-Match': tag }), 200);

console.log('== headers');
for (const [m, p] of [['GET', '/api/roles'], ['GET', '/api/roles/' + rid], ['GET', '/api/permissions']]) {
  const r = await call(m, p, T.admin);
  const h = (k) => r.headers.get(k);
  console.log(`   ${m} ${p.slice(0, 30)} -> cache-control=${h('cache-control')} | x-content-type-options=${h('x-content-type-options')} | etag=${h('etag')} | server=${h('server')} | content-type=${h('content-type')}`);
}
const e = await call('POST', '/api/roles', T.admin, '{bad json');
console.log('   malformed json ->', e.status, e.text.slice(0, 160));
const w = await call('POST', '/api/roles', T.admin, JSON.stringify({ name: 'Ctype Probe' }), { 'Content-Type': 'text/plain' });
console.log('   text/plain body ->', w.status);
const o = await fetch(process.env.RV_BASE ?? 'http://127.0.0.1:5318', { method: 'OPTIONS' }).catch(() => null);
void o;
const cors = await fetch((process.env.RV_BASE ?? 'http://127.0.0.1:5318') + '/api/roles', { method: 'OPTIONS', headers: { Origin: 'https://evil.example', 'Access-Control-Request-Method': 'DELETE', 'Access-Control-Request-Headers': 'authorization,if-match' } });
console.log('   CORS preflight from a hostile origin ->', cors.status, 'allow-origin=', cors.headers.get('access-control-allow-origin'));
const cors2 = await fetch((process.env.RV_BASE ?? 'http://127.0.0.1:5318') + '/api/roles', { method: 'OPTIONS', headers: { Origin: 'http://localhost:4200', 'Access-Control-Request-Method': 'PUT', 'Access-Control-Request-Headers': 'authorization,if-match' } });
console.log('   CORS preflight from the allowed origin (PUT with if-match) ->', cors2.status, 'allow-origin=', cors2.headers.get('access-control-allow-origin'), 'allow-headers=', cors2.headers.get('access-control-allow-headers'), 'expose=', cors2.headers.get('access-control-expose-headers'));
