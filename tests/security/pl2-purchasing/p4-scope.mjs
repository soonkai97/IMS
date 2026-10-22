// Area 4: warehouse scope and IDOR. Orders of warehouse B are probed by users limited to warehouse A (and the other way round). The convention under test: read or act outside the scope = 404 with the
// same body as an id that does not exist; a write that NAMES an unreadable warehouse = the same 400 as an unknown one.
import { call, tokens, fx, expect, check, failures, useAdmin, PO, act, newPo, line, submitted, approvedPo, items, roleUser, receiptDraft, sleep } from './lib.mjs';
import crypto from 'node:crypto';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const strip = (r) => JSON.stringify(r.json && typeof r.json === 'object' ? { ...r.json, correlationId: 0, traceId: 0, instance: 0 } : r.text);
const ghost = () => crypto.randomUUID();

const b = {                                            // orders of warehouse B, made by the Administrator (who sees every warehouse)
  draft: (await newPo(T.admin, F, { warehouseId: F.whB })).json,
  appr: await approvedPo(F, T, { creator: 'admin', warehouseId: F.whB }),
  sub: (await submitted(T.admin, F, { warehouseId: F.whB, lines: [line(F, { unitPrice: 600 })] })).json,
};
const a = { appr: await approvedPo(F, T) };
check(4, 'fixtures: orders in B exist (admin sees them)', !!(b.draft.id && b.appr.id && b.sub.id));
const rcpB = await receiptDraft(F, T, b.appr.id, { creator: 'admin' });

