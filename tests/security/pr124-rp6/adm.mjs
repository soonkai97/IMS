// Fixtures and helpers for the PR 124 (RP-6, administrability) probes.
import fs from 'node:fs';
import { call, login, loginFull, refreshWith, short, PROBE_PW, ALL, CORE, sql, adminCount, adminNames, expect, failures } from './lib.mjs';
export { call, login, loginFull, refreshWith, short, PROBE_PW, ALL, CORE, sql, adminCount, adminNames, expect, failures };
const OUT = process.env.RV_OUT ?? '.';
const FX = `${OUT}/fx124.json`;
export const load = () => JSON.parse(fs.readFileSync(FX, 'utf8'));

// username -> { role (home role code), id }
export const HOME = { adm2: 'Admin', adm3: 'Admin', dep: 'Deputy', core1: 'CoreOnly', core2: 'CoreOnly', cp: 'CopyA', del3: 'ThreeOfCore' };

export async function setup() {
  const a = await login('admin');
  const T = { admin: a.token };
  const wh = (await call('GET', '/api/warehouses', a.token)).json;
  const wh1 = (Array.isArray(wh) ? wh : wh.items)[0].id;
  const roles = (await call('GET', '/api/roles?includeInactive=true', a.token)).json;
  const byName = Object.fromEntries((Array.isArray(roles) ? roles : roles.items).map(r => [r.name, r]));
  const mk = async (name, perms, extra = {}) => byName[name] ?? (await call('POST', '/api/roles', a.token, { name, permissions: perms, ...extra })).json;
  const R = {
    Deputy: await mk('Deputy', [...ALL]),
    CoreOnly: await mk('CoreOnly', [...CORE]),
    ThreeOfCore: await mk('ThreeOfCore', ['users.read', 'users.manage', 'roles.read', 'stock.read']),
    CopyA: await mk('CopyA', null, { copyFromRoleId: byName['Admin'].id })
  };
  if (R.CopyA && !R.CopyA.id) throw new Error('copy failed ' + JSON.stringify(R.CopyA));
  const users = {};
  for (const [u, rn] of Object.entries(HOME)) {
    const r = await call('POST', '/api/users', a.token, { username: u, email: `${u}@ims.local`, displayName: u, role: R[rn]?.code ?? 'Admin', isActive: true, password: PROBE_PW, warehouseIds: [wh1], allWarehouses: true });
    users[u] = r.json?.id ? r.json : (await call('GET', `/api/users?search=${u}`, a.token)).json.items.find(x => x.username === u);
  }
  fs.writeFileSync(FX, JSON.stringify({ wh1, R, users, adminRole: byName['Admin'] }));
  return load();
}

export const ROLE_CODE = (fx, home) => (home === 'Admin' ? 'Admin' : fx.R[home].code);

// Put the database in a known state with SQL: only the named fixture users are administrators (active), the others are inactive; the seeded admin is deactivated
// (its token stays valid: it is the caller who is not one of the people at stake). Roles are live again, lockouts cleared.
export function state(fx, names) {
  sql(`UPDATE [Role] SET IsActive=1, REC_DELETED=0 WHERE Name IN ('Deputy','CoreOnly','ThreeOfCore','CopyA');
       UPDATE [User] SET LockoutUntil=NULL, FailedLoginCount=0 WHERE 1=1;`.replace(', FailedLoginCount=0', ''));
  for (const [u, home] of Object.entries(HOME)) {
    const roleCode = ROLE_CODE(fx, home);
    sql(`UPDATE [User] SET IsActive=${names.includes(u) ? 1 : 0}, REC_DELETED=0, RoleId=(SELECT Id FROM [Role] WHERE Code='${roleCode}' AND REC_DELETED=0) WHERE Username='${u}'`);
  }
  sql(`UPDATE [User] SET IsActive=0 WHERE Username='admin'`);
}
export const tokenOf = async (u) => (await login(u, PROBE_PW)).token;

export const body = (fx, u, over = {}) => ({ username: u, email: `${u}@ims.local`, displayName: u, role: ROLE_CODE(fx, HOME[u]), isActive: true, warehouseIds: [fx.wh1], allWarehouses: true, ...over });
export const saveUser = (tok, fx, u, over) => call('PUT', `/api/users/${fx.users[u].id}`, tok, body(fx, u, over));

export async function getRole(tok, id) {
  const r = await call('GET', `/api/roles/${id}`, tok);
  return { ...r, tag: r.headers.get('etag') };
}
export async function replacePerms(tok, id, permissions) {
  const g = await getRole(tok, id);
  return call('PUT', `/api/roles/${id}/permissions`, tok, { permissions }, { 'If-Match': g.tag });
}
