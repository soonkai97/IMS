// Probe 2: delete, in-use refusal, recreate, races with the user save. Needs s0 first.
import fs from 'node:fs';
import { call, expect, short, etag, failures, PROBE_PW, sfx } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
const F = JSON.parse(fs.readFileSync(`${out}/fixtures.json`, 'utf8'));
const id = (k) => F.R[k].id;
const body = (u, role, extra = {}) => ({ username: u.username, email: u.email, displayName: u.displayName, isActive: true, warehouseIds: [F.wh1], allWarehouses: false, role, ...extra });

console.log('== reset dele to Delegate');
expect('reset', await call('PUT', '/api/users/' + F.U.delegate.id, T.admin, body(F.U.delegate, 'Delegate')), 200);

console.log('== in use');
let r = expect('DELETE Reader (user reader holds it)', await call('DELETE', '/api/roles/' + id('reader'), T.admin), 409);
console.log('   message:', r.json.detail);
expect('deactivate Reader', await call('PUT', '/api/roles/' + id('reader'), T.admin, { name: 'Reader', isActive: false }), 409);
expect('dele deletes SubOfDelegate (in use by holder)', await call('DELETE', '/api/roles/' + id('sub'), T.dele), 409);
expect('GET Reader userCount', await call('GET', '/api/roles/' + id('reader'), T.admin), 200);

console.log('== delete then recreate the same name');
r = expect('create Temp One', await call('POST', '/api/roles', T.admin, { name: 'Temp One', permissions: ['stock.read'] }), 200);
const t1 = r.json; console.log('   code', t1.code);
expect('delete Temp One', await call('DELETE', '/api/roles/' + t1.id, T.admin), 204);
expect('GET deleted -> 404', await call('GET', '/api/roles/' + t1.id, T.admin), 404);
expect('PUT deleted -> 404', await call('PUT', '/api/roles/' + t1.id, T.admin, { name: 'Temp One', isActive: true }), 404);
expect('PUT perms deleted -> 404', await call('PUT', `/api/roles/${t1.id}/permissions`, T.admin, { permissions: [] }, { 'If-Match': '*' }), 404);
expect('DELETE deleted again -> 404', await call('DELETE', '/api/roles/' + t1.id, T.admin), 404);
r = expect('recreate Temp One', await call('POST', '/api/roles', T.admin, { name: 'Temp One', permissions: ['stock.read'] }), 200);
console.log('   code', r.json.code, 'new id differs:', r.json.id !== t1.id);
expect('copy from deleted role -> 400', await call('POST', '/api/roles', T.admin, { name: 'Copy Of Gone', copyFromRoleId: t1.id }), 400, 404);

console.log('== revoke then re-grant the same permission (soft-deleted grant row)');
r = await call('POST', '/api/roles', T.admin, { name: 'Regrant', permissions: ['stock.read', 'cost.view'] });
const rg = r.json;
let tag = etag(await call('GET', '/api/roles/' + rg.id, T.admin));
r = expect('replace -> [stock.read]', await call('PUT', `/api/roles/${rg.id}/permissions`, T.admin, { permissions: ['stock.read'] }, { 'If-Match': tag }), 200);
tag = r.headers.get('etag');
r = expect('replace -> [stock.read, cost.view] again', await call('PUT', `/api/roles/${rg.id}/permissions`, T.admin, { permissions: ['stock.read', 'cost.view'] }, { 'If-Match': tag }), 200);
console.log('   permissions now', JSON.stringify(r.json.role?.permissions), 'added', JSON.stringify(r.json.added), 'removed', JSON.stringify(r.json.removed));
tag = r.headers.get('etag');
for (let i = 0; i < 3; i++) {
  r = await call('PUT', `/api/roles/${rg.id}/permissions`, T.admin, { permissions: i % 2 ? ['stock.read', 'cost.view'] : [] }, { 'If-Match': tag });
  tag = r.headers.get('etag'); if (r.status !== 200) console.log('   toggle', i, short(r));
}
console.log('   toggle x3 ok');

console.log('== race: DELETE role vs POST user with that role (15 rounds)');
let bad = 0;
const outcomes = {};
for (let i = 0; i < 15; i++) {
  const name = `Race ${sfx} ${i}`; const code = `Race${sfx}${i}`;
  const rr = (await call('POST', '/api/roles', T.admin, { name, permissions: ['stock.read'] })).json;
  const uname = `race${sfx}${i}`;
  const [d, u] = await Promise.all([
    call('DELETE', '/api/roles/' + rr.id, T.admin),
    call('POST', '/api/users', T.admin, { username: uname, email: `${uname}@ims.local`, displayName: uname, role: rr.code, isActive: true, password: PROBE_PW, warehouseIds: [F.wh1], allWarehouses: false })
  ]);
  const key = `delete ${d.status} / create-user ${u.status}`; outcomes[key] = (outcomes[key] ?? 0) + 1;
  const stillThere = (await call('GET', '/api/roles/' + rr.id, T.admin)).status === 200;
  if (u.status === 200 && !stillThere) { bad++; console.log('   ORPHAN: user created but role deleted', rr.code); }
  if (d.status === 204 && u.status === 200) { bad++; console.log('   BOTH SUCCEEDED', rr.code); }
}
console.log('   outcomes', JSON.stringify(outcomes), 'inconsistent:', bad);

console.log('== race: deactivate role vs POST user (15 rounds)');
bad = 0; const o2 = {};
for (let i = 0; i < 15; i++) {
  const name = `Deact ${sfx} ${i}`;
  const rr = (await call('POST', '/api/roles', T.admin, { name, permissions: ['stock.read'] })).json;
  const uname = `deact${sfx}${i}`;
  const [d, u] = await Promise.all([
    call('PUT', '/api/roles/' + rr.id, T.admin, { name, isActive: false }),
    call('POST', '/api/users', T.admin, { username: uname, email: `${uname}@ims.local`, displayName: uname, role: rr.code, isActive: true, password: PROBE_PW, warehouseIds: [F.wh1], allWarehouses: false })
  ]);
  const key = `deactivate ${d.status} / create-user ${u.status}`; o2[key] = (o2[key] ?? 0) + 1;
  const now = (await call('GET', '/api/roles/' + rr.id + '', T.admin)).json;
  if (u.status === 200 && now.isActive === false) { bad++; console.log('   USER OF INACTIVE ROLE', rr.code); }
}
console.log('   outcomes', JSON.stringify(o2), 'inconsistent:', bad);

console.log('== race: DELETE role vs PUT user (move an existing user to it)');
bad = 0; const o3 = {};
for (let i = 0; i < 10; i++) {
  const name = `Mv ${sfx} ${i}`;
  const rr = (await call('POST', '/api/roles', T.admin, { name, permissions: ['stock.read'] })).json;
  const [d, u] = await Promise.all([
    call('DELETE', '/api/roles/' + rr.id, T.admin),
    call('PUT', '/api/users/' + F.U.holder.id, T.admin, body(F.U.holder, rr.code))
  ]);
  const key = `delete ${d.status} / move-user ${u.status}`; o3[key] = (o3[key] ?? 0) + 1;
  const stillThere = (await call('GET', '/api/roles/' + rr.id, T.admin)).status === 200;
  if (u.status === 200 && !stillThere) { bad++; console.log('   ORPHAN after move', rr.code); }
  await call('PUT', '/api/users/' + F.U.holder.id, T.admin, body(F.U.holder, 'SubOfDelegate'));
}
console.log('   outcomes', JSON.stringify(o3), 'inconsistent:', bad);
console.log('failures:', failures());
