// A user of an inactive or soft-deleted role (database edit): sign-in and refresh stay fail-closed, the list and the save behave, the token in hand lives to its expiry only.
import { load, state, saveUser, call, login, loginFull, refreshWith, expect, failures, sql, adminNames, PROBE_PW, short } from './adm.mjs';
const fx = load();
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const ghost = (await login('admin')).token;
state(fx, ['adm2', 'dep']);
const depLogin = await loginFull('dep', PROBE_PW);   // token + refresh cookie before the role goes
const rt = (u) => Number(sql(`SELECT COUNT(*) FROM RefreshToken t JOIN [User] u ON u.Id=t.UserId WHERE u.Username='${u}'`));

for (const [label, edit, undo] of [
  ['role INACTIVE', "UPDATE [Role] SET IsActive=0 WHERE Name='Deputy'", "UPDATE [Role] SET IsActive=1 WHERE Name='Deputy'"],
  ['role SOFT-DELETED', "UPDATE [Role] SET REC_DELETED=1 WHERE Name='Deputy'", "UPDATE [Role] SET REC_DELETED=0 WHERE Name='Deputy'"]
]) {
  sql(edit);
  const before = rt('dep');
  const bad = await call('POST', '/api/auth/login', null, { username: 'dep', password: 'Wrong#Pass9999' });
  const nouser = await call('POST', '/api/auth/login', null, { username: 'nobody-' + Date.now(), password: PROBE_PW });
  const l = await call('POST', '/api/auth/login', null, { username: 'dep', password: PROBE_PW });
  expect(`${label}: sign-in -> 401`, l, 401);
  console.log(`${label}: same detail as wrong password and unknown user:`, l.json.detail === bad.json.detail && l.json.detail === nouser.json.detail, '| refresh rows before/after', before, rt('dep'));
  const rf = await refreshWith(depLogin.refresh);
  expect(`${label}: refresh with the earlier refresh token -> 401`, rf, 401);
  console.log(`${label}: token in hand still reads (documented staleness):`, (await call('GET', '/api/stock/balances?pageSize=1', depLogin.token)).status, '| /api/me', (await call('GET', '/api/auth/me', depLogin.token)).status);
  const list = await call('GET', '/api/users?search=dep&pageSize=50', ghost);
  console.log(`${label}: dep listed:`, list.json.items.some(u => u.username === 'dep'));
  sql(undo);
}
// count of administrators never includes the inactive/deleted-role holder (checked in p1); finally the user can sign in again once the role is back
expect('role back: sign-in -> 200', await call('POST', '/api/auth/login', null, { username: 'dep', password: PROBE_PW }), 200);
console.log('failures', failures());
