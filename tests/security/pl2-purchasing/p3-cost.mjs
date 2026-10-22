// Area 3: cost hiding. Callers without cost.view (Warehouse staff, a po.read-only role, an audit-only role, an approver-without-cost role) must get JSON null for every price and amount,
// and no money in raw bodies. A marker price (731.29, amount 2193.87) is searched for in the raw text of every response, so a field the DTO scan misses is still found.
import { call, tokens, fx, expect, check, failures, useAdmin, PO, act, approvedPo, newPo, line, etag, moneyPaths, roleUser, receiptDraft, postReceipt, docAct, items } from './lib.mjs';
const F = fx(), T = await tokens(); useAdmin(T.admin);
T.auditor = await roleUser('auditor', ['masterdata.read', 'audit.read', 'po.read']);
T.reviewer = await roleUser('revnocost', ['masterdata.read', 'stock.read', 'po.read', 'document.approve']);
const PRICE = '731.29', AMOUNT = '2193.87';
const clean = (label, r) => { const leak = r.text.includes(PRICE) || r.text.includes(AMOUNT); const paths = typeof r.json === 'object' ? moneyPaths(r.json) : []; check(3, label, !leak && paths.length === 0, leak ? 'RAW MARKER PRESENT' : paths.slice(0, 3).join(' ') || 'no money'); };

