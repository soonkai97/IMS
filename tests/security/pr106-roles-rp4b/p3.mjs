import fs from 'node:fs';
import { call, short, T, decode } from './lib.mjs';
const log = console.log;
const wh1 = (await call('GET', '/api/warehouses', T.admin)).json[0].id;
const PW = 'Probe#Pass12345';
const ADMINPW = JSON.parse(fs.readFileSync('D:/ClaudeTemp/rv106/pub/appsettings.Development.json', 'utf8')).Seed.AdminPassword;
const sfx = Date.now().toString(36).slice(-4);
const mk = (name, role, extra = {}) => ({ username: name + sfx, email: `${name}${sfx}@ims.local`, displayName: name, role, isActive: true, password: PW, warehouseIds: [wh1], ...extra });
const loginAs = async (u) => { const r = await call('POST', '/api/auth/login', null, { username: u, password: PW }); if (r.status !== 200) throw new Error(u + ' ' + short(r)); return r.json.accessToken; };
const find = async (t, q) => (await call('GET', '/api/users?search=' + q, t)).json.items.find((i) => i.username === q || i.username.startsWith(q));

log('== admin (sees all) grants the flag');
let r = await call('POST', '/api/users', T.admin, mk('allwh', 'Supervisor', { allWarehouses: true }));
log('  create Supervisor allWarehouses:true ->', short(r), 'dto.allWarehouses=', r.json.allWarehouses);
const allwhId = r.json.id; const allwhName = r.json.username;
const tk = await loginAs(allwhName); const c = decode(tk);
log('  its token: allwh=', c.allwh, ' wh claim=', JSON.stringify(c.wh), ' (list was [WH01]; expect no wh claim)');
r = await call('GET', '/api/auth/me', tk); log('  /me seesAllWarehouses=', r.json.seesAllWarehouses, 'warehouseIds=', JSON.stringify(r.json.warehouseIds));
r = await call('POST', '/api/users', T.admin, mk('plain', 'Clerk')); log('  create Clerk without flag -> allWarehouses=', r.json.allWarehouses);
const plain = r.json;
r = await call('PUT', '/api/users/' + plain.id, T.admin, { username: plain.username, email: plain.email, displayName: 'plain', role: 'Clerk', isActive: true, warehouseIds: [wh1] });
log('  PUT same user, flag omitted -> allWarehouses=', r.json.allWarehouses, short(r));

log('== update by admin: omitted keeps, true sets, false clears');
const u1 = await find(T.admin, allwhName);
const base = { username: u1.username, email: u1.email, displayName: 'x', role: 'Supervisor', isActive: true, warehouseIds: [wh1] };
const cur = async () => (await find(T.admin, allwhName)).allWarehouses;
log('  omitted (user has true) ->', short(await call('PUT', '/api/users/' + allwhId, T.admin, base)), '| now', await cur());
log('  explicit null  ->', short(await call('PUT', '/api/users/' + allwhId, T.admin, { ...base, allWarehouses: null })), '| now', await cur());
log('  false ->', short(await call('PUT', '/api/users/' + allwhId, T.admin, { ...base, allWarehouses: false })), '| now', await cur());
log('  "true" string ->', short(await call('PUT', '/api/users/' + allwhId, T.admin, { ...base, allWarehouses: 'true' })));
log('  1 (number) ->', short(await call('PUT', '/api/users/' + allwhId, T.admin, { ...base, allWarehouses: 1 })));

log('== callers WITHOUT users.manage (Supervisor, Clerk, Staff, Owner) try create / update');
for (const w of ['supervisor', 'clerk', 'staff', 'owner']) {
  const a = await call('POST', '/api/users', T[w], mk('x' + w, 'Admin', { allWarehouses: true }));
  const b = await call('PUT', '/api/users/' + plain.id, T[w], { ...base, role: 'Admin', allWarehouses: true });
  const g = await call('GET', '/api/users', T[w]);
  log(`  ${w}: create Admin+flag ${a.status} | update ${b.status} | list ${g.status}`);
}

