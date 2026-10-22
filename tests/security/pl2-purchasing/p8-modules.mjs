// Area 8: module and role administration. Two stages: `node p8-modules.mjs open` (purchasing ON, open orders exist) and `node p8-modules.mjs off` (after z-cleanup.mjs closed them: switches
// both modules OFF, runs the module-off matrix, hidden grants, the enable guard, and restores the found approval rules by SQL because the guard cannot be undone through the API).
import { execFileSync } from 'node:child_process';
import { call, tokens, fx, forge, decode, expect, check, failures, useAdmin, PO, approvedPo, items, roleUser, sfx } from './lib.mjs';
const stage = process.argv[2] ?? 'open';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const mod = (tok, code, enabled, reason = 'SEC-PL2.11 retest') => call('PUT', `/api/modules/${code}`, tok, { enabled, reason });
const codes = async () => new Set(JSON.stringify((await call('GET', '/api/permissions', T.admin)).json).match(/"(?:code|id)":"([a-z.]+)"/g)?.map((m) => m.split('"')[3]));
const sql = (q) => execFileSync('sqlcmd', ['-S', '(localdb)\\ImsDev', '-d', 'Ims_Dev', '-E', '-W', '-h', '-1', '-s', ',', '-Q', `set nocount on; ${q}`], { encoding: 'utf8' }).trim();
const ruleRows = () => sql("select Id, IsActive, isnull(convert(varchar(33),REC_REVDATE,126),'NULL'), isnull(convert(varchar(10),REC_REVISOR),'NULL') from ApprovalRule where DocType=5 order by Id").split(/\r?\n/).filter(Boolean).map((l) => l.split(','));

