// PR 134 p2: info leak, mass assignment, exact role code, targets not assignable.
import fs from 'node:fs';
import { call, expect, failures, short, PROBE_PW, sfx, decode } from './lib.mjs';
const F = JSON.parse(fs.readFileSync(`${process.env.RV_OUT ?? '.'}/fixtures.json`));
const { T, wh1, R, U } = F;
const nu = () => `q${sfx}${Math.random().toString(36).slice(2, 7)}`;
const mk = (role, extra = {}) => { const u = nu(); return { username: u, email: `${u}@ims.local`, displayName: u, role, isActive: true, password: PROBE_PW, warehouseIds: [wh1], allWarehouses: false, ...extra }; };
// (2) users.read-only caller: which role names does the user list reveal?
const ul = await call('GET', '/api/users?pageSize=100', T.uread);
console.log('users.read list', ul.status, 'items', ul.json.items?.length, 'keys', Object.keys(ul.json.items?.[0] ?? {}).join(','));
const names = [...new Set(ul.json.items.map(u => `${u.role}=${u.roleName}`))];
console.log('  (role=roleName) seen by users.read-only caller:', names.join(' | '));
const ulr = await call('GET', '/api/users?pageSize=100', T.ronly); console.log('roles.read only caller on /api/users', ulr.status);
// unassignable names exposed? (compare with GET /api/roles by a roles.read caller: that is the real ACL for role info)
const rr = await call('GET', '/api/roles', T.ronly); console.log('roles.read caller sees roles', rr.status);
// (3) mass assignment + exact code
const dele = T.dele;
let r = await call('POST', '/api/users', dele, { ...mk(R.weak.code), roleName: 'Administrator', roles: ['Admin'], id: '00000000-0000-0000-0000-000000000001', publicId: '00000000-0000-0000-0000-000000000002', isSystem: true, permissions: ['roles.manage'] });
expect('create with extra props (roleName, id, permissions...)', r, 200, 201);
console.log('   created ->', JSON.stringify({ id: r.json.id, role: r.json.role, roleName: r.json.roleName }));
const made = r.json;
for (const [label, code] of [['lowercase', R.strong.code.toLowerCase()], ['upper', R.strong.code.toUpperCase()], ['trailing space', R.weak.code + ' '], ['leading space', ' ' + R.weak.code], ['name instead of code', 'Weak'], ['Admin', 'Admin'], ['Administrator by name', 'Administrator'], ['cyrillic A', 'Аdmin'], ['empty', ''], ['null', null], ['deleted role', R.deleted.code], ['inactive role', R.inactive.code], ['wildcard %', '%'], ['sql', "Weak' OR '1'='1"]]) {
  const rr2 = await call('POST', '/api/users', dele, mk(code));
  expect(`create as delegate with role code [${label}]`, rr2, 400, 403, 404, 409, 422);
}
// case-variant of an assignable code should be refused (ordinal): 'weak' vs code
console.log('weak code is', R.weak.code);
const lc = await call('POST', '/api/users', dele, mk(R.weak.code.toLowerCase()));
console.log('lowercase of assignable code ->', short(lc));
// roleName in body of PUT cannot change name display; role change via roleName only
r = await call('PUT', `/api/users/${made.id}`, dele, { ...mk(R.weak.code, { username: made.username, email: made.email }), roleName: 'Admin' });
console.log('PUT keeping role + roleName=Admin ->', short(r), r.json?.role, r.json?.roleName);
// move to stronger role
r = await call('PUT', `/api/users/${made.id}`, dele, { ...mk(R.strong.code, { username: made.username, email: made.email }) }); expect('PUT to stronger role (not held)', r, 403);
r = await call('PUT', `/api/users/${made.id}`, dele, { ...mk('Admin', { username: made.username, email: made.email }) }); expect('PUT to Admin', r, 403);
// delegate vs targets whose role is not assignable
for (const [n, id] of [['big user', U.big.id], ['admin-core user', U.adminCore.id], ['roles-only user', U.rolesOnly.id]]) {
  const cur = (await call('GET', `/api/users?pageSize=100`, T.admin)).json.items.find(x => x.id === id);
  const keep = { username: cur.username, email: cur.email, displayName: 'changed', role: cur.role, isActive: true, warehouseIds: cur.warehouseIds, allWarehouses: cur.allWarehouses };
  const same = await call('PUT', `/api/users/${id}`, dele, keep);
  console.log(`delegate re-saves ${n} (role ${cur.role}, same role, edit displayName) ->`, short(same));
  const down = await call('PUT', `/api/users/${id}`, dele, { ...keep, role: R.weak.code });
  console.log(`delegate demotes ${n} to Weak ->`, short(down));
}
// anyone can edit a user holding the SAME or a lesser role, including keeping a role that is not in list? (target role inactive can't exist)
// password reset on a not-assignable user
const pr = await call('PUT', `/api/users/${U.big.id}`, dele, { username: 'bigu', email: 'bigu@ims.local', displayName: 'x', role: R.big.code, isActive: true, password: PROBE_PW + 'z', warehouseIds: [], allWarehouses: true });
console.log('delegate password-reset on Big user ->', short(pr));
// UserDto read-only: roleName in PUT cannot rename role
const nm = await call('GET', '/api/roles?includeInactive=true', T.admin); const weak = (Array.isArray(nm.json) ? nm.json : nm.json.items).find(x => x.code === R.weak.code);
console.log('weak role name still', weak.name);
console.log('failures', failures());
