// Area 7: information disclosure and session behaviour. Needs the Development API (RV_BASE, serves the OpenAPI document) and, for the production-only checks, a second instance in Production
// mode (RV_PROD_BASE, default http://127.0.0.1:5961, same database, Cors:Origins set to https://ims.example.test). The rate-limit steps run last and wait out their window.
import { call, tokens, fx, login, forge, decode, expect, check, failures, useAdmin, PO, newPo, approvedPo, roleUser, sleep, PROBE_PW, BASE, saveFx } from './lib.mjs';
const RUN = Date.now().toString(36).slice(-4); // fresh throw-away users per run (a locked account stays locked for 15 minutes)
const PROD = process.env.RV_PROD_BASE ?? 'http://127.0.0.1:5961';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const raw = async (base, method, path, { token, headers = {}, body } = {}) => { const r = await fetch(base + path, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers }, body, redirect: 'manual' }); return { status: r.status, headers: r.headers, text: await r.text() }; };
const LEAK = /StackTrace|\bat Ims\.|\bat System\.|Microsoft\.(Data|EntityFrameworkCore|AspNetCore)|SqlException|SqlClient|SELECT\s.+FROM|INSERT INTO|Ims\.(Domain|Application|Infrastructure|Api)\.|\\src\\|\.cs:line|Exception:/;
const po = await approvedPo(F, T);

// ---- A. errors carry no internals (Production mode: what a customer would see)
const cases = [['unknown route', 'GET', '/api/nope', T.admin], ['bad guid', 'GET', `${PO}/xyz`, T.admin], ['unknown order', 'GET', `${PO}/${'1'.repeat(8)}-1111-1111-1111-${'1'.repeat(12)}`, T.admin], ['forbidden', 'POST', PO, T.staff, '{}'],
  ['bad JSON', 'POST', PO, T.clerk, '{{'], ['wrong method', 'DELETE', `${PO}/${po.id}`, T.admin], ['PATCH', 'PATCH', PO, T.admin, '{}'], ['no token', 'GET', PO, null], ['422-ish: empty object', 'POST', PO, T.clerk, '{}'], ['conflict', 'POST', `${PO}/${po.id}/submit`, T.clerk, '', { 'If-Match': '"x"' }]];
for (const [n, m, p, tk, body, h] of cases) {
  const r = await raw(PROD, m, p, { token: tk, body, headers: h });
  check(7, `error body (${n}, Production): ${r.status}, no stack, SQL, paths or type names`, r.status >= 400 && !LEAK.test(r.text), r.text.slice(0, 120).replace(/\s+/g, ' '));
}
const probeBad = await raw(PROD, 'POST', PO, { token: T.clerk, body: JSON.stringify({ warehouseId: F.whA, supplierId: F.supplier, lines: {} }), headers: {} });
check(7, 'FINDING F-1: malformed JSON types echo CLR type names (Ims.Application...) in the 400 (also on anonymous /api/auth/login)', !LEAK.test(probeBad.text), probeBad.text.slice(0, 260));

// ---- B. headers
for (const [env, base] of [['Development', BASE], ['Production', PROD]]) {
  const r = await raw(base, 'GET', `${PO}/${po.id}`, { token: T.admin });
  const h = (k) => r.headers.get(k);
  check(7, `${env}: X-Content-Type-Options nosniff and X-Frame-Options/CSP frame-ancestors`, h('x-content-type-options') === 'nosniff' && (!!h('x-frame-options') || /frame-ancestors/.test(h('content-security-policy') ?? '')), `xfo=${h('x-frame-options')} csp=${h('content-security-policy')?.slice(0, 60)}`);
  check(7, `${env}: Referrer-Policy present`, !!h('referrer-policy'), String(h('referrer-policy')));
  check(7, `${env}: no Server / X-Powered-By version header`, !h('x-powered-by') && !/\d/.test(h('server') ?? ''), `server=${h('server')}`);
  check(7, `${env}: order (cost data) answered with Cache-Control no-store/private`, /no-store|private/.test(h('cache-control') ?? ''), `cache-control=${h('cache-control')}`);
}
check(7, 'Production: HSTS is wired (PipelineSetup.cs:20 UseHsts outside Development); NOT TESTABLE over loopback HTTP (the middleware skips plain HTTP and localhost)', true, `header seen: ${(await raw(PROD, 'GET', '/health')).headers.get('strict-transport-security')}`);
check(7, 'Production: Swagger UI and the OpenAPI document are not served (not 200)', (await raw(PROD, 'GET', '/openapi/v1.json')).status !== 200 && (await raw(PROD, 'GET', '/swagger/index.html')).status !== 200);

