// Probe 4: endpoint authorization, ids, mass assignment, concurrency, audit. Needs s0 + tokens.
import fs from 'node:fs';
import { call, expect, short, etag, failures, ALL } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
const F = JSON.parse(fs.readFileSync(`${out}/fixtures.json`, 'utf8'));
let n = 0; const nm = (s) => `${s} ${Date.now().toString(36)}${++n}`;
const target = (await call('POST', '/api/roles', T.admin, { name: nm('Authz Target'), permissions: ['stock.read'] })).json;
const tag0 = etag(await call('GET', '/api/roles/' + target.id, T.admin));

console.log('== role x endpoint matrix (reads need roles.read, writes need roles.manage)');
const who = { anon: null, staff: T.staff, clerk: T.clerk, supervisor: T.supervisor, owner: T.owner, reader: T.reader, narrow: T.narrow, dele: T.dele, bigu: T.bigu, admin: T.admin };
const canRead = new Set(['reader', 'narrow', 'dele', 'bigu', 'admin']);
const canWrite = new Set(['narrow', 'dele', 'bigu', 'admin']);
const ghost = '11111111-2222-4333-8444-555555555555';
const reads = [['GET /api/permissions', 'GET', '/api/permissions'], ['GET /api/roles', 'GET', '/api/roles'], ['GET /api/roles/{id}', 'GET', '/api/roles/' + target.id]];
for (const [label, m, p] of reads) for (const [w, t] of Object.entries(who)) {
  const r = await call(m, p, t);
  const want = w === 'anon' ? 401 : canRead.has(w) ? 200 : 403;
  expect(`${w.padEnd(10)} ${label}`, r, want);
}
// writes against a ghost id so that nothing changes when the caller is allowed (404 means authorized and not found)
const writes = [
  ['POST /api/roles (invalid body)', 'POST', '/api/roles', {}], ['PUT /api/roles/{ghost}', 'PUT', '/api/roles/' + ghost, { name: 'Zz', isActive: true }],
  ['PUT /api/roles/{ghost}/permissions', 'PUT', `/api/roles/${ghost}/permissions`, { permissions: [] }], ['DELETE /api/roles/{ghost}', 'DELETE', '/api/roles/' + ghost, undefined]
];
for (const [label, m, p, b] of writes) for (const [w, t] of Object.entries(who)) {
  const r = await call(m, p, t, b, { 'If-Match': '*' });
  const want = w === 'anon' ? [401] : canWrite.has(w) ? [400, 404] : [403];
  expect(`${w.padEnd(10)} ${label}`, r, ...want);
}

console.log('== ids by public GUID only');
for (const p of ['1', '0', '-1', 'abc', '%00', 'null', target.id + 'x', '00000000-0000-0000-0000-000000000000', 'Admin', 'Supervisor']) {
  expect(`GET /api/roles/${p}`, await call('GET', '/api/roles/' + p, T.admin), 404);
}
expect('GET uppercase guid', await call('GET', '/api/roles/' + target.id.toUpperCase(), T.admin), 200);
expect('GET braces guid', await call('GET', '/api/roles/%7B' + target.id + '%7D', T.admin), 200, 404);
expect('PUT /api/roles/1', await call('PUT', '/api/roles/1', T.admin, { name: 'Zz', isActive: true }), 404, 405);
expect('DELETE /api/roles/Supervisor', await call('DELETE', '/api/roles/Supervisor', T.admin), 404, 405);
const rr = await call('GET', '/api/roles', T.admin);
console.log('   role DTO fields:', Object.keys(rr.json[0]).join(','));
console.log('   list ids look like guids:', rr.json.every((x) => /^[0-9a-f-]{36}$/.test(x.id)));

