// Probe 1: escalation through role writes. Needs s0 first.
import fs from 'node:fs';
import { call, expect, short, etag, failures } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
const F = JSON.parse(fs.readFileSync(`${out}/fixtures.json`, 'utf8'));
const id = (k) => F.R[k].id;
const sys = (await call('GET', '/api/roles', T.admin)).json.filter((r) => r.isSystem);
const S = Object.fromEntries(sys.map((r) => [r.code, r]));
console.log('system roles:', Object.keys(S).join(','));

console.log('== delegate (roles.manage, users.manage, no settings/audit/valuation)');
expect('create with permission beyond own (settings.manage)', await call('POST', '/api/roles', T.dele, { name: 'Esc One', permissions: ['settings.manage'] }), 403);
expect('create with beyond own (cost.view+valuation.view)', await call('POST', '/api/roles', T.dele, { name: 'Esc Two', permissions: ['cost.view', 'valuation.view'] }), 403);
expect('create copy of Big (holds more)', await call('POST', '/api/roles', T.dele, { name: 'Esc Copy', copyFromRoleId: id('big') }), 403);
expect('create copy of Admin system role', await call('POST', '/api/roles', T.dele, { name: 'Esc Copy2', copyFromRoleId: S.Admin.id }), 403);
expect('create copy of Sub (subset)', await call('POST', '/api/roles', T.dele, { name: 'Ok Copy', copyFromRoleId: id('sub') }), 200);
expect('create within own', await call('POST', '/api/roles', T.dele, { name: 'Ok Own', permissions: ['stock.read', 'cost.view'] }), 200);
expect('create blank', await call('POST', '/api/roles', T.dele, { name: 'Ok Blank' }), 200);
expect('create empty list', await call('POST', '/api/roles', T.dele, { name: 'Ok Empty', permissions: [] }), 200);
console.log('-- edit roles that hold more');
for (const k of ['big', 'holds']) {
  const r0 = await call('GET', '/api/roles/' + id(k), T.dele);
  expect(`GET ${k}`, r0, 200);
  const tag = etag(r0);
  expect(`PUT text ${k}`, await call('PUT', '/api/roles/' + id(k), T.dele, { name: 'Renamed', description: 'x', isActive: true }), 403);
  expect(`PUT perms ${k} (same set)`, await call('PUT', `/api/roles/${id(k)}/permissions`, T.dele, { permissions: F.R[k].permissions }, { 'If-Match': tag }), 403);
  expect(`PUT perms ${k} (shrink to own)`, await call('PUT', `/api/roles/${id(k)}/permissions`, T.dele, { permissions: ['stock.read'] }, { 'If-Match': tag }), 403);
  expect(`PUT deactivate ${k}`, await call('PUT', '/api/roles/' + id(k), T.dele, { name: F.R[k].name, isActive: false }), 403);
  expect(`DELETE ${k}`, await call('DELETE', '/api/roles/' + id(k), T.dele), 403);
}
console.log('-- own role');
{
  const own = id('delegate'); const tag = etag(await call('GET', '/api/roles/' + own, T.dele));
  expect('PUT text own', await call('PUT', '/api/roles/' + own, T.dele, { name: 'Delegate', description: 'mine', isActive: true }), 409);
  expect('PUT perms own (same)', await call('PUT', `/api/roles/${own}/permissions`, T.dele, { permissions: F.R.delegate.permissions }, { 'If-Match': tag }), 409);
  expect('DELETE own', await call('DELETE', '/api/roles/' + own, T.dele), 409);
}
console.log('-- system roles as delegate');
for (const c of Object.keys(S)) {
  const sid = S[c].id; const tag = etag(await call('GET', '/api/roles/' + sid, T.dele));
  expect(`${c}: PUT text`, await call('PUT', '/api/roles/' + sid, T.dele, { name: S[c].name, isActive: true }), 403, 409);
  expect(`${c}: PUT perms`, await call('PUT', `/api/roles/${sid}/permissions`, T.dele, { permissions: ['stock.read'] }, { 'If-Match': tag }), 403, 409);
  expect(`${c}: DELETE`, await call('DELETE', '/api/roles/' + sid, T.dele), 403, 409);
}
console.log('-- edit a subset role (allowed) and grant beyond');
{
  const sid = id('sub'); const r = await call('GET', '/api/roles/' + sid, T.dele); const tag = etag(r);
  expect('add held permission to Sub', await call('PUT', `/api/roles/${sid}/permissions`, T.dele, { permissions: ['stock.read', 'cost.view', 'masterdata.read'] }, { 'If-Match': tag }), 200);
  const tag2 = etag(await call('GET', '/api/roles/' + sid, T.dele));
  expect('add NOT held (valuation.view)', await call('PUT', `/api/roles/${sid}/permissions`, T.dele, { permissions: ['stock.read', 'cost.view', 'valuation.view'] }, { 'If-Match': tag2 }), 403);
  expect('add NOT held (settings.manage)', await call('PUT', `/api/roles/${sid}/permissions`, T.dele, { permissions: ['settings.manage'] }, { 'If-Match': tag2 }), 403);
  expect('rename subset role', await call('PUT', '/api/roles/' + sid, T.dele, { name: 'Sub Of Delegate', description: 'ok', isActive: true }), 200);
}
console.log('== narrow delegate (roles.manage only)');
expect('narrow: create with users.manage', await call('POST', '/api/roles', T.narrow, { name: 'N Esc', permissions: ['users.manage', 'users.read'] }), 403);
expect('narrow: edit Delegate role (holds more)', await call('PUT', '/api/roles/' + id('delegate'), T.narrow, { name: 'Delegate', isActive: true }), 403);
expect('narrow: edit Sub (cost.view not held)', await call('PUT', '/api/roles/' + id('sub'), T.narrow, { name: 'Sub Of Delegate', isActive: true }), 403);
expect('narrow: delete Sub', await call('DELETE', '/api/roles/' + id('sub'), T.narrow), 403);
expect('narrow: create within own', await call('POST', '/api/roles', T.narrow, { name: 'N Ok', permissions: ['stock.read'] }), 200);
console.log('== assign a role to self via user save');
{
  const me = F.U.delegate; const base = { username: me.username, email: me.email, displayName: me.displayName, isActive: true, warehouseIds: [F.wh1] };
  expect('dele -> self Big', await call('PUT', '/api/users/' + me.id, T.dele, { ...base, role: 'Big' }), 403);
  expect('dele -> self Admin', await call('PUT', '/api/users/' + me.id, T.dele, { ...base, role: 'Admin' }), 403);
  expect('dele -> self allWarehouses', await call('PUT', '/api/users/' + me.id, T.dele, { ...base, role: 'Delegate', allWarehouses: true }), 403);
  expect('dele edits bigu (holds more)', await call('PUT', '/api/users/' + F.U.big.id, T.dele, { username: 'bigu', email: 'bigu@ims.local', displayName: 'x', role: 'SubOfDelegate', isActive: true, warehouseIds: [F.wh1] }), 403);
}
console.log('== ladder: dele edits subset role to hold roles.manage, then assigns it to self (no gain expected)');
{
  const sid = id('sub'); const tag = etag(await call('GET', '/api/roles/' + sid, T.dele));
  console.log('   edit', short(await call('PUT', `/api/roles/${sid}/permissions`, T.dele, { permissions: ['stock.read', 'cost.view', 'roles.manage', 'roles.read'] }, { 'If-Match': tag })));
  const me = F.U.delegate; const r = await call('PUT', '/api/users/' + me.id, T.dele, { username: me.username, email: me.email, displayName: me.displayName, isActive: true, warehouseIds: [F.wh1], role: 'SubOfDelegate' });
  console.log('   dele -> self SubOfDelegate:', short(r));
}
console.log('failures:', failures());