const po = await approvedPo(F, T, { lines: [line(F, { qty: 3, unitPrice: 731.29 })] });
const withCost = await call('GET', `${PO}/${po.id}`, T.clerk);
check(3, 'control: a cost.view holder (Clerk) does see the price', withCost.json.lines[0].unitPrice === 731.29 && withCost.json.totalAmount === 2193.87);
const NOCOST = ['staff', 'poRead', 'auditor', 'reviewer'];
for (const k of NOCOST) {
  const g = await call('GET', `${PO}/${po.id}`, T[k]);
  check(3, `${k}: PO get has totalAmount/unitPrice/amount as JSON null (present, not a number)`, g.json.totalAmount === null && g.json.lines.every((l) => l.unitPrice === null && l.amount === null), `total=${g.json.totalAmount} unit=${g.json.lines[0].unitPrice}`);
  clean(`${k}: PO get raw body free of the marker and of money fields`, g);
  const l = await call('GET', `${PO}?search=${po.no.slice(-4)}`, T[k]); check(3, `${k}: PO list totalAmount null`, items(l).every((x) => x.totalAmount === null)); clean(`${k}: PO list raw body`, l);
  clean(`${k}: reorder suggestions`, await call('GET', `${PO}/suggestions?warehouseId=${F.whA}&search=SEC`, T[k]));
}
// receipt from the order by a caller without cost.view: no money in the draft, the stock is still valued at the order price
const d = await call('POST', `${PO}/${po.id}/receipt`, T.staff);
clean('staff: receipt created from the order (response)', d);
check(3, 'staff: draft lines carry unitCost null', d.json.lines.every((l) => l.unitCost === null) && d.json.totalAmount === null);
clean('staff: document get', await call('GET', `/api/documents/${d.json.id}`, T.staff));
clean('staff: document list', await call('GET', `/api/documents?pageSize=50`, T.staff));
clean('staff: PO detail with a linked receipt', await call('GET', `${PO}/${po.id}`, T.staff));
const adminDoc = await call('GET', `/api/documents/${d.json.id}`, T.admin);
check(3, 'control: the Administrator sees the PO price on the same draft (valuation uses it, D6)', adminDoc.json.lines[0].unitCost === 731.29, `unitCost=${adminDoc.json.lines[0].unitCost}`);
// cannot SET a price without cost.view
const body = (cost) => ({ warehouseId: d.json.warehouseId, partnerId: d.json.partnerId, remark: null, lines: d.json.lines.map((l) => ({ productId: l.productId, qty: l.qty, uomId: l.uomId, unitCost: cost, remark: null, purchaseOrderLineNo: l.purchaseOrderLineNo })) });
// not a 403: the field is accepted and discarded (DocumentLineBuilder: the typed cost is used only for a caller with cost.view). The proof is what is stored.
const put = await call('PUT', `/api/documents/${d.json.id}`, T.staff, body(1.11), { 'If-Match': etag(d) });
check(3, 'staff-typed unit cost on the linked draft is discarded (stored cost is still the order price)', put.status === 200 && (await call('GET', `/api/documents/${d.json.id}`, T.admin)).json.lines[0].unitCost === 731.29, `PUT ${put.status}`);
const plain = await call('POST', '/api/documents/receipts', T.staff, { warehouseId: F.whA, partnerId: F.supplier, lines: [{ productId: F.P.a, qty: 1, uomId: null, unitCost: 1.11, remark: null }] });
check(3, 'staff-typed unit cost on a plain receipt is discarded (stored null, valued at average)', plain.status === 200 && (await call('GET', `/api/documents/${plain.json.id}`, T.admin)).json.lines[0].unitCost === null, `POST ${plain.status}`);
expect(3, 'staff cannot create a purchase order with prices', await call('POST', PO, T.staff, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F)] }), 403);
expect(3, 'a po.read-only caller cannot create one either', await call('POST', PO, T.poRead, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F)] }), 403);
const posted = await postReceipt(T, d.json); check(3, 'staff-made receipt posts at the order price', posted.status === 200, `${posted.status}`);
const led = await call('GET', `/api/stock/ledger?pageSize=5&search=${F.P.a}`, T.admin);
check(3, 'ledger unit cost of the posting equals the order price (admin view)', JSON.stringify(led.json).includes('731.29'), led.status + '');
clean('staff: ledger/stock endpoints they may read', await call('GET', `/api/stock?pageSize=20`, T.staff));
// approval trigger amounts for an approver without cost.view
const big = (await call('POST', PO, T.clerk, { warehouseId: F.whA, supplierId: F.supplier, lines: [line(F, { qty: 3, unitPrice: 731.29 + 1999 })] })).json;
await act(T.clerk, big.id, 'submit');
const bs = await call('GET', `${PO}/${big.id}`, T.reviewer);
check(3, 'approver without cost.view: approvalTriggers carry no amounts, order total null', moneyPaths(bs.json).length === 0 && bs.json.totalAmount === null, JSON.stringify(bs.json.approvalTriggers).slice(0, 200));
const wc = await call('GET', `${PO}/${big.id}`, T.supervisor);
check(3, 'control: trigger text for a cost.view holder', wc.json.approvalTriggers.length > 0, JSON.stringify(wc.json.approvalTriggers).slice(0, 200));
expect(3, 'approver without cost.view can still approve (the tier decides, not the view)', await act(T.reviewer, big.id, 'approve'), 200, 403);
// audit log: a reader without cost.view
const au = await call('GET', `/api/audit?pageSize=100`, T.auditor);
clean('auditor (no cost.view): audit log page', au);
const audMarker = await call('GET', `/api/audit?pageSize=200&search=${po.no}`, T.auditor); clean('auditor: audit entries of the marker order', audMarker);
const auAdmin = await call('GET', `/api/audit?pageSize=200&search=${po.no}`, T.admin); check(3, 'control: audit of the marker order exists for the admin', items(auAdmin).length > 0, `${items(auAdmin).length} entries`);
// over-receipt 409 body: quantities only. Both drafts are submitted while the whole quantity is still open; the first post then makes the second one too large.
const oo = await approvedPo(F, T, { lines: [line(F, { qty: 6, unitPrice: 731.29 })] });
const r1 = (await receiptDraft(F, T, oo.id, { qty: 4 })).json, r2 = (await receiptDraft(F, T, oo.id)).json;
await docAct(T.staff, r1.id, 'submit'); await docAct(T.staff, r2.id, 'submit');
expect(3, 'first receipt (4 of 6) posts', await docAct(T.supervisor, r1.id, 'post'), 200);
const over = await docAct(T.staff, r2.id, 'post'); expect(3, 'Warehouse staff: second receipt (6 of the 2 left) is refused with 409', over, 409);
check(3, 'over_receipt body names quantities and no money', over.json.code === 'over_receipt' && JSON.stringify(over.json).includes('ordered') && !over.text.includes('731') && moneyPaths(over.json).length === 0, over.text.slice(0, 300));
const noRead = await roleUser('postnoread2', ['masterdata.read', 'stock.read', 'document.create', 'document.post']);
const gen = await docAct(noRead, r2.id, 'post'); check(3, 'a poster without po.read gets a general 409 (no order number, figures or lines)', gen.status === 409 && !gen.text.includes('PO-') && !gen.text.includes('731') && !JSON.stringify(gen.json).includes('lines'), gen.text.slice(0, 220));
console.log(failures() ? `${failures()} FAIL` : 'p3 all ok');
