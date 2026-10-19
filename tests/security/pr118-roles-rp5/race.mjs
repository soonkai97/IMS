// Race probe: DELETE role vs POST user / PUT user that gives the role. Run against the PR build and against a mutant without LockRolesAsync in DeleteRoleHandler.
// RV_BASE selects the API; RV_ROUNDS the number of rounds (default 40). Needs s0 + tokens.
import fs from 'node:fs';
import { call, PROBE_PW, login } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const F = JSON.parse(fs.readFileSync(`${out}/fixtures.json`, 'utf8'));
const admin = (await login('admin')).token;
const rounds = Number(process.env.RV_ROUNDS ?? 40);
const sfx = Date.now().toString(36).slice(-4);
const o = {}; let orphans = 0;
for (let i = 0; i < rounds; i++) {
  const name = `Rc ${sfx} ${i}`;
  const rr = (await call('POST', '/api/roles', admin, { name, permissions: ['stock.read'] })).json;
  const uname = `rc${sfx}${i}`;
  const [d, u] = await Promise.all([
    call('DELETE', '/api/roles/' + rr.id, admin),
    call('POST', '/api/users', admin, { username: uname, email: `${uname}@ims.local`, displayName: uname, role: rr.code, isActive: true, password: PROBE_PW, warehouseIds: [F.wh1], allWarehouses: false })
  ]);
  const k = `delete ${d.status} / create-user ${u.status}`; o[k] = (o[k] ?? 0) + 1;
  const gone = (await call('GET', '/api/roles/' + rr.id, admin)).status === 404;
  if (u.status === 200 && gone) { orphans++; console.log('ORPHAN: user', uname, 'holds deleted role', rr.code); }
}
console.log(process.env.RV_BASE ?? 'default base', JSON.stringify(o), 'orphans:', orphans);