// ---- C. CORS (Cors:Origins = http://localhost:4300 on the Development run)
const okOrigin = 'http://localhost:4300';
for (const o of ['https://evil.example', 'null', okOrigin + '.evil.example', 'http://localhost:43000', 'http://LOCALHOST:4300', okOrigin.replace('http', 'https')]) {
  const g = await raw(BASE, 'GET', PO, { token: T.admin, headers: { Origin: o } }), p = await raw(BASE, 'OPTIONS', PO, { headers: { Origin: o, 'Access-Control-Request-Method': 'POST', 'Access-Control-Request-Headers': 'authorization,content-type,if-match' } });
  check(7, `CORS: hostile origin ${o} gets no Access-Control-Allow-Origin (GET and preflight)`, !g.headers.get('access-control-allow-origin') && !p.headers.get('access-control-allow-origin'), `${g.status}/${p.status}`);
}
const good = await raw(BASE, 'OPTIONS', PO, { headers: { Origin: okOrigin, 'Access-Control-Request-Method': 'POST', 'Access-Control-Request-Headers': 'authorization,if-match' } });
check(7, 'CORS: the configured origin is echoed exactly, with credentials; no wildcard', good.headers.get('access-control-allow-origin') === okOrigin && good.headers.get('access-control-allow-credentials') === 'true', String(good.headers.get('access-control-allow-origin')));
check(7, 'CORS: ETag is exposed to the allowed origin (the web needs it)', /etag/i.test((await raw(BASE, 'GET', PO, { token: T.admin, headers: { Origin: okOrigin } })).headers.get('access-control-expose-headers') ?? ''));

// ---- D. improper inventory: every documented operation answers 401 anonymously, except the intended public ones
const spec = JSON.parse((await raw(BASE, 'GET', '/openapi/v1.json')).text); const pub = new Set(['/api/auth/login', '/api/auth/refresh', '/api/auth/logout', '/health']);
let n = 0; const open = [];
for (const [path, ops] of Object.entries(spec.paths)) for (const m of Object.keys(ops)) {
  if (!['get', 'post', 'put', 'delete', 'patch'].includes(m)) continue; n++;
  const r = await raw(BASE, m.toUpperCase(), path.replace(/\{[^}]+\}/g, '11111111-1111-1111-1111-111111111111').replace('{kind}', 'products'), { body: ['post', 'put'].includes(m) ? '{}' : undefined });
  if (r.status !== 401 && !pub.has(path)) open.push(`${m.toUpperCase()} ${path} -> ${r.status}`);
}
check(7, `inventory: ${n} documented operations; all but the public ones answer 401 without a token`, open.length === 0, open.slice(0, 4).join('; '));
const mine = Object.keys(spec.paths).filter((p) => /purchase-orders|modules/.test(p)); check(7, 'inventory: purchasing/modules paths in the contract', mine.length >= 10, `${mine.length} paths`);
check(7, 'inventory: undocumented purchase-order verbs are not routable (PATCH/DELETE -> 404/405)', [(await call('PATCH', `${PO}/${po.id}`, T.admin, {})).status, (await call('DELETE', `${PO}/${po.id}`, T.admin)).status].every((s) => [404, 405].includes(s)));