log('== scoped delegates (hold users.manage, do not see every warehouse)');
const del = (await call('POST', '/api/users', T.admin, mk('del', 'Delegate'))).json;
const sad = (await call('POST', '/api/users', T.admin, mk('sad', 'Admin'))).json;
log('  created Delegate user and a scoped Admin-role user; flags:', del.allWarehouses, sad.allWarehouses);
const tokens = { del: await loginAs(del.username), sad: await loginAs(sad.username) };
for (const [k, u] of [['del', del], ['sad', sad]]) {
  const t = tokens[k]; const cl = decode(t);
  log(`-- ${k}: allwh=${cl.allwh} perms=${[].concat(cl.perm).length}`);
  let x;
  x = await call('POST', '/api/users', t, mk('n1' + k, 'Clerk', { allWarehouses: true })); log('  create Clerk allWarehouses:true ->', short(x));
  x = await call('POST', '/api/users', t, mk('n2' + k, 'Clerk')); log('  create Clerk (flag omitted) ->', short(x), x.json.allWarehouses);
  const selfBase = { username: u.username, email: u.email, displayName: u.displayName, role: u.role, isActive: true, warehouseIds: [wh1] };
  x = await call('PUT', '/api/users/' + u.id, t, { ...selfBase, allWarehouses: true }); log('  SELF allWarehouses:true ->', short(x));
  x = await call('PUT', '/api/users/' + plain.id, t, { username: plain.username, email: plain.email, displayName: 'plain', role: 'Clerk', isActive: true, warehouseIds: [wh1], allWarehouses: true }); log('  OTHER (plain) allWarehouses:true ->', short(x));
  x = await call('POST', '/api/users', t, mk('n3' + k, 'Admin')); log('  create Admin-role user (scoped flag omitted) ->', short(x), x.json.role);
  x = await call('POST', '/api/users', t, mk('n4' + k, 'Owner', { allWarehouses: false })); log('  create Owner-role user flag false ->', short(x));
  const rid = (await call('POST', '/api/warehouses', T.admin, { code: 'W' + k + sfx, name: 'Probe WH ' + k })).json.id;
  x = await call('PUT', '/api/users/' + u.id, t, { ...selfBase, warehouseIds: [wh1, rid] }); log('  SELF: add warehouse outside own scope ->', short(x), x.json.warehouseIds?.length);
  const all = (await call('GET', '/api/warehouses', T.admin)).json.map((w) => w.id);
  x = await call('PUT', '/api/users/' + u.id, t, { ...selfBase, warehouseIds: all }); log('  SELF: warehouseIds = every warehouse ->', short(x));
  x = await call('PUT', '/api/users/' + u.id, t, { ...selfBase, role: 'Admin' }); log('  SELF: role -> Admin ->', short(x));
  const adm = (await call('GET', '/api/users?search=admin@', t)).json.items.find((i) => i.username === 'admin');
  x = await call('PUT', '/api/users/' + adm.id, t, { username: adm.username, email: adm.email, displayName: adm.displayName, role: 'Admin', isActive: true, warehouseIds: [], password: PW });
  log('  reset password of the all-warehouse admin ->', short(x));
  if (x.status === 200) {
    const lg = await call('POST', '/api/auth/login', null, { username: 'admin', password: PW });
    log('    admin signs in with the delegate-chosen password:', lg.status, 'allwh=', lg.status === 200 ? decode(lg.json.accessToken).allwh : '');
    const t2 = lg.json.accessToken;
    const back = await call('PUT', '/api/users/' + adm.id, t2, { username: adm.username, email: adm.email, displayName: adm.displayName, role: 'Admin', isActive: true, warehouseIds: [], password: ADMINPW });
    log('    admin password restored ->', back.status);
  }
}
fs.writeFileSync('D:/ClaudeTemp/rv106/probe/p3ids.json', JSON.stringify({ del, sad, plain, allwhId, sfx }));