console.log('== mass assignment');
let r = expect('POST with extra properties', await call('POST', '/api/roles', T.admin, { name: nm('Mass'), permissions: ['stock.read'], isSystem: true, id: ghost, code: 'Admin', isActive: false, createdBy: 'evil', recDeleted: true, userCount: 99, rowVersion: 'AAAA', concurrencyToken: 'x', publicId: ghost, grants: ['settings.manage'] }), 200);
console.log('   result: isSystem=', r.json.isSystem, 'isActive=', r.json.isActive, 'code=', r.json.code, 'id==ghost:', r.json.id === ghost, 'userCount=', r.json.userCount, 'perms=', JSON.stringify(r.json.permissions));
const mass = r.json;
r = expect('PUT text with extra properties', await call('PUT', '/api/roles/' + mass.id, T.admin, { name: nm('Mass2'), description: 'd', isActive: true, isSystem: true, code: 'Hacked', id: ghost, permissions: ALL, createdBy: 'x' }), 200);
console.log('   result: isSystem=', r.json.isSystem, 'code=', r.json.code, 'perms=', JSON.stringify(r.json.permissions));
r = expect('PUT perms with extra properties', await call('PUT', `/api/roles/${mass.id}/permissions`, T.admin, { permissions: ['stock.read', 'cost.view'], isSystem: true, code: 'Hacked2', name: 'Hacked' }, { 'If-Match': '*' }), 200);
console.log('   result: isSystem=', r.json.role.isSystem, 'code=', r.json.role.code, 'name=', r.json.role.name);
expect('PUT text: isActive missing (bool default false)', await call('PUT', '/api/roles/' + mass.id, T.admin, { name: nm('Mass3') }), 200, 400);
const afterMissing = (await call('GET', '/api/roles/' + mass.id, T.admin)).json;
console.log('   after a PUT without isActive: isActive =', afterMissing.isActive, '(role has no users)');
expect('PUT text: description omitted clears it?', await call('PUT', '/api/roles/' + mass.id, T.admin, { name: afterMissing.name, isActive: true }), 200);
console.log('   description after omit:', JSON.stringify((await call('GET', '/api/roles/' + mass.id, T.admin)).json.description), '(was "d")');

