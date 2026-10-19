// Lock timeout behaviour. Hold the 'roles' application lock in another session first (see README), then run this: a user save, a role write and a read are timed.
import { load, state, saveUser, replacePerms, call, login, sql } from './adm.mjs';
const fx = load();
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const ghost = (await login('admin')).token;
state(fx, ['adm2', 'dep']);
const timed = async (label, f) => { const t = Date.now(); const r = await f(); console.log(`${label}: ${r.status} in ${Date.now() - t} ms ${String(r.json?.detail ?? r.json?.title ?? '').slice(0, 120)}`); return r; };
await timed('GET users (read, must not wait)', () => call('GET', '/api/users?pageSize=5', ghost));
await timed('GET roles (read, must not wait)', () => call('GET', '/api/roles', ghost));
await timed('PUT user (waits for the lock)', () => saveUser(ghost, fx, 'dep', {}));
await timed('PUT role permissions (waits for the lock)', () => replacePerms(ghost, fx.R.Deputy.id, fx.R.Deputy.permissions));
