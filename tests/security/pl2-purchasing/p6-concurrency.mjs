// Area 6: concurrency and integrity. Races are real parallel requests (Promise.all) against the live API, repeated for several rounds; the outcome rule is always "exactly one winner and the stored state matches".
import { call, tokens, fx, expect, check, failures, useAdmin, PO, act, tag, etag, newPo, line, submitted, approvedPo, receiptDraft, postReceipt, docAct, reverseDoc, items } from './lib.mjs';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const R = Number(process.env.SEC_ROUNDS ?? 8);
const get = async (id) => (await call('GET', `${PO}/${id}`, T.admin)).json;
const onHand = async () => items(await call('GET', `/api/stock?pageSize=5&search=SEC-${F.sfx}-C`, T.admin)).find((x) => x.warehouseId === F.whA)?.qtyOnHand ?? 0;
const sub = async (doc) => { const s = await docAct(T.staff, doc.id, 'submit'); if (s.json.requiresApproval) await docAct(T.supervisor, doc.id, 'approve', {}); return s; };

// ---- 1. two receipts for the last quantity, posted at the same instant
{
  const base = await onHand(), want = []; let wins = 0, bad = 0, closed = 0;
  for (let i = 0; i < R; i++) {
    const po = await approvedPo(F, T, { lines: [line(F, { productId: F.P.c, qty: 10, unitPrice: 5 })] });
    const a = (await receiptDraft(F, T, po.id)).json, b = (await receiptDraft(F, T, po.id)).json;
    await sub(a); await sub(b);
    const [x, y] = await Promise.all([docAct(T.supervisor, a.id, 'post'), docAct(T.supervisor, b.id, 'post')]);
    const ok = [x, y].filter((r) => r.status === 200).length, conflict = [x, y].filter((r) => r.status === 409).length;
    if (ok === 1 && conflict === 1) wins++; else bad++;
    const g = await get(po.id); if (g.status === 'Closed' && g.closeKind === 'Received' && g.lines[0].receivedQty === 10) closed++;
    want.push(10);
  }
  check(6, `last quantity posted by two receipts at once, ${R} rounds: exactly one 200 and one 409 every round`, wins === R && bad === 0, `${wins}/${R} clean`);
  check(6, 'order Closed(Received) with received = ordered (never 20 of 10) every round', closed === R, `${closed}/${R}`);
  check(6, 'stock on hand grew by exactly 10 per round (no double posting)', (await onHand()) - base === 10 * R, `delta ${(await onHand()) - base} expected ${10 * R}`);
}
// ---- 2. cancel against receipt creation / posting
{
  let clean = 0;
  for (let i = 0; i < R; i++) {
    const po = await approvedPo(F, T);
    const [c, r] = await Promise.all([act(T.supervisor, po.id, 'cancel', { reason: 'SEC race cancel' }), call('POST', `${PO}/${po.id}/receipt`, T.staff)]);
    const g = await get(po.id), one = (c.status === 200) !== (r.status === 200);
    if (one && ((c.status === 200 && g.status === 'Cancelled') || (r.status === 200 && g.status === 'Approved' && g.receipts.length === 1))) clean++;
  }
  check(6, `cancel racing receipt creation, ${R} rounds: exactly one wins, state consistent`, clean === R, `${clean}/${R}`);
  let c2 = 0;
  for (let i = 0; i < R; i++) {
    const po = await approvedPo(F, T); const d = (await receiptDraft(F, T, po.id)).json; await sub(d);
    const [c, p] = await Promise.all([act(T.supervisor, po.id, 'cancel', { reason: 'SEC race cancel' }), docAct(T.supervisor, d.id, 'post')]);
    const g = await get(po.id);
    if (p.status === 200 && c.status !== 200 && ['Approved', 'Closed'].includes(g.status)) c2++;        // an open linked receipt blocks the cancel, so the post must win
  }
  check(6, `cancel racing the post of a submitted linked receipt, ${R} rounds: the cancel never wins, nothing posted against a Cancelled order`, c2 === R, `${c2}/${R}`);
}
// ---- 3. approve against recall / reject / another approver
{
  let a = 0, b = 0, c = 0;
  for (let i = 0; i < R; i++) {
    const s = (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json, t = await tag(s.id);
    const [x, y] = await Promise.all([call('POST', `${PO}/${s.id}/approve`, T.supervisor, undefined, { 'If-Match': t }), call('POST', `${PO}/${s.id}/recall`, T.clerk, undefined, { 'If-Match': t })]);
    const g = await get(s.id); if (([x, y].filter((r) => r.status === 200).length === 1) && ((x.status === 200) === (g.status === 'Approved')) && ((y.status === 200) === (g.status === 'Draft'))) a++;
    const s2 = (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json, t2 = await tag(s2.id);
    const rs = await Promise.all([T.supervisor, T.sup2, T.admin].map((tk) => call('POST', `${PO}/${s2.id}/approve`, tk, undefined, { 'If-Match': t2 })));
    if (rs.filter((r) => r.status === 200).length === 1) b++;
    const s3 = (await submitted(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json, t3 = await tag(s3.id);
    const [p, q] = await Promise.all([call('POST', `${PO}/${s3.id}/approve`, T.supervisor, undefined, { 'If-Match': t3 }), call('POST', `${PO}/${s3.id}/reject`, T.sup2, { reason: 'SEC race reject' }, { 'If-Match': t3 })]);
    const g3 = await get(s3.id); if ([p, q].filter((r) => r.status === 200).length === 1 && (p.status === 200 ? g3.status === 'Approved' : g3.status === 'Draft')) c++;
  }
  check(6, `approve vs recall, ${R} rounds: exactly one 200, stored status matches the winner`, a === R, `${a}/${R}`);
  check(6, `three approvers at once, ${R} rounds: exactly one 200`, b === R, `${b}/${R}`);
  check(6, `approve vs reject, ${R} rounds: exactly one 200, status matches`, c === R, `${c}/${R}`);
}
// ---- 4. double submit, replay, stale and missing If-Match
{
  const d = (await newPo(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] })).json, t = await tag(d.id);
  const r = await Promise.all([1, 2, 3, 4].map(() => call('POST', `${PO}/${d.id}/submit`, T.clerk, undefined, { 'If-Match': t })));
  check(6, 'four simultaneous submits with one token: exactly one 200', r.filter((x) => x.status === 200).length === 1, r.map((x) => x.status).join(','));
  expect(6, 'replaying the same approve (old token) is refused 409', await call('POST', `${PO}/${d.id}/approve`, T.supervisor, undefined, { 'If-Match': t }), 409);
  const cur = await tag(d.id);
  const stale = await call('POST', `${PO}/${d.id}/approve`, T.supervisor, undefined, { 'If-Match': '"AAAAAAAAAAAA"' });
  check(6, 'stale token -> 409 with code document_changed', stale.status === 409 && stale.json.code === 'document_changed', `${stale.status} ${stale.json.code}`);
  expect(6, 'missing If-Match -> 428', await call('POST', `${PO}/${d.id}/approve`, T.supervisor), 428);
  const star = await call('POST', `${PO}/${d.id}/approve`, T.supervisor, undefined, { 'If-Match': '*' });
  check(6, 'FINDING F-2: If-Match "*" is accepted on a required-token endpoint (approve ran without a token)', star.status !== 200, `${star.status}`);
  const c1 = await newPo(T.clerk, F), c2 = await newPo(T.clerk, F);
  check(6, 'replayed create makes two distinct orders with distinct numbers (no idempotency key: by design)', c1.json.id !== c2.json.id && c1.json.no !== c2.json.no);
  const nums = await Promise.all(Array.from({ length: 20 }, () => newPo(T.clerk, F))); const ns = nums.map((x) => x.json.no);
  check(6, '20 parallel creates: distinct numbers, no gaps', new Set(ns).size === 20 && (() => { const k = ns.map((n) => +n.slice(-4)).sort((a, b) => a - b); return k[19] - k[0] === 19; })(), `${ns[0]}..${ns.at(-1)}`);
}
// ---- 5. receiving arithmetic: partial, close, reversal, close short
{
  const po = await approvedPo(F, T, { lines: [line(F, { productId: F.P.c, qty: 10, unitPrice: 5 })] });
  const r1 = (await receiptDraft(F, T, po.id, { qty: 6 })).json; expect(6, '6 of 10 posts', await postReceipt(T, r1), 200);
  let g = await get(po.id); check(6, 'after 6 of 10: still Approved, received 6, remaining 4', g.status === 'Approved' && g.lines[0].receivedQty === 6 && g.lines[0].remainingQty === 4);
  expect(6, 'a draft above the remainder is refused at save (400, advisory)', await receiptDraft(F, T, po.id, { qty: 5 }), 400);
  expect(6, 'zero / negative quantities on a linked draft -> 400', await receiptDraft(F, T, po.id, { qty: 0 }), 400);
  expect(6, 'negative quantity -> 400', await receiptDraft(F, T, po.id, { qty: -1 }), 400);
  expect(6, 'reversing the 6 frees the quantity (200)', await reverseDoc(T.supervisor, r1.id), 200);
  g = await get(po.id); check(6, 'after the reversal: received 0, remaining 10, still Approved', g.status === 'Approved' && g.lines[0].receivedQty === 0 && g.lines[0].remainingQty === 10);
  const r2 = (await receiptDraft(F, T, po.id, { qty: 4 })).json; expect(6, '4 of 10 posts', await postReceipt(T, r2), 200);
  const r3d = (await receiptDraft(F, T, po.id)).json; expect(6, 'close short refused while a receipt is waiting (409)', await act(T.clerk, po.id, 'close', { reason: 'SEC close short' }), 409);
  expect(6, 'cancel the waiting draft receipt', await docAct(T.staff, r3d.id, 'cancel', { reason: 'SEC cleanup' }), 200);
  expect(6, 'close short needs a reason', await act(T.clerk, po.id, 'close', { reason: 'no' }), 400);
  const cl = await act(T.clerk, po.id, 'close', { reason: 'SEC supplier will not ship the rest' }); expect(6, 'close short with 4 received -> 200', cl, 200);
  check(6, 'closed Short, received stays 4', cl.json.status === 'Closed' && cl.json.closeKind === 'Short' && cl.json.lines[0].receivedQty === 4);
  expect(6, 'no receipt can be made from a Closed order (409)', await call('POST', `${PO}/${po.id}/receipt`, T.staff), 409);
  expect(6, 'reversing the 4 does not reopen the Closed order', await reverseDoc(T.supervisor, r2.id), 200);
  check(6, 'order stays Closed after the reversal', (await get(po.id)).status === 'Closed');
  const full = await approvedPo(F, T, { lines: [line(F, { productId: F.P.c, qty: 3, unitPrice: 5 })] });
  const rf = (await receiptDraft(F, T, full.id)).json; await postReceipt(T, rf); const gf = await get(full.id);
  check(6, 'full receipt closes the order by itself (Received)', gf.status === 'Closed' && gf.closeKind === 'Received');
  expect(6, 'a Closed order cannot be cancelled', await act(T.admin, full.id, 'cancel', { reason: 'SEC after close' }), 400, 409);
  expect(6, 'a receipt of a Cancelled order is impossible', await call('POST', `${PO}/${(await (async () => { const a = await approvedPo(F, T); await act(T.supervisor, a.id, 'cancel', { reason: 'SEC cancelled first' }); return a; })()).id}/receipt`, T.staff), 409);
}
// ---- 6. unit conversion lock after use
{
  const po = await approvedPo(F, T, { lines: [line(F, { productId: F.P.box, uomId: F.box, qty: 2, unitPrice: 100 })] });
  const prod = (await call('GET', `/api/products?pageSize=1&search=SEC-${F.sfx}-BOX`, T.admin)).json.items[0];
  const put = (conv) => call('PUT', `/api/products/${prod.id}`, T.admin, { sku: prod.sku, name: prod.name, categoryId: prod.categoryId, baseUomId: prod.baseUomId, safetyStock: prod.safetyStock, isActive: true, barcodes: prod.barcodes, conversions: conv });
  const bad = await put([{ fromUomId: F.box, toUomId: F.pcs, factor: 10 }]); expect(6, 'changing the BOX factor while the product is on an open order -> 400', bad, 400);
  check(6, 'the refusal names the unit and not the order', !JSON.stringify(bad.json).includes(po.no), String(bad.json?.detail ?? '').slice(0, 120));
  expect(6, 'removing the conversion -> 400', await put([]), 400);
  expect(6, 'a re-send of the same factor is allowed', await put([{ fromUomId: F.box, toUomId: F.pcs, factor: 12 }]), 200);
  await act(T.supervisor, po.id, 'cancel', { reason: 'SEC conversion probe done' });
}
console.log(failures() ? `${failures()} FAIL` : 'p6 all ok');