// ---- user limited to warehouse A looks at B
for (const who of ['scopeA', 'clerk', 'supervisor', 'staff']) {
  const t = T[who];
  const real = await call('GET', `${PO}/${b.draft.id}`, t), none = await call('GET', `${PO}/${ghost()}`, t);
  check(4, `${who}: GET order of B -> 404, body identical to an unknown id`, real.status === 404 && strip(real) === strip(none), `${real.status} vs ${none.status}`);
  const l = await call('GET', `${PO}?pageSize=200`, t); check(4, `${who}: list holds no order of B`, !items(l).some((o) => [b.draft.no, b.appr.no, b.sub.no].includes(o.no)));
  check(4, `${who}: filter by warehouse B and search by B's number return nothing`, items(await call('GET', `${PO}?warehouseId=${F.whB}`, t)).length === 0 && items(await call('GET', `${PO}?search=${b.appr.no}`, t)).length === 0);
}
{
  const t = T.scopeA;
  const probe = [['PUT', b.draft.id, '', { warehouseId: F.whB, supplierId: F.supplier, lines: [line(F)] }], ['POST', b.draft.id, '/submit'], ['POST', b.sub.id, '/recall'], ['POST', b.sub.id, '/approve'],
    ['POST', b.sub.id, '/reject', { reason: 'SEC scope' }], ['POST', b.appr.id, '/cancel', { reason: 'SEC scope' }], ['POST', b.appr.id, '/close', { reason: 'SEC scope' }], ['POST', b.appr.id, '/receipt']];
  for (const [m, id, tail, body] of probe) {
    const tok = tail.includes('approve') || tail.includes('reject') ? T.supervisor : t;                        // the supervisor of A holds the approval right but only for A
    const r = await call(m, `${PO}/${id}${tail}`, tok, body, { 'If-Match': '"x"' }), ghostR = await call(m, `${PO}/${ghost()}${tail}`, tok, body, { 'If-Match': '"x"' });
    check(4, `${m} ${tail || 'update'} on an order of B by a user of A -> 404, same as unknown id`, r.status === 404 && strip(r) === strip(ghostR), `${r.status} / ${ghostR.status}`);
  }
  expect(4, 'create an order for warehouse B -> 400', await newPo(t, F, { warehouseId: F.whB }), 400);
  const unk = await newPo(t, F, { warehouseId: ghost() });
  check(4, 'unreadable warehouse and unknown warehouse give the same 400', (await newPo(t, F, { warehouseId: F.whB })).text.replace(/"(correlationId|traceId)":"[^"]*"/g, '') === unk.text.replace(/"(correlationId|traceId)":"[^"]*"/g, ''));
  const s = await call('GET', `${PO}/suggestions?warehouseId=${F.whB}`, t), sg = await call('GET', `${PO}/suggestions?warehouseId=${ghost()}`, t);
  check(4, 'suggestions for B -> 404, same as an unknown warehouse', s.status === 404 && strip(s) === strip(sg), `${s.status}/${sg.status}`);
  check(4, 'B receipt draft is invisible by id and in lists to a user of A', (await call('GET', `/api/documents/${rcpB.json.id}`, t)).status === 404 && !items(await call('GET', `/api/documents?pageSize=200`, t)).some((d) => d.id === rcpB.json.id));
  const st = await call('POST', `/api/documents/${rcpB.json.id}/submit`, t, undefined, { 'If-Match': '"x"' }); check(4, 'submit/post the B receipt by id as A -> 404', st.status === 404, `${st.status}`);
  // GUID guessing: random ids never answer 200; sequential neighbours of a real id are not valid either
  const guesses = await Promise.all(Array.from({ length: 40 }, () => call('GET', `${PO}/${ghost()}`, T.admin))); check(4, '40 random ids: all 404', guesses.every((g) => g.status === 404));
  const near = b.draft.id.slice(0, -1) + (b.draft.id.endsWith('0') ? '1' : '0'); expect(4, 'an id one character off a real one is 404', await call('GET', `${PO}/${near}`, T.admin), 404);
  // timing: existing-but-forbidden vs unknown (30 samples each, medians must be close)
  const med = (x) => x.sort((p, q) => p - q)[x.length >> 1];
  const t1 = [], t2 = [];
  for (let i = 0; i < 30; i++) { t1.push((await call('GET', `${PO}/${b.draft.id}`, t)).ms); t2.push((await call('GET', `${PO}/${ghost()}`, t)).ms); }
  check(4, 'timing: forbidden vs unknown id medians within 3 ms', Math.abs(med(t1) - med(t2)) < 3, `median ${med(t1).toFixed(1)} vs ${med(t2).toFixed(1)} ms`);
}
// ---- the reverse direction: user of B, and bodies naming foreign ids
{
  const t = T.scopeB;
  expect(4, 'user of B cannot read an order of A', await call('GET', `${PO}/${a.appr.id}`, t), 404);
  expect(4, 'user of B cannot receive against an order of A', await call('POST', `${PO}/${a.appr.id}/receipt`, t), 404);
  check(4, 'user of B: list holds no order of A', !items(await call('GET', `${PO}?pageSize=200`, t)).some((o) => o.no === a.appr.no));
  expect(4, 'supervisor of B (approval right) cannot approve an order of A', await act(T.supB, (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json.id, 'approve'), 404);
  const own = await newPo(t, F, { warehouseId: F.whB }); expect(4, 'user of B creates in B', own, 200);
  const sup = (await newPo(t, F, { warehouseId: F.whB, supplierId: F.customer })); expect(4, 'a customer as supplier -> 400', sup, 400);
  expect(4, 'unknown product id -> 400 naming the line', await newPo(t, F, { warehouseId: F.whB, lines: [line(F, { productId: ghost() })] }), 400);
  expect(4, 'unknown supplier id -> 400', await newPo(t, F, { warehouseId: F.whB, supplierId: ghost() }), 400);
  expect(4, 'unit of measure that is not the product\'s -> 400', await newPo(t, F, { warehouseId: F.whB, lines: [line(F, { uomId: F.box })] }), 400);
  expect(4, 'update cannot move an order to another warehouse (A) -> 400/404', await call('PUT', `${PO}/${own.json.id}`, t, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F)] }, { 'If-Match': own.json.concurrencyToken }), 400, 404);
}
console.log(failures() ? `${failures()} FAIL` : 'p4 all ok');
