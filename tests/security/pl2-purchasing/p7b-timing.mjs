// Area 7: user enumeration by timing, measured more carefully than p7 (warm-up, 8 interleaved samples each over two fresh users, so no account reaches the 5-failure lock).
// Only the Administrator is needed; works with the modules in either state.
import { login, useAdmin, roleUser, fx, check, failures } from './lib.mjs';
useAdmin(await login('admin'));
const tag = Date.now().toString(36).slice(-3), users = [];
for (const n of ['tmA', 'tmB']) { await roleUser(n + tag, ['masterdata.read']); users.push(fx().extra.at(-1).username); }
const time = async (u) => { const t0 = performance.now(); await fetch((process.env.RV_BASE ?? 'http://127.0.0.1:5960') + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username: u, password: 'wrong-password-1' }) }); return performance.now() - t0; };
await time('sec-no-such-user'); // warm-up, not counted
const known = [], unknown = [];
for (let i = 0; i < 4; i++) for (const u of users) { known.push(await time(u)); unknown.push(await time('sec-no-such-user')); }
const med = (x) => [...x].sort((a, b) => a - b)[x.length >> 1];
check(7, 'enumeration by timing (8 samples each): existing vs unknown user medians within 40 ms', Math.abs(med(known) - med(unknown)) < 40, `existing ${med(known).toFixed(0)} ms, unknown ${med(unknown).toFixed(0)} ms`);
console.log(failures() ? `${failures()} FAIL` : 'p7b all ok');
