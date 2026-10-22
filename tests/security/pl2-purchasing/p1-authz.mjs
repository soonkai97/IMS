// Area 1: authorization matrix. Every purchase-order endpoint x every caller kind. The expected cell comes from the permission each endpoint declares
// (po.read / po.manage / document.create + po.read / DocumentReview / PurchaseOrderCancel / accounting.export), never from a role name.
// A "door" cell is the 401/403 decision before any handler runs; where the door is open the probe uses a fresh order in the right state and expects 200.
import { call, tokens, fx, forge, decode, expect, check, failures, useAdmin, PO, act, approvedPo, submitted, newPo, line, etag, tag } from './lib.mjs';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const real = decode(T.clerk); const { perm, ...noPerm } = real; // a validly signed token that says Admin but carries no perm claim
T.forged = forge({ ...noPerm, 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Admin' });
T.expired = forge(real, { exp: -120 });
T.anon = null;
const KINDS = ['admin', 'owner', 'supervisor', 'clerk', 'staff', 'poRead', 'postNoRead', 'none', 'forged', 'expired', 'anon'];
const status = (k, allowed) => (allowed.includes(k) ? 200 : k === 'anon' || k === 'expired' ? 401 : 403);
const read = ['admin', 'owner', 'supervisor', 'clerk', 'staff', 'poRead'], manage = ['admin', 'supervisor', 'clerk'];

// a draft owned by the Clerk, a Standard-tier Submitted order, an Approved order: the shared targets of the denied cells
const draft = (await newPo(T.clerk, F)).json;
const sub = (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json;
const appr = await approvedPo(F, T);
const snap = async () => (await Promise.all([draft.id, sub.id, appr.id].map(async (i) => (await call('GET', `${PO}/${i}`, T.admin)).json.concurrencyToken))).join();
const before = await snap();
const listPath = `${PO}?page=1&pageSize=5`, sugg = `${PO}/suggestions?warehouseId=${F.whA}`, exp = `/api/accounting/export?from=2026-01-01&to=2026-01-02`;

// ---- reads and denied writes, every kind
for (const k of KINDS) {
  const tok = T[k];
  expect(1, `${k} GET list`, await call('GET', listPath, tok), status(k, read));
  expect(1, `${k} GET order`, await call('GET', `${PO}/${draft.id}`, tok), status(k, read));
  expect(1, `${k} GET suggestions`, await call('GET', sugg, tok), status(k, read));
  expect(1, `${k} GET accounting export`, await call('GET', exp, tok), status(k, ['admin', 'owner']));
  if (!manage.includes(k)) {
    expect(1, `${k} POST create`, await call('POST', PO, tok, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F)] }), status(k, manage));
    expect(1, `${k} PUT update`, await call('PUT', `${PO}/${draft.id}`, tok, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F)] }, { 'If-Match': await tag(draft.id) }), status(k, manage));
    expect(1, `${k} POST submit`, await act(tok, draft.id, 'submit'), status(k, manage));
    expect(1, `${k} POST recall`, await act(tok, sub.id, 'recall'), status(k, manage));
    expect(1, `${k} POST close`, await act(tok, appr.id, 'close', { reason: 'SEC no door' }), status(k, manage));
  }
  if (!['admin', 'supervisor', 'owner'].includes(k)) {
    expect(1, `${k} POST approve`, await act(tok, sub.id, 'approve'), status(k, ['admin', 'supervisor', 'owner']));
    expect(1, `${k} POST reject`, await act(tok, sub.id, 'reject', { reason: 'SEC no door' }), status(k, ['admin', 'supervisor', 'owner']));
  }
  if (!['admin', 'supervisor', 'clerk', 'owner'].includes(k)) expect(1, `${k} POST cancel`, await act(tok, appr.id, 'cancel', { reason: 'SEC no door' }), status(k, ['admin', 'supervisor', 'clerk', 'owner']));
  if (!['admin', 'supervisor', 'clerk', 'staff'].includes(k)) expect(1, `${k} POST receipt`, await call('POST', `${PO}/${appr.id}/receipt`, tok), status(k, ['admin', 'supervisor', 'clerk', 'staff']));
}
check(1, 'denied cells changed nothing (tokens of the three shared orders unchanged)', (await snap()) === before);

// ---- open doors, real success paths with fresh orders
const ok = async (label, k, fn) => expect(1, `${k} ${label}`, await fn(T[k]), 200);
for (const k of manage) {
  await ok('POST create', k, (t) => newPo(t, F));
  const d = (await newPo(T.clerk, F)).json;
  await ok('PUT update', k, async (t) => call('PUT', `${PO}/${d.id}`, t, { warehouseId: F.whA, supplierId: F.supplier, remark: 'SEC edit', lines: [line(F)] }, { 'If-Match': await tag(d.id) }));
  const d2 = (await newPo(T.clerk, F)).json; await ok('POST submit', k, (t) => act(t, d2.id, 'submit'));
  const own = (await newPo(T[k], F, { lines: [line(F, { unitPrice: 600 })] })).json; await act(T[k], own.id, 'submit');
  await ok('POST recall (own submitted order)', k, (t) => act(t, own.id, 'recall'));
}
for (const k of ['admin', 'supervisor']) {
  const s = (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json; await ok('POST approve (Standard tier)', k, (t) => act(t, s.id, 'approve'));
  const r = (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json; await ok('POST reject (Standard tier)', k, (t) => act(t, r.id, 'reject', { reason: 'SEC rejected' }));
}
for (const k of ['admin', 'owner']) {
  const s = (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 2500 })] })).json; await ok('POST approve (Large tier)', k, (t) => act(t, s.id, 'approve'));
}
for (const k of ['admin', 'supervisor', 'clerk', 'staff']) { const a = await approvedPo(F, T); await ok('POST receipt', k, (t) => call('POST', `${PO}/${a.id}/receipt`, t)); }
{ const a = await approvedPo(F, T); const r = await call('POST', `${PO}/${a.id}/receipt`, T.postNoRead); check(1, 'document.post without po.read: receipt from order refused (needs po.read)', r.status === 403, `${r.status}`); }
for (const k of ['admin', 'owner']) expect(1, `${k} export returns xlsx`, await call('GET', exp, T[k]), 200);
for (const k of ['admin', 'supervisor']) { const a = await approvedPo(F, T); await ok('POST cancel (Approved, reviewer of its tier)', k, (t) => act(t, a.id, 'cancel', { reason: 'SEC reviewer cancel' })); }
{ const a = await approvedPo(F, T, { tier: 'large' }); await ok('POST cancel (Approved Large, Owner)', 'owner', (t) => act(t, a.id, 'cancel', { reason: 'SEC owner cancel' })); }

// ---- token abuse on one read and one write
const good = T.clerk, parts = good.split('.');
const tamper = parts[0] + '.' + Buffer.from(JSON.stringify({ ...real, perm: [...perm, 'po.manage', 'modules.manage'] })).toString('base64url') + '.' + parts[2];
const bad = { 'payload tampered (extra perms, old signature)': tamper, 'alg none': forge(real, { alg: 'none' }), 'wrong key': forge(real, { key: 'x'.repeat(40) }), 'wrong audience': forge(real, { aud: 'other' }), 'wrong issuer': forge(real, { iss: 'other' }), 'garbage': 'abc.def.ghi' };
for (const [n, t] of Object.entries(bad)) expect(1, `token ${n}: list`, await call('GET', listPath, t), 401);
check(1, 'forged role=Admin without perm claims cannot read (403, not 200)', (await call('GET', listPath, T.forged)).status === 403);
console.log(failures() ? `${failures()} FAIL` : 'p1 all ok');
