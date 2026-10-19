// Parallel lock-out attempts. Caller = the seeded administrator deactivated after sign-in (live token), or the administrators themselves (R4, R5).
// After every round: at least one administrator by the rule, and no active user holds an inactive or deleted role. Status counts are printed per scenario.
import { load, state, tokenOf, saveUser, replacePerms, call, login, failures, adminCount, adminNames, sql } from './adm.mjs';
const fx = load();
const ROUNDS = Number(process.env.ROUNDS ?? 12);
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const ghost = (await login('admin')).token;
const R = (k) => fx.R[k].id;
let bad = 0;
const hist = (rs) => rs.reduce((m, r) => ((m[r.status] = (m[r.status] ?? 0) + 1), m), {});
const merge = (a, b) => { for (const [k, v] of Object.entries(b)) a[k] = (a[k] ?? 0) + v; };
const orphans = () => Number(sql(`SELECT COUNT(*) FROM [User] u JOIN [Role] r ON r.Id=u.RoleId WHERE u.REC_DELETED=0 AND u.IsActive=1 AND (r.REC_DELETED=1 OR r.IsActive=0)`));
function check(label, round, statuses) {
  const c = adminCount(), o = orphans();
  if (c < 1 || o > 0 || statuses.some(s => s >= 500)) { bad++; console.log(`FAIL ${label} round ${round}: administrators=${c} orphanedHolders=${o} statuses=${statuses.join(',')}`); }
}
async function scenario(label, names, build, after) {
  const total = {};
  for (let i = 0; i < ROUNDS; i++) {
    state(fx, names);
    if (after?.before) await after.before();
    const rs = await Promise.all(build());
    merge(total, hist(rs));
    check(label, i, rs.map(r => r.status));
    if (after?.after) await after.after();
  }
  console.log(`${label}: ${JSON.stringify(total)}`);
}

// R1 two administrators, 10 demotions of each, all at once, by a caller who is not one of them
await scenario('R1 demote adm2 x10 + demote adm3 x10', ['adm2', 'adm3'], () => [
  ...Array.from({ length: 10 }, () => saveUser(ghost, fx, 'adm2', { role: 'Supervisor' })),
  ...Array.from({ length: 10 }, () => saveUser(ghost, fx, 'adm3', { isActive: false }))
]);

// R2 three administrators of three kinds, demoted at once (twice each)
await scenario('R2 adm2/adm3/dep each demoted x2', ['adm2', 'adm3', 'dep'], () => [
  ...[0, 1].map(() => saveUser(ghost, fx, 'adm2', { role: 'Supervisor' })),
  ...[0, 1].map(() => saveUser(ghost, fx, 'adm3', { isActive: false })),
  ...[0, 1].map(() => saveUser(ghost, fx, 'dep', { role: 'Clerk' }))
]);

// R3 a user save against a role edit: adm2 demoted while Deputy loses the core
const full = fx.R.Deputy.permissions;
await scenario('R3 demote adm2 x8 vs strip core from Deputy x8', ['adm2', 'dep'], () => [
  ...Array.from({ length: 8 }, () => saveUser(ghost, fx, 'adm2', { role: 'Supervisor' })),
  ...Array.from({ length: 8 }, () => replacePerms(ghost, R('Deputy'), ['stock.read']))
], { after: async () => { const r = await replacePerms(ghost, R('Deputy'), full); if (r.status !== 200) console.log('restore Deputy', r.status); } });

// R4 two real administrators (bare core) deactivate each other
state(fx, ['adm2', 'adm3', 'core1', 'core2']);
const c1 = await tokenOf('core1'), c2 = await tokenOf('core2');
await scenario('R4 core1 deactivates core2 x6 + core2 deactivates core1 x6', ['core1', 'core2'], () => [
  ...Array.from({ length: 6 }, () => saveUser(c1, fx, 'core2', { isActive: false })),
  ...Array.from({ length: 6 }, () => saveUser(c2, fx, 'core1', { isActive: false }))
]);

// R5 the Administrator role: two administrators demote each other, with the stale caller joining in
state(fx, ['adm2', 'adm3']);
const a2 = await tokenOf('adm2'), a3 = await tokenOf('adm3');
await scenario('R5 adm2<->adm3 cross demotion x5 + ghost x5', ['adm2', 'adm3'], () => [
  ...Array.from({ length: 5 }, () => saveUser(a2, fx, 'adm3', { role: 'Supervisor' })),
  ...Array.from({ length: 5 }, () => saveUser(a3, fx, 'adm2', { role: 'Supervisor' })),
  ...Array.from({ length: 5 }, () => saveUser(ghost, fx, 'adm3', { isActive: false }))
]);

// R6 give a user a role while the role is deactivated (cp is no administrator here)
await scenario('R6 give CopyA to dep x8 vs deactivate CopyA x8', ['adm2'], () => [
  ...Array.from({ length: 8 }, () => saveUser(ghost, fx, 'cp', { role: fx.R.CopyA.code })),
  ...Array.from({ length: 8 }, () => call('PUT', `/api/roles/${R('CopyA')}`, ghost, { name: 'CopyA', isActive: false }))
], { before: async () => { sql("UPDATE [User] SET RoleId=(SELECT Id FROM [Role] WHERE Code='Clerk') WHERE Username='cp'; UPDATE [Role] SET IsActive=1 WHERE Name='CopyA'"); }, after: async () => { sql("UPDATE [Role] SET IsActive=1 WHERE Name='CopyA'"); } });

// R7 an administrator holding a custom role vs the role being renamed and re-described (no change of who administers)
await scenario('R7 renames + saves of adm2/dep in parallel', ['adm2', 'dep'], () => [
  ...Array.from({ length: 4 }, (_, i) => call('PUT', `/api/roles/${R('Deputy')}`, ghost, { name: 'Deputy', description: `d${i}` })),
  ...Array.from({ length: 4 }, () => saveUser(ghost, fx, 'dep', {})),
  ...Array.from({ length: 4 }, () => saveUser(ghost, fx, 'adm2', {}))
]);
console.log(bad === 0 ? 'ok   no round ended with zero administrators or an active holder of a dead role' : `FAIL ${bad} bad rounds`);