console.log('== concurrency tokens');
{
  const g = await call('GET', '/api/roles/' + target.id, T.admin); const e1 = etag(g);
  console.log('   token shape:', e1, 'len', e1.length, '| body token equals ETag:', `"${g.json.concurrencyToken}"` === e1);
  const a = await call('PUT', '/api/roles/' + target.id, T.admin, { name: g.json.name, description: 'v2', isActive: true }, { 'If-Match': e1 }); expect('PUT text with current If-Match', a, 200);
  expect('PUT text with the now-stale If-Match', await call('PUT', '/api/roles/' + target.id, T.admin, { name: g.json.name, description: 'v3', isActive: true }, { 'If-Match': e1 }), 409);
  expect('PUT text with no If-Match (optional): lost update possible', await call('PUT', '/api/roles/' + target.id, T.admin, { name: g.json.name, description: 'v4', isActive: true }), 200);
  const e = etag(await call('GET', '/api/roles/' + target.id, T.admin));
  for (const [label, h] of [['garbage', '"zzz"'], ['empty quotes', '""'], ['other role token', tag0 && etag(await call('GET', '/api/roles/' + mass.id, T.admin))], ['weak current', 'W/' + e], ['unquoted current', e.replaceAll('"', '')], ['current + garbage list', e + ', "zzz"'], ['star', '*']]) {
    const rr2 = await call('PUT', `/api/roles/${target.id}/permissions`, T.admin, { permissions: ['stock.read'] }, { 'If-Match': h });
    console.log(`   perms If-Match ${label.padEnd(24)} -> ${short(rr2)}`);
  }
  expect('PUT perms without If-Match -> 428', await call('PUT', `/api/roles/${target.id}/permissions`, T.admin, { permissions: ['stock.read'] }), 428);
}
{
  console.log('-- parallel replace with the same ETag (10 rounds): expect one 200 and one 409, never 500');
  const o = {};
  for (let i = 0; i < 10; i++) {
    const role = (await call('POST', '/api/roles', T.admin, { name: nm('Par'), permissions: ['stock.read'] })).json;
    const e = etag(await call('GET', '/api/roles/' + role.id, T.admin));
    const [x, y] = await Promise.all([
      call('PUT', `/api/roles/${role.id}/permissions`, T.admin, { permissions: ['stock.read', 'cost.view'] }, { 'If-Match': e }),
      call('PUT', `/api/roles/${role.id}/permissions`, T.admin, { permissions: ['stock.read', 'reports.read'] }, { 'If-Match': e })
    ]);
    const k = [x.status, y.status].sort().join('+'); o[k] = (o[k] ?? 0) + 1;
    const fin = (await call('GET', '/api/roles/' + role.id, T.admin)).json.permissions;
    if (x.status === 200 && y.status === 200) console.log('   BOTH 200 (lost update):', JSON.stringify(fin));
  }
  console.log('   outcomes', JSON.stringify(o));
  const o2 = {};
  for (let i = 0; i < 10; i++) {
    const role = (await call('POST', '/api/roles', T.admin, { name: nm('Par2'), permissions: ['stock.read'] })).json;
    const [x, y] = await Promise.all([
      call('PUT', `/api/roles/${role.id}/permissions`, T.admin, { permissions: ['stock.read', 'cost.view'] }, { 'If-Match': '*' }),
      call('PUT', `/api/roles/${role.id}/permissions`, T.admin, { permissions: ['stock.read', 'cost.view'] }, { 'If-Match': '*' })
    ]);
    const k = [x.status, y.status].sort().join('+'); o2[k] = (o2[k] ?? 0) + 1;
  }
  console.log('   same grant added twice at once (star): outcomes', JSON.stringify(o2));
  const o3 = {};
  for (let i = 0; i < 10; i++) {
    const nme = nm('Par3');
    const [x, y] = await Promise.all([call('POST', '/api/roles', T.admin, { name: nme }), call('POST', '/api/roles', T.admin, { name: nme })]);
    const k = [x.status, y.status].sort().join('+'); o3[k] = (o3[k] ?? 0) + 1;
  }
  console.log('   same role name created twice at once: outcomes', JSON.stringify(o3));
  const o4 = {};
  for (let i = 0; i < 10; i++) {
    const [x, y] = await Promise.all([call('POST', '/api/roles', T.admin, { name: nm('Par4') + ' a' }), call('POST', '/api/roles', T.admin, { name: nm('Par4') + ' a'.replace('a', 'a') })]);
    void x; void y;
  }
  const o5 = {};
  for (let i = 0; i < 10; i++) {
    const base = nm('Par5').replace(/\d+$/, '');
    const [x, y] = await Promise.all([call('POST', '/api/roles', T.admin, { name: base + ' one' }), call('POST', '/api/roles', T.admin, { name: base + '  one' })]);
    const k = [x.status, y.status].sort().join('+'); o5[k] = (o5[k] ?? 0) + 1;
  }
  console.log('   names that make the same code at once: outcomes', JSON.stringify(o5));
}

console.log('== audit');
{
  const a = await call('GET', '/api/audit?pageSize=200&entityType=Role', T.admin);
  const items = a.json.items ?? [];
  const byAction = {};
  for (const i of items) byAction[i.action] = (byAction[i.action] ?? 0) + 1;
  console.log('   audit rows for entity Role:', items.length, JSON.stringify(byAction));
  const ex = (act) => items.find((i) => i.action === act);
  for (const act of ['role.created', 'role.updated', 'role.permissions-changed', 'role.deleted']) {
    const x = ex(act); console.log(`   ${act}:`, x ? JSON.stringify({ actor: x.actor ?? x.actorName ?? x.user, entityId: x.entityId, before: x.before ?? x.oldValues, after: x.after ?? x.newValues }).slice(0, 400) : 'NONE');
  }
  const blob = JSON.stringify(items);
  console.log('   audit text mentions password/token/hash:', /password|passwordhash|eyJ|refresh/i.test(blob));
}
console.log('failures:', failures());
