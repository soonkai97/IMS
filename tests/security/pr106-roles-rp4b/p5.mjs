import { call, short, T } from './lib.mjs';
const log = console.log;
log('== who may read / write approval rules');
for (const w of ['admin', 'owner', 'supervisor', 'clerk', 'staff']) {
  const g = await call('GET', '/api/approval-rules', T[w]);
  const p = await call('POST', '/api/approval-rules', T[w], { docType: 'Issue', qtyThreshold: 7, approverTier: 'Large', isActive: false });
  log(`  ${w}: GET ${g.status} | POST ${p.status}${p.status === 200 ? ' (created id ' + p.json.id + ')' : ''}`);
}
const rules = (await call('GET', '/api/approval-rules', T.admin)).json;
log('  GET as staff - amountThreshold hidden?', JSON.stringify((await call('GET', '/api/approval-rules', T.staff)).json[0]));
const target = rules.find((r) => r.docType === 'Issue' && r.amountThreshold === 5000) ?? rules[0];
log('  target rule', target.id, target.approverTier, target.roleRequired);
const send = (b, id) => id ? call('PUT', '/api/approval-rules/' + id, T.admin, b) : call('POST', '/api/approval-rules', T.admin, b);
const base = { docType: 'Receipt', qtyThreshold: 1234, isActive: false };
log('== approverTier values (POST, inactive rule)');
for (const v of ['Standard', 'Large', 'standard', 'LARGE', ' Large', 1, 2, 0, 3, -1, 99, 1.5, true, '', 'Admin', '2', null, [], {}]) {
  const r = await send({ ...base, approverTier: v });
  log('  tier', JSON.stringify(v).padEnd(10), '->', short(r), r.status === 200 ? `${r.json.approverTier}/${r.json.roleRequired}` : '');
}
log('== roleRequired values (no tier)');
for (const v of ['Owner', 'Admin', 'Supervisor', 'Clerk', 'WarehouseStaff', 'owner', 'OWNER', 'Admin ', ' Admin', 'Admin\u0000', 'Hacker', '', null, 5]) {
  const r = await send({ ...base, roleRequired: v });
  log('  role', JSON.stringify(v).padEnd(18), '->', short(r), r.status === 200 ? `${r.json.approverTier}/${r.json.roleRequired}` : '');
}
log('== both given');
for (const [rr, t] of [['Owner', 'Large'], ['Owner', 'Standard'], ['Supervisor', 'Large'], ['Supervisor', 'Standard'], ['Admin', 'Standard'], ['Clerk', 'Large'], ['Owner', 1], ['Supervisor', 2]]) {
  const r = await send({ ...base, roleRequired: rr, approverTier: t });
  log('  ', rr, '+', JSON.stringify(t), '->', short(r), r.status === 200 ? `${r.json.approverTier}/${r.json.roleRequired}` : '');
}
log('== neither given');
log('  ', short(await send({ ...base })));
log('== mass assignment / odd bodies on PUT of a real rule (all inactive copies)');
const mine = (await send({ ...base, approverTier: 'Standard' })).json;
const ex = await send({ ...base, approverTier: 'Large', id: '00000000-0000-0000-0000-000000000001', tier: 'Standard', publicId: '00000000-0000-0000-0000-000000000002', recDeleted: true, REC_DELETED: true, createdBy: 99, roleId: 1 }, mine.id);
log('  extra props ->', short(ex), JSON.stringify(ex.json));
log('  PUT unknown id ->', short(await send({ ...base, approverTier: 'Large' }, '00000000-0000-0000-0000-0000000000ff')));
log('  PUT non-guid id ->', short(await call('PUT', '/api/approval-rules/1', T.admin, { ...base, approverTier: 'Large' })));
log('  PUT sets Large rule inactive/lower thresholds is Admin-only: supervisor PUT ->', short(await call('PUT', '/api/approval-rules/' + mine.id, T.supervisor, { ...base, approverTier: 'Standard' })));
log('  negative qty ->', short(await send({ ...base, qtyThreshold: -1, approverTier: 'Large' })));
log('  amount too precise ->', short(await send({ docType: 'Receipt', amountThreshold: 1.234567, approverTier: 'Large', isActive: false })));
log('  huge amount 1e30 ->', short(await send({ docType: 'Receipt', amountThreshold: 1e30, approverTier: 'Large', isActive: false })));
log('  docType 999 ->', short(await send({ ...base, docType: 999, approverTier: 'Large' })));
log('  docType "Receipt " ->', short(await send({ ...base, docType: 'Receipt ', approverTier: 'Large' })));
log('  malformed JSON ->', short(await send('{"docType":')));
log('  deprecated roleRequired visible shape: list =>', JSON.stringify(Object.keys(rules[0])));
