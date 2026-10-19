import fs from 'node:fs';
import { call, short, T, decode } from './lib.mjs';
const log = console.log;
const PW = 'Probe#Pass12345';
const wh1 = (await call('GET', '/api/warehouses', T.admin)).json[0].id;
const ids = JSON.parse(fs.readFileSync('D:/ClaudeTemp/rv106/probe/p3ids.json', 'utf8'));
const list = async () => (await call('GET', '/api/users?pageSize=200', T.admin)).json.items;
const put = (t, u, over) => call('PUT', '/api/users/' + u.id, t, { username: u.username, email: u.email, displayName: u.displayName, role: u.role, isActive: u.isActive, warehouseIds: u.warehouseIds, ...over });
const loginAs = async (u) => (await call('POST', '/api/auth/login', null, { username: u, password: PW })).json.accessToken;

// reduce to: admin (sole Admin) + Delegate user (custom role users.manage) as the caller
let users = await list();
const del = users.find((u) => u.id === ids.del.id);
let r = await put(T.admin, {...del, isActive:true}, { role: 'Delegate', isActive:true }); log('reset delegate role ->', short(r)); users = await list();
for (const u of users.filter((u) => u.role === 'Admin' && u.username !== 'admin' && u.isActive)) { const x = await put(T.admin, u, { isActive: false }); if (x.status !== 200) log('deactivate', u.username, short(x)); }
users = await list();
log('active admins now:', users.filter((u) => u.role === 'Admin' && u.isActive).map((u) => u.username).join(','));
const td = await loginAs(del.username);
log('delegate perms:', [].concat(decode(td).perm).join(','));
const admin = users.find((u) => u.username === 'admin');
log('== last-admin guard, caller = Delegate (users.manage only)');
log('  demote sole admin to Supervisor ->', short(await put(td, admin, { role: 'Supervisor' })));
log('  deactivate sole admin ->', short(await put(td, admin, { isActive: false })));
log('  role lowercase "admin" (still Admin) ->', short(await put(td, admin, { role: 'admin' })));
log('  role "Admin " trailing space (still Admin) ->', short(await put(td, admin, { role: 'Admin ' })));
log('  role fullwidth -> demote? ("\\uff21dmin") ->', short(await put(td, admin, { role: '\uff21dmin' })));
log('  role "Dormant" (inactive) ->', short(await put(td, admin, { role: 'Dormant' })));
log('  role "Peek" (custom, demote) ->', short(await put(td, admin, { role: 'Peek' })));
log('  keep Admin, change display name ->', short(await put(td, admin, { displayName: 'Administrator' })));
users = await list(); log('  admin still:', users.find((u) => u.username === 'admin').role, users.find((u) => u.username === 'admin').isActive);

log('== race: two admins, Delegate demotes both at once (x10)');
let broke = 0;
for (let i = 0; i < 10; i++) {
  users = await list();
  const a = users.find((u) => u.username === 'admin');
  const sad = users.find((u) => u.id === ids.sad.id);
  await put(T.admin, sad, { role: 'Admin', isActive: true });
  const [x, y] = await Promise.all([put(td, a, { role: 'Supervisor' }), put(td, { ...sad, role: 'Admin' }, { role: 'Supervisor' })]);
  const left = (await list()).filter((u) => u.role === 'Admin' && u.isActive).length;
  if (left === 0) { broke++; log('  run', i, 'statuses', x.status, y.status, 'ADMINS LEFT 0'); await put(T.admin, a, {role:'Admin'}).catch(()=>{}); }
  else { /* restore */ const aa = (await list()).find((u) => u.username === 'admin'); await put(T.admin, aa, { role: 'Admin', isActive: true }); }
}
log('  runs that left zero active administrators:', broke);