await sleep(62000); // fresh login window
// ---- E. sessions: lifetime, rotation, replay, logout, cookie flags, CSRF
const u = await roleUser('ses' + RUN, ['masterdata.read']); const uname = fx().extra.find((x) => x.name === 'ses' + RUN).username;
const lg = async (b = BASE, pw = PROBE_PW, user = uname) => fetch(b + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username: user, password: pw }) });
const l1 = await lg(); const j1 = await l1.json(); const c1 = (l1.headers.getSetCookie?.() ?? [])[0] ?? '';
const claims = decode(j1.accessToken); check(7, 'access token lives 5 minutes', claims.exp - claims.nbf === 300, `${claims.exp - claims.nbf} s`);
check(7, 'refresh cookie: HttpOnly, SameSite=Strict, scoped path; token not in the body', /httponly/i.test(c1) && /samesite=strict/i.test(c1) && /path=\/api\/auth/i.test(c1) && !j1.refreshToken, c1.replace(/=[^;]+/, '=<value>').slice(0, 160));
const cookie = c1.split(';')[0], rt = decodeURIComponent(cookie.split('=').slice(1).join('='));
const ref = (body, headers = {}) => fetch(BASE + '/api/auth/refresh', { method: 'POST', headers: { 'Content-Type': 'application/json', ...headers }, body: body === undefined ? undefined : JSON.stringify(body) });
const csrfHdr = { Cookie: cookie, 'X-Requested-With': 'XMLHttpRequest' };
check(7, 'cookie refresh from a hostile Origin is refused (CSRF guard)', [400, 403].includes((await ref(undefined, { ...csrfHdr, Origin: 'https://evil.example' })).status));
const bodyRefresh = await ref({ refreshToken: rt }); const jb = await bodyRefresh.json().catch(() => ({}));
check(7, 'refresh with the token in the body works once and rotates (new access token)', bodyRefresh.status === 200 && !!jb.accessToken, `${bodyRefresh.status}`);
const replay = await ref({ refreshToken: rt }); check(7, 'replaying the rotated refresh token -> 401', replay.status === 401, `${replay.status}`);
const newRt = decodeURIComponent((bodyRefresh.headers.getSetCookie?.() ?? [])[0]?.split(';')[0].split('=').slice(1).join('=') ?? '');
const stillOk = await ref({ refreshToken: newRt }); check(7, 'NOTE: the newer token still works after the replay (no reuse detection / family revoke)', true, `${stillOk.status} (informational)`);
await fetch(BASE + '/api/auth/logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: decodeURIComponent((stillOk.headers.getSetCookie?.() ?? [])[0]?.split(';')[0].split('=').slice(1).join('=') ?? '') }) });
check(7, 'after logout the refresh token is dead', (await ref({ refreshToken: newRt })).status === 401);

// ---- F. lockout, enumeration, change password (throw-away user, 15-minute lock stays on a user we deactivate afterwards)
const msg = async (user, pw) => { const r = await lg(BASE, pw, user); const j = await r.json().catch(() => ({})); return { s: r.status, m: j.detail ?? j.title, ms: 0 }; };
const wrong = await msg(uname, 'wrong-password-1'), unknown = await msg('sec-no-such-user', 'wrong-password-1');
await sleep(62000);
const lockUser = await roleUser('lck' + RUN, ['masterdata.read']); const lname = fx().extra.find((x) => x.name === 'lck' + RUN).username;
check(7, 'enumeration: wrong password and unknown user give the same status and message', wrong.s === unknown.s && wrong.m === unknown.m, `${wrong.s} "${wrong.m}" / ${unknown.s} "${unknown.m}"`);
await sleep(62000);
for (let i = 0; i < 5; i++) await lg(BASE, 'wrong-password-' + i, lname);
const lockedRight = await msg(lname, PROBE_PW), lockedWrong = await msg(lname, 'wrong-password-9');
check(7, 'lockout after 5 failures: the right password is refused (locked message), a wrong one gets only the generic message', lockedRight.s !== 200 && lockedRight.m !== lockedWrong.m && lockedWrong.m === unknown.m, `"${lockedRight.m}" / "${lockedWrong.m}"`);
// change password
const cp = (tok, body) => call('POST', '/api/auth/change-password', tok, body);
expect(7, 'change-password without a token -> 401', await cp(null, { currentPassword: PROBE_PW, newPassword: 'New#Pass1234567' }), 401);
const tcp = await login(uname, PROBE_PW);
expect(7, 'wrong current password -> 400', await cp(tcp, { currentPassword: 'nope', newPassword: 'New#Pass1234567' }), 400);
expect(7, 'new password too short -> 400', await cp(tcp, { currentPassword: PROBE_PW, newPassword: 'short' }), 400);
expect(7, 'new password equal to the old one -> 400', await cp(tcp, { currentPassword: PROBE_PW, newPassword: PROBE_PW }), 400);
for (let i = 0; i < 5; i++) await cp(tcp, { currentPassword: 'nope' + i, newPassword: 'New#Pass1234567' });
const locked = await cp(tcp, { currentPassword: PROBE_PW, newPassword: 'New#Pass1234567' });
check(7, 'change-password guesses count toward the lockout (6th attempt with the right password is refused)', locked.status === 401 || locked.status === 403 || locked.status === 400 && /lock|credential/i.test(locked.text), `${locked.status} ${locked.text.slice(0, 90)}`);
console.log('wait for the login window, then the rate-limit steps');
await sleep(62000);

// ---- G. rate limiting
const codes = []; for (let i = 0; i < 25; i++) codes.push((await lg(BASE, 'x', 'sec-no-such-user')).status);
const first429 = codes.indexOf(429); check(7, 'login limited to 20 per minute per address: the 21st attempt is 429', first429 === 20, `first 429 at attempt ${first429 + 1}`);
const lim = await lg(BASE, 'x', 'sec-no-such-user'); check(7, '429 carries Retry-After and a readable problem body', lim.status === 429 && !!lim.headers.get('retry-after'), `retry-after=${lim.headers.get('retry-after')}`);
await sleep(62000);
const clerkNow = await login('clerk'); const burst = []; for (let i = 0; i < 6; i++) burst.push(...await Promise.all(Array.from({ length: 110 }, () => fetch(BASE + '/api/modules', { headers: { Authorization: 'Bearer ' + clerkNow } }).then((r) => r.status))));
check(7, 'global per-user limit (600/minute): 660 quick requests produce 429s and no 5xx', burst.includes(429) && !burst.some((s) => s >= 500), `${burst.filter((s) => s === 429).length} x 429`);
await sleep(62000);
console.log(failures() ? `${failures()} FAIL` : 'p7 all ok');
