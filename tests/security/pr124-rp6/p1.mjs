// Sequential lock-out matrix. Caller = the seeded administrator, deactivated by SQL after sign-in (live token): not one of the people at stake.
import { load, state, tokenOf, saveUser, replacePerms, call, login, expect, failures, adminCount, adminNames, sql, short } from './adm.mjs';
const fx = load();
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const ghost = (await login('admin')).token;
const R = (k) => fx.R[k].id;
const keep = (label, n) => { const c = adminCount(); console.log(`${c >= n ? 'ok  ' : 'FAIL'} ${label}: administrators=${c} (expected >= ${n})`); };

// T1 two admins: adm2 (Admin) and dep (custom, all permissions)
state(fx, ['adm2', 'dep']);
console.log('T1 start', adminNames());
expect('T1 demote adm2 (dep remains)', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 200);
expect('T1 demote dep (last) -> 409', await saveUser(ghost, fx, 'dep', { role: 'Clerk' }), 409);
expect('T1 deactivate dep (last) -> 409', await saveUser(ghost, fx, 'dep', { isActive: false }), 409);
expect('T1 replace Deputy without roles.manage -> 409', await replacePerms(ghost, R('Deputy'), fx.R.Deputy.permissions.filter(p => p !== 'roles.manage')), 409);
expect('T1 replace Deputy with stock.read only -> 409', await replacePerms(ghost, R('Deputy'), ['stock.read']), 409);
expect('T1 replace Deputy with empty -> 409', await replacePerms(ghost, R('Deputy'), []), 409);
expect('T1 delete Deputy (in use) -> 409', await call('DELETE', `/api/roles/${R('Deputy')}`, ghost), 409);
expect('T1 deactivate Deputy (in use) -> 409', await call('PUT', `/api/roles/${R('Deputy')}`, ghost, { name: 'Deputy', isActive: false }), 409);
expect('T1 replace Deputy dropping a non-core permission -> 200', await replacePerms(ghost, R('Deputy'), fx.R.Deputy.permissions.filter(p => p !== 'stock.read')), 200);
expect('T1 same save again (dep role unchanged) -> 200', await saveUser(ghost, fx, 'dep', {}), 200);
keep('T1 end', 1);

// T2 role made inactive by a database edit: its holder must not count
state(fx, ['adm2', 'dep']);
sql(`UPDATE [Role] SET IsActive=0 WHERE Name='Deputy'`);
console.log('T2 admins by rule', adminNames());
expect('T2 demote adm2 while dep holds the core through an INACTIVE role -> 409', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 409);
expect('T2 deactivate adm2 -> 409', await saveUser(ghost, fx, 'adm2', { isActive: false }), 409);
keep('T2 end', 1);

// T3 role soft-deleted by a database edit
state(fx, ['adm2', 'dep']);
sql(`UPDATE [Role] SET REC_DELETED=1 WHERE Name='Deputy'`);
expect('T3 demote adm2 while dep holds the core through a DELETED role -> 409', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 409);
const list = await call('GET', '/api/users?search=dep&pageSize=50', ghost);
console.log('T3 dep listed:', list.json.items?.some(u => u.username === 'dep'), 'role shown:', list.json.items?.find(u => u.username === 'dep')?.role);
expect('T3 give dep the deleted role again -> 400', await saveUser(ghost, fx, 'dep', { role: 'Deputy' }), 400);
expect('T3 move dep to Clerk -> 200', await saveUser(ghost, fx, 'dep', { role: 'Clerk' }), 200);
keep('T3 end', 1);

// T4 user soft-deleted by a database edit
state(fx, ['adm2', 'dep']);
sql(`UPDATE [User] SET REC_DELETED=1 WHERE Username='dep'`);
expect('T4 demote adm2 while dep is soft-deleted -> 409', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 409);
expect('T4 PUT the soft-deleted user -> 404', await saveUser(ghost, fx, 'dep', { role: 'Clerk' }), 404);
const l4 = await call('GET', '/api/users?pageSize=100&search=dep', ghost);
console.log('T4 soft-deleted user in list:', l4.json.items.some(u => u.username === 'dep'), '(expected false)');
sql(`UPDATE [User] SET REC_DELETED=0 WHERE Username='dep'`);

