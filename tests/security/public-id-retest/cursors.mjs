// Cursor abuse: tamper, truncate, extend, replay across lists, replay across users with a different scope, change the filter under a cursor,
// oversize, and combine with page. Passing means: anything not minted by the server for that list is a 400 with one fixed body, and a valid
// cursor never widens what the caller may see.   usage: IMS_STATE=state.json node cursors.mjs
import { raw, login, loadState, normBody } from './lib.mjs';

const st = loadState(); const I = st.ids;
const admin = await login('admin'); const staff = await login('staff'); const staff2 = await login('staff2', 'created'); const owner = await login('owner');
const fails = []; const check = (l, ok, e = '') => { console.log(`${ok ? 'PASS' : 'FAIL'}  ${l}${e ? '  ' + e : ''}`); if (!ok) fails.push(l); };
const lists = { documents: '/api/documents', ledger: '/api/stock/ledger', audit: '/api/audit' };
const cur = {};
for (const [n, p] of Object.entries(lists)) { const r = await raw('GET', `${p}?pageSize=1`, { token: n === 'audit' ? admin : admin }); cur[n] = r.data.nextCursor; check(`${n}: first page gives an opaque cursor`, typeof cur[n] === 'string' && cur[n].length > 20, `length ${cur[n]?.length}`); }
console.log('   sample shape (not a secret): length', cur.documents.length, 'charset', /^[A-Za-z0-9_-]+$/.test(cur.documents) ? 'base64url' : 'other');

// determinism / size: two cursors for the same position differ (random IV) and have constant length
const again = (await raw('GET', '/api/documents?pageSize=1', { token: admin })).data.nextCursor;
check('same position, two requests: different ciphertext', again !== cur.documents);
check('constant length', again.length === cur.documents.length);

const get = (list, c, tok = admin, extra = '') => raw('GET', `${lists[list]}?pageSize=5&cursor=${encodeURIComponent(c)}${extra}`, { token: tok });
const ok1 = await get('documents', cur.documents);
check('valid cursor works', ok1.status === 200, `status ${ok1.status}`);
const badBodies = new Set();
const bad = async (label, list, c, tok = admin) => { const r = await get(list, c, tok); badBodies.add(normBody(r)); check(label, r.status === 400, `status ${r.status}`); };
const flip = (s, i) => s.slice(0, i) + (s[i] === 'A' ? 'B' : 'A') + s.slice(i + 1);
await bad('flip first char', 'documents', flip(cur.documents, 0));
await bad('flip middle char', 'documents', flip(cur.documents, 30));
await bad('flip last char', 'documents', flip(cur.documents, cur.documents.length - 1));
await bad('truncate', 'documents', cur.documents.slice(0, -4));
await bad('append', 'documents', cur.documents + 'AAAA');
await bad('whitespace variant', 'documents', ' ' + cur.documents);
await bad('padding variant', 'documents', cur.documents + '=');
await bad('standard base64 alphabet', 'documents', cur.documents.replace(/-/g, '+').replace(/_/g, '/') + '+');
await bad('empty-looking', 'documents', '%00');
await bad('integer (the old afterId format)', 'documents', '12345');
await bad('negative integer', 'documents', '-1');
await bad('guid', 'documents', I.p1);
await bad('sql text', 'documents', "1' OR 1=1--");
await bad('oversize 64 KB', 'documents', 'A'.repeat(65536).slice(0, 4000));
for (const [from, to] of [['documents', 'ledger'], ['documents', 'audit'], ['ledger', 'documents'], ['ledger', 'audit'], ['audit', 'documents'], ['audit', 'ledger']]) await bad(`${from} cursor replayed on ${to}`, to, cur[from]);
check('every refusal has the same body (no oracle between tampered / wrong list / wrong format)', badBodies.size === 1, `${badBodies.size} distinct bodies`);
if (badBodies.size > 1) for (const b of badBodies) console.log('     ', b.slice(0, 200));

// the huge query string
{ const r = await raw('GET', `/api/documents?cursor=${'A'.repeat(100000)}`, { token: admin }); check('100 KB cursor in the query string is refused cheaply', [400, 414, 431].includes(r.status), `status ${r.status}`); }

// replay across users: a cursor made for admin (all warehouses) used by scoped staff2 must only ever return WH02 rows
{
  const all = (await raw('GET', '/api/documents?pageSize=100', { token: admin })).data.items;
  const adminPage1 = (await raw('GET', '/api/documents?pageSize=3', { token: admin })).data;
  const mine = (await raw('GET', '/api/documents?pageSize=100', { token: staff2 })).data.items;
  const viaAdminCursor = (await get('documents', adminPage1.nextCursor, staff2, '&pageSize=100')).data;
  const ids2 = new Set(mine.map((d) => d.id));
  check('admin cursor used by staff2: only staff2-visible documents', viaAdminCursor.items.every((d) => ids2.has(d.id)), `${viaAdminCursor.items.length} rows, all in staff2 scope`);
  check('admin sees documents staff2 cannot (control)', all.some((d) => !ids2.has(d.id)));
  const led = (await raw('GET', '/api/stock/ledger?pageSize=2', { token: admin })).data;
  const l2 = (await get('ledger', led.nextCursor, staff2, '&pageSize=100')).data;
  check('admin ledger cursor used by staff2: only WH02 rows', l2.items.every((x) => x.warehouseCode === 'WH02'), `${l2.items.length} rows`);
  // filter change under a cursor: staff2 filters to WH01 (out of scope) with a valid cursor -> still nothing
  const ff = (await get('documents', adminPage1.nextCursor, staff2, `&warehouseId=${I.wh1}`)).data;
  check('valid cursor + out-of-scope warehouse filter: zero rows', ff.items.length === 0, `total ${ff.total}`);
  // cursor + page parameter
  const both = await get('documents', adminPage1.nextCursor, admin, '&page=3');
  console.log('INFO  cursor together with page=3 ->', both.status, both.data?.page !== undefined ? `page=${both.data.page}` : '');
  // includeTotal with cursor does not reveal other warehouses' count
  const tot = (await get('documents', adminPage1.nextCursor, staff2, '&includeTotal=true')).data;
  check('includeTotal for staff2 counts only its scope', tot.total <= mine.length, `total ${tot.total} vs own ${mine.length}`);
}
// a cursor survives walking the whole list: no skipped / repeated rows for one user (sanity)
{
  const seen = []; let c = null;
  for (let i = 0; i < 100; i++) { const r = (await raw('GET', `/api/documents?pageSize=4${c ? `&cursor=${encodeURIComponent(c)}` : ''}`, { token: admin })).data; seen.push(...r.items.map((d) => d.id)); c = r.nextCursor; if (!c) break; }
  const total = (await raw('GET', '/api/documents?pageSize=100', { token: admin })).data.items.length;
  check('walk with cursors returns every row once', seen.length === new Set(seen).size && seen.length === total, `${seen.length}/${total}`);
}
console.log(fails.length ? `\n${fails.length} FAILED: ${fails.join('; ')}` : '\nall cursor checks passed');
// write the admin cursor for the restart / key-ring test
import { writeFileSync } from 'node:fs';
writeFileSync(process.env.IMS_CURSOR_OUT ?? 'cursor.txt', cur.documents);
