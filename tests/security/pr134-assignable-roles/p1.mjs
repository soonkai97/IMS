// PR 134 p1: authorization matrix + list == what the user save accepts, per caller.
import fs from 'node:fs';
import { call, expect, failures, short, PROBE_PW, sfx } from './lib.mjs';
const F = JSON.parse(fs.readFileSync(`${process.env.RV_OUT ?? '.'}/fixtures.json`));
const { T, wh1 } = F;
const AR = '/api/users/assignable-roles';
// anonymous / bad tokens
expect('anonymous', await call('GET', AR), 401);
expect('garbage token', await call('GET', AR, 'abc.def.ghi'), 401);
for (const who of ['staff','clerk','supervisor','owner','ronly','uread']) expect(`${who} (no users.manage)`, await call('GET', AR, T[who]), 403);
for (const who of ['admin','dele','deleall','admcore','bigu']) expect(`${who}`, await call('GET', AR, T[who]), 200);
// methods / siblings
for (const m of ['POST','PUT','DELETE','PATCH']) expect(`${m} on route`, await call(m, AR, T.admin, {}), 404, 405);
expect('trailing slash', await call('GET', AR + '/', T.dele), 200, 404);
expect('route not captured as id for users.read-only', await call('GET', AR, T.uread), 403);
expect('query junk ignored', await call('GET', AR + '?includeInactive=true&search=%27', T.dele), 200);
const body = (r) => r.json;
// Full role universe from admin (incl inactive)
const all = (await call('GET', '/api/roles?includeInactive=true', T.admin)).json; const allRoles = Array.isArray(all) ? all : all.items;
console.log('roles in db (non deleted)', allRoles.length);
// shape
const sample = (await call('GET', AR, T.dele)).json;
console.log('shape keys', [...new Set(sample.flatMap(Object.keys))].join(','));
for (const who of ['dele','deleall','admcore','bigu','admin']) {
  const list = (await call('GET', AR, T[who])).json; const listed = new Set(list.map(r => r.code));
  const nameOk = list.every(r => allRoles.find(x => x.code === r.code)?.name === r.name);
  let mismatch = 0, accepted = 0;
  const whIds = ['dele','admcore'].includes(who) ? [wh1] : [];
  const all_ = !['dele','admcore'].includes(who);
  for (const role of allRoles) {
    const u = `p${sfx}${globalThis.crypto.randomUUID().slice(0, 5)}`;
    const r = await call('POST', '/api/users', T[who], { username: u, email: `${u}@ims.local`, displayName: u, role: role.code, isActive: true, password: PROBE_PW, warehouseIds: whIds, allWarehouses: all_ });
    const ok = r.status === 200 || r.status === 201;
    if (ok) accepted++;
    if (ok !== listed.has(role.code)) { mismatch++; console.log('  MISMATCH', who, role.code, role.isActive ? '' : '(inactive)', 'list', listed.has(role.code), 'save', short(r)); }
    if (!ok && ![403, 400, 409].includes(r.status)) console.log('  odd status', short(r));
  }
  console.log(`${mismatch === 0 && nameOk ? 'ok  ' : 'FAIL'} ${who}: list ${list.length}, save accepted ${accepted}, mismatches ${mismatch}, names match ${nameOk}`);
  console.log('     codes', list.map(r => r.code).join(' '));
}
// own role / soft deleted / inactive not in list
const dl = (await call('GET', AR, T.dele)).json.map(r => r.code);
console.log('delegate sees its own role:', dl.includes(F.R.delegate.code), '| inactive:', dl.includes(F.R.inactive.code), '| deleted:', dl.includes(F.R.deleted.code), '| Admin:', dl.includes('Admin'));
const ac = (await call('GET', AR, T.admcore)).json.map(r => r.code);
console.log('admin-core custom caller sees Admin:', ac.includes('Admin'), 'AdminCoreCustom:', ac.includes(F.R.adminCore.code));
console.log('failures', failures());
