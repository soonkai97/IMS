// The roles lock is held while a password is hashed (user save with "password"). Light load: 30 parallel saves, then one role edit behind them.
import { load, state, saveUser, replacePerms, call, login, sql } from './adm.mjs';
const fx = load();
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const ghost = (await login('admin')).token;
state(fx, ['adm2', 'dep']);
const N = Number(process.env.N ?? 30);
const t0 = Date.now();
const one = async (f) => { const t = Date.now(); const r = await f(); return { s: r.status, ms: Date.now() - t }; };
const solo = await one(() => saveUser(ghost, fx, 'dep', { password: 'Probe#Pass12345' }));
const solo2 = await one(() => saveUser(ghost, fx, 'dep', {}));
console.log(`single save with password ${solo.ms} ms, without ${solo2.ms} ms`);
const jobs = [
  ...Array.from({ length: N }, () => one(() => saveUser(ghost, fx, 'dep', { password: 'Probe#Pass12345' }))),
];
const rolePromise = (async () => { await new Promise(r => setTimeout(r, 150)); return one(() => replacePerms(ghost, fx.R.Deputy.id, fx.R.Deputy.permissions)); })();
const res = await Promise.all(jobs);
const role = await rolePromise;
const ms = res.map(r => r.ms).sort((a, b) => a - b);
console.log(`${N} parallel password saves: statuses ${JSON.stringify(res.reduce((m, r) => ((m[r.s] = (m[r.s] ?? 0) + 1), m), {}))}, latency min/median/max ${ms[0]}/${ms[Math.floor(ms.length / 2)]}/${ms.at(-1)} ms; wall ${Date.now() - t0} ms`);
console.log(`a role edit queued behind them: ${role.s} in ${role.ms} ms`);