// T5 revoked grant rows must not count
state(fx, ['adm2', 'dep']);
expect('T5 Deputy to all (reset)', await replacePerms(ghost, R('Deputy'), fx.R.Deputy.permissions), 200);
expect('T5 replace Deputy without users.manage (adm2 remains) -> 200', await replacePerms(ghost, R('Deputy'), fx.R.Deputy.permissions.filter(p => p !== 'users.manage')), 200);
console.log('T5 admins by rule', adminNames(), 'soft-deleted RolePermission rows of Deputy:', sql(`SELECT COUNT(*) FROM RolePermission WHERE REC_DELETED=1 AND RoleId=(SELECT Id FROM [Role] WHERE Name='Deputy')`));
expect('T5 demote adm2 (dep no longer holds the core; a revoked row must not count) -> 409', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 409);
expect('T5 re-grant: Deputy back to all -> 200', await replacePerms(ghost, R('Deputy'), fx.R.Deputy.permissions), 200);
expect('T5 dep is an administrator again: demote adm2 -> 200', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 200);
keep('T5 end', 1);

// T6 create-by-copy of Admin: the copy administers
state(fx, ['adm2', 'cp']);
console.log('T6 admins', adminNames());
expect('T6 demote adm2 (cp holds the copy of Admin)', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 200);
expect('T6 demote cp (last) -> 409', await saveUser(ghost, fx, 'cp', { role: 'Clerk' }), 409);
expect('T6 replace CopyA losing the core -> 409', await replacePerms(ghost, R('CopyA'), fx.R.CopyA.permissions.filter(p => !p.startsWith('roles.') && p !== 'users.manage')), 409);
keep('T6 end', 1);

// T7 a locked-out account still counts as an administrator
state(fx, ['adm2', 'dep']);
sql(`UPDATE [User] SET LockoutUntil=DATEADD(minute,30,SYSUTCDATETIME()) WHERE Username='dep'`);
const r7 = await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' });
console.log(`INFO T7 demote adm2 while the only other administrator is locked out for 30 minutes -> ${short(r7)}`);
sql(`UPDATE [User] SET LockoutUntil=NULL WHERE Username='dep'`);

// T8 delegate holding users.read, users.manage, roles.read (not roles.manage): not an administrator, cannot touch one
state(fx, ['adm2', 'del3', 'dep']);
const del3 = await tokenOf('del3');
expect('T8 del3 (3 of 4) demotes adm2 -> 403', await saveUser(del3, fx, 'adm2', { role: 'Supervisor' }), 403);
expect('T8 del3 deactivates dep -> 403', await saveUser(del3, fx, 'dep', { isActive: false }), 403);
expect('T8 del3 edits Deputy role -> 403 (no roles.manage)', await replacePerms(del3, R('Deputy'), ['stock.read']), 403);
console.log('T8 admins', adminNames());

// T9 a holder of the bare core: self lock-out, peers
state(fx, ['core1', 'core2']);
const c1 = await tokenOf('core1');
expect('T9 core1 demotes itself -> 409 CannotLockOutSelf', await saveUser(c1, fx, 'core1', { role: 'Clerk' }), 409);
expect('T9 core1 deactivates itself -> 409', await saveUser(c1, fx, 'core1', { isActive: false }), 409);
expect('T9 core1 deactivates core2 (core1 remains) -> 200', await saveUser(c1, fx, 'core2', { isActive: false }), 200);
expect('T9 core1 reactivates core2', await saveUser(c1, fx, 'core2', { isActive: true }), 200);
keep('T9 end', 1);
console.log('failures', failures());

// T10 a holder of only three of the four core permissions is not an administrator
state(fx, ['adm2', 'del3']);
console.log('T10 admins by rule', adminNames());
expect('T10 demote adm2 while del3 holds 3 of 4 core permissions -> 409', await saveUser(ghost, fx, 'adm2', { role: 'Supervisor' }), 409);
console.log('failures', failures());
