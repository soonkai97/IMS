// Two API instances (RV_BASE and RV_BASE2) on one database: the roles lock is a database application lock, so the race is still serialised.
import { load, state, body, login, adminCount, sql } from './adm.mjs';
const fx = load();
const B1 = process.env.RV_BASE, B2 = process.env.RV_BASE2;
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const lg = async (b) => (await (await fetch(b + '/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ username: 'admin', password: process.env.RV_ADMIN_PW }) })).json()).accessToken;
const G = { [B1]: await lg(B1), [B2]: await lg(B2) };
const put = (base, u, over) => fetch(`${base}/api/users/${fx.users[u].id}`, { method: 'PUT', headers: { 'content-type': 'application/json', authorization: 'Bearer ' + G[base] }, body: JSON.stringify(body(fx, u, over)) }).then(r => r.status);
let bad = 0; const tot = {};
for (let i = 0; i < Number(process.env.ROUNDS ?? 15); i++) {
  state(fx, ['adm2', 'adm3']);
  const rs = await Promise.all([
    ...Array.from({ length: 6 }, () => put(B1, 'adm2', { role: 'Supervisor' })),
    ...Array.from({ length: 6 }, () => put(B2, 'adm3', { isActive: false }))
  ]);
  for (const s of rs) tot[s] = (tot[s] ?? 0) + 1;
  if (adminCount() < 1) { bad++; console.log('FAIL round', i, rs.join(',')); }
}
console.log('two instances:', JSON.stringify(tot), bad === 0 ? 'ok   never zero administrators' : `FAIL ${bad} rounds`);