if (stage === 'open') {
  for (const k of ['owner', 'supervisor', 'clerk', 'staff', 'poRead', 'none']) expect(8, `${k} cannot switch a module`, await mod(T[k], 'purchasing', true), 403);
  expect(8, 'anonymous cannot switch a module', await mod(null, 'purchasing', true), 401);
  const { perm, ...rest } = decode(T.clerk); expect(8, 'forged role=Admin token without perms cannot switch a module', await mod(forge({ ...rest, 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Admin' }), 'purchasing', false), 403);
  check(8, 'any signed-in user may read the module list (the web hides parts by it)', (await call('GET', '/api/modules', T.staff)).status === 200);
  check(8, 'INFO: an Administrator (roles.manage) may grant modules.manage to a custom role (delegation by design)', (await call('POST', '/api/roles', T.admin, { name: `SEC-${sfx}-mm`, permissions: ['masterdata.read', 'modules.manage'] })).status === 200);
  const rolesAdm = await roleUser('rolesadm', ['masterdata.read', 'roles.read', 'roles.manage']);
  expect(8, 'a roles.manage holder who lacks modules.manage cannot grant it (containment)', await call('POST', '/api/roles', rolesAdm, { name: `SEC-${sfx}-mm2`, permissions: ['masterdata.read', 'modules.manage'] }), 400, 403);
  expect(8, 'unknown module code is refused', await mod(T.admin, 'nosuchmodule', true), 400, 404);
  expect(8, 'a switch without a reason -> 400', await call('PUT', '/api/modules/purchasing', T.admin, { enabled: true, reason: '' }), 400);
  expect(8, 'a switch without "enabled" -> 400', await call('PUT', '/api/modules/purchasing', T.admin, { reason: 'SEC missing enabled' }), 400);
  const open = await approvedPo(F, T);
  const off = await mod(T.admin, 'purchasing', false);
  check(8, 'switching purchasing OFF with open orders -> 409 naming a count, no change', off.status === 409 && /\d+ purchase orders? (are|is) open/.test(JSON.stringify(off.json)) && (await call('GET', '/api/modules', T.admin)).json.find((m) => m.code === 'purchasing').enabled === true, `${off.status} ${String(off.json?.detail).slice(0, 110)}`);
  check(8, 'the refusal does not list order numbers', !JSON.stringify(off.json).includes(open.no));
  const closure = (n, p) => call('POST', '/api/roles', T.admin, { name: `SEC-${sfx}-${n}`, permissions: p });
  expect(8, 'po.manage without cost.view is refused (closure)', await closure('c1', ['masterdata.read', 'stock.read', 'po.read', 'po.manage']), 400);
  expect(8, 'po.manage without stock.read is refused', await closure('c2', ['masterdata.read', 'cost.view', 'po.read', 'po.manage']), 400);
  expect(8, 'po.manage without po.read is refused', await closure('c3', ['masterdata.read', 'stock.read', 'cost.view', 'po.manage']), 400);
  expect(8, 'po.read without masterdata.read is refused', await closure('c4', ['po.read']), 400);
  expect(8, 'a complete po.manage set is accepted', await closure('c5', ['masterdata.read', 'stock.read', 'cost.view', 'po.read', 'po.manage']), 200);
  const r5 = items(await call('GET', '/api/roles', T.admin)).find((r) => r.name === `SEC-${sfx}-c5`);
  const r5g = await call('GET', `/api/roles/${r5.id}`, T.admin);
  expect(8, 'removing cost.view from a role that holds po.manage is refused', await call('PUT', `/api/roles/${r5.id}/permissions`, T.admin, { permissions: ['masterdata.read', 'stock.read', 'po.read', 'po.manage'] }, { 'If-Match': r5g.headers.get('etag') ?? '*' }), 400);
  expect(8, 'the closure-test role is a throw-away: switched off', await call('PUT', `/api/roles/${r5.id}`, T.admin, { name: r5.name, description: null, isActive: false }), 200);
  const rules = items(await call('GET', '/api/approval-rules', T.admin));
  check(8, 'approval rules list shows the purchase-order rules while the module is on', rules.some((r) => r.docType === 'PurchaseOrder'), `${rules.filter((r) => r.docType === 'PurchaseOrder').length} rules`);
  expect(8, 'a non-admin cannot save an approval rule', await call('POST', '/api/approval-rules', T.supervisor, { docType: 'PurchaseOrder', amountThreshold: 1, qtyThreshold: null, isActive: true, approverTier: 'Standard' }), 403);
  const cheap = items(await call('GET', '/api/approval-rules', T.staff));
  check(8, 'approval-rule thresholds are null for a caller without cost.view', cheap.every((r) => r.amountThreshold === null), JSON.stringify(cheap.slice(0, 1)));
}

if (stage === 'off') {
  const found = ruleRows(); console.log('PO rules as found (Id, IsActive, REC_REVDATE, REC_REVISOR):\n' + found.map((r) => r.join(' ')).join('\n'));
  await roleUser('hidden', ['masterdata.read', 'po.read']);
  for (const c of ['purchasing']) expect(8, `switch ${c} OFF`, await mod(T.admin, c, false), 200);
  const ghost = '11111111-1111-1111-1111-111111111111';
  const offAll = [['GET', PO], ['GET', `${PO}/suggestions?warehouseId=${F.whA}`], ['POST', PO], ['GET', `${PO}/${ghost}`], ['POST', `${PO}/${ghost}/approve`], ['POST', `${PO}/${ghost}/receipt`]];
  for (const k of ['admin', 'owner', 'supervisor', 'clerk', 'staff', 'poRead', 'none']) {
    const st = []; for (const [m, p] of offAll) st.push((await call(m, p, T[k], m === 'POST' ? {} : undefined, m === 'POST' ? { 'If-Match': '"x"' } : {})).status);
    check(8, `module OFF: ${k} gets 404 on every purchasing endpoint`, st.every((s) => s === 404), st.join(','));
  }
  check(8, 'module OFF: anonymous still gets 401, not 404 (the module is not disclosed)', (await call('GET', PO)).status === 401);
  const { perm, ...noPerm } = decode(T.clerk); check(8, 'module OFF: a token without perms gets 404 too (the gate runs before the permission)', (await call('GET', PO, forge(noPerm))).status === 404);
  const p = await codes(); check(8, 'module OFF: /api/permissions omits po.read, po.manage', p.size > 10 && !p.has('po.read') && !p.has('po.manage'), `${p.size} codes`);
  expect(8, 'module OFF: a role save naming po.read -> 400 not available', await call('POST', '/api/roles', T.admin, { name: `SEC-${sfx}-off1`, permissions: ['masterdata.read', 'po.read'] }), 400);
  const r = items(await call('GET', '/api/roles', T.admin)).find((x) => x.name === `SEC-${fx().sfx}-hidden`);
  const rg = await call('GET', `/api/roles/${r.id}`, T.admin);
  expect(8, 'module OFF: saving the visible permissions of a role that holds po.read keeps the hidden grant', await call('PUT', `/api/roles/${r.id}/permissions`, T.admin, { permissions: ['masterdata.read'] }, { 'If-Match': rg.headers.get('etag') ?? '*' }), 200);
  expect(8, 'module OFF: a purchase-order approval rule cannot be saved (400)', await call('POST', '/api/approval-rules', T.admin, { docType: 'PurchaseOrder', amountThreshold: 1, qtyThreshold: null, isActive: true, approverTier: 'Standard' }), 400);
  check(8, 'module OFF: the approval-rule list hides the PO rules (kept, hidden)', !items(await call('GET', '/api/approval-rules', T.admin)).some((x) => x.docType === 'PurchaseOrder'));
  const on = await mod(T.admin, 'purchasing', true); expect(8, 'switch purchasing ON again', on, 200);
  check(8, 'the enable reply counts the roles regaining po.* grants (>= 1)', on.json.rolesRegainingGrants >= 1, `rolesRegainingGrants=${on.json.rolesRegainingGrants}`);
  const back = items(await call('GET', '/api/roles', T.admin)).find((x) => x.id === r.id); check(8, 'the hidden grant came back: the role holds po.read again', back.permissions.includes('po.read'), back.permissions.join(','));
  // The guards on the approval rules: while the module is on the LAST active purchase-order rule cannot be switched off through the API (409); the enable guard is reached only when the rules are inactive
  // in the data (a restore, a script), so the probe switches them off by SQL, tries to enable, and puts the found values back in `finally`.
  try {
    const status = [];
    for (const x of items(await call('GET', '/api/approval-rules', T.admin)).filter((q) => q.docType === 'PurchaseOrder' && q.isActive))
      status.push((await call('PUT', `/api/approval-rules/${x.id}`, T.admin, { docType: 'PurchaseOrder', amountThreshold: x.amountThreshold, qtyThreshold: x.qtyThreshold, isActive: false, approverTier: x.approverTier })).status);
    check(8, 'while purchasing is on the last active purchase-order approval rule cannot be deactivated (409)', status.at(-1) === 409, `deactivation statuses ${status.join(',')}`);
    sql('update ApprovalRule set IsActive=0 where DocType=5');
    expect(8, 'switch purchasing OFF (no open orders)', await mod(T.admin, 'purchasing', false), 200);
    const guard = await mod(T.admin, 'purchasing', true);
    check(8, 'enabling purchasing with no active PO approval rule is refused and the module stays OFF', [400, 409].includes(guard.status) && (await call('GET', '/api/modules', T.admin)).json.find((m) => m.code === 'purchasing').enabled === false, `${guard.status} ${String(guard.json?.detail).slice(0, 140)}`);
  } finally {
    for (const [id, active, rd, rv] of found) sql(`update ApprovalRule set IsActive=${active === 'True' || active === '1' ? 1 : 0}, REC_REVDATE=${rd === 'NULL' ? 'NULL' : `'${rd}'`}, REC_REVISOR=${rv === 'NULL' ? 'NULL' : rv} where Id=${id}`);
    const now = ruleRows(); check(8, 'approval rules restored exactly as found (IsActive, REC_REVDATE, REC_REVISOR)', JSON.stringify(now) === JSON.stringify(found), now.map((r) => r.join(' ')).join(' | '));
  }
  const after = (await call('GET', '/api/modules', T.admin)).json; check(8, 'final: both modules OFF as found', after.every((m) => m.enabled === false), JSON.stringify(after.map((m) => [m.code, m.enabled])));
}
console.log(failures() ? `${failures()} FAIL` : `p8 ${stage} all ok`);
