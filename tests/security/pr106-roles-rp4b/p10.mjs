import { call, short, T, decode } from './lib.mjs';
const log = console.log;
const PW = 'Probe#Pass12345';
const wh1 = (await call('GET', '/api/warehouses', T.admin)).json[0].id;
const s = Date.now().toString(36).slice(-5);
const body = { username: 'ma' + s, email: `ma${s}@ims.local`, displayName: 'ma', role: 'Clerk', isActive: true, password: PW, warehouseIds: [wh1],
  id: '00000000-0000-0000-0000-00000000aaaa', publicId: '00000000-0000-0000-0000-00000000bbbb', roleId: 1, RoleId: 1, passwordHash: 'x', failedLoginCount: 99, lockoutUntil: '2099-01-01T00:00:00Z', isSystem: true, recDeleted: true, permissions: ['users.manage'], perm: ['users.manage'], allwh: true, seesAllWarehouses: true, SeesAllWarehouses: true };
const r = await call('POST', '/api/users', T.admin, body);
log('create with extras ->', short(r), JSON.stringify(r.json));
const l = await call('POST', '/api/auth/login', null, { username: body.username, password: PW });
const c = decode(l.json.accessToken);
log('login ok', l.status, 'role', c['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'], 'allwh', c.allwh, 'perms', JSON.stringify(c.perm), 'sub is not the supplied id:', JSON.stringify(c.sub ?? c.nameid).slice(0, 40));
// update with extras
const u = r.json;
const up = await call('PUT', '/api/users/' + u.id, T.admin, { username: u.username, email: u.email, displayName: 'ma2', role: 'Clerk', isActive: true, warehouseIds: [wh1], roleId: 1, id: '00000000-0000-0000-0000-00000000cccc', allWarehouses: false, isSystem: true });
log('update with extras ->', short(up), JSON.stringify(up.json));
