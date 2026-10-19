// Probe 5: a user whose role row is soft-deleted or inactive by a database edit (the API refuses to produce this state):
// sign-in and refresh must fail closed with the uniform 401 and leave no refresh row. Needs s0 + tokens. Uses sqlcmd against the throw-away database.
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { call, loginFull, refreshWith, expect, short, failures, PROBE_PW } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const DB = process.env.RV_DB ?? 'Ims_Rv118';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
const F = JSON.parse(fs.readFileSync(`${out}/fixtures.json`, 'utf8'));
const sql = (q) => execFileSync('sqlcmd', ['-S', '(localdb)\\ImsDev', '-E', '-I', '-b', '-d', DB, '-h', '-1', '-W', '-Q', `SET NOCOUNT ON; ${q}`], { encoding: 'utf8' }).trim();
const rows = () => Number(sql(`SELECT COUNT(*) FROM RefreshToken WHERE UserId=(SELECT Id FROM [User] WHERE Username='holder')`));
const list = async () => (await call('GET', '/api/users?pageSize=200', T.admin)).json;

const base = await loginFull('holder', PROBE_PW);
console.log('baseline login', base.status, 'refresh row count', rows());
const before = await list();
for (const [label, set, undo] of [
  ['role soft-deleted (REC_DELETED=1)', 'REC_DELETED=1', 'REC_DELETED=0'],
  ['role inactive (IsActive=0)', 'IsActive=0', 'IsActive=1']
]) {
  console.log(`== ${label}`);
  const old = await loginFull('holder', PROBE_PW); // a live session from before the edit
  sql(`UPDATE [Role] SET ${set} WHERE Code='SubOfDelegate'`);
  const n0 = rows();
  const wrong = await call('POST', '/api/auth/login', null, { username: 'holder', password: 'definitely-wrong-1' });
  const ghost = await call('POST', '/api/auth/login', null, { username: 'nobody-here', password: 'definitely-wrong-1' });
  const dead = await loginFull('holder', PROBE_PW);
  console.log('   sign-in with the right password ->', dead.status, JSON.stringify(dead.json?.detail), '| wrong password ->', wrong.status, JSON.stringify(wrong.json?.detail), '| unknown user ->', ghost.status, JSON.stringify(ghost.json?.detail));
  console.log('   uniform message:', dead.json?.detail === wrong.json?.detail && wrong.json?.detail === ghost.json?.detail);
  console.log('   refresh rows before/after the failed sign-in:', n0, rows());
  const rf = await refreshWith(old.refresh);
  console.log('   refresh with the pre-edit token ->', rf.status, JSON.stringify(rf.json?.detail), '| rows after:', rows());
  const me = await call('GET', '/api/auth/me', old.token);
  console.log('   the old access token on /me ->', me.status, '(lives until it expires)');
  const l = await list();
  console.log('   user list total', before.total, '->', l.total, '; holder listed:', (l.items ?? []).some((u) => u.username === 'holder'));
  const h = (before.items ?? []).find((u) => u.username === 'holder');
  const put = await call('PUT', '/api/users/' + F.U.holder.id, T.admin, { username: 'holder', email: 'holder@ims.local', displayName: 'holder', role: 'SubOfDelegate', isActive: true, warehouseIds: [F.wh1], allWarehouses: false });
  console.log('   admin PUT user (repair) ->', short(put));
  sql(`UPDATE [Role] SET ${undo} WHERE Code='SubOfDelegate'`);
  void h;
}
console.log('restored; login ->', (await loginFull('holder', PROBE_PW)).status);
