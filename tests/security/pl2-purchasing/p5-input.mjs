// Area 5: mass assignment and input handling on the purchase-order endpoints.
import zlib from 'node:zlib';
import { call, tokens, fx, saveFx, expect, check, failures, useAdmin, PO, act, newPo, line, approvedPo, postReceipt, receiptDraft, items, etag, BASE, tag, docAct } from './lib.mjs';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const no500 = (label, r) => check(5, label, r.status >= 400 && r.status < 500 && !/Exception|StackTrace|at Ims\.|System\.|SqlClient|SELECT |INSERT /i.test(r.text), `${r.status} ${String(r.json?.detail ?? r.text).slice(0, 110)}`);
const body = (o = {}) => ({ warehouseId: F.whA, supplierId: F.supplier, remark: 'SEC input', lines: [line(F)], ...o });

// ---- mass assignment: unknown / forbidden properties are ignored
const extra = { id: '11111111-1111-1111-1111-111111111111', status: 'Approved', no: 'PO-HACK-0001', totalAmount: 1, totalQty: 99999, requiresApproval: false, isLarge: false, isReturned: true, createdById: F.U.none.id, createdBy: 'admin',
  approvedBy: 'owner', approvedAt: '2020-01-01T00:00:00Z', closeKind: 'Received', cancelReason: 'x', concurrencyToken: 'AAAA', rowVersion: 'AAAA', allowedActions: ['approve'], receipts: [], recDeleted: true, isActive: false };
const c = await call('POST', PO, T.clerk, { ...body(), ...extra, lines: [{ ...line(F), amount: 1, baseQty: 999, lineNo: 77, receivedQty: 10, remainingQty: 0, unitCost: 1, purchaseOrderLineNo: 5 }] });
expect(5, 'create with 20 extra properties -> 200', c, 200);
const p = c.json;
check(5, 'none of them took effect (Draft, generated number, real totals, own creator, line 1, nothing received)', p.status === 'Draft' && p.id !== extra.id && /^PO-WH01-\d{4}-\d{4}$/.test(p.no) && p.totalAmount === 125 && p.totalQty === 10 && p.createdBy === 'clerk' &&
  !p.isReturned && !p.approvedBy && p.closeKind === null && p.cancelReason === null && p.lines[0].lineNo === 1 && p.lines[0].baseQty === 10 && p.lines[0].amount === 125 && p.lines[0].receivedQty === 0, `${p.no} ${p.status}`);
const u = await call('PUT', `${PO}/${p.id}`, T.clerk, { ...body(), status: 'Closed', no: 'X', warehouseId: F.whB, createdById: F.U.none.id }, { 'If-Match': await tag(p.id) });
check(5, 'update with status/no/other warehouse in the body: warehouse is the one in the body only if allowed, status and no unchanged', u.status === 200 ? (u.json.status === 'Draft' && u.json.no === p.no && u.json.warehouseId === F.whA) : u.status === 400, `${u.status} ${u.json?.status ?? ''} ${u.json?.warehouseName ?? ''}`);
const sp = await approvedPo(F, T);
const rr = await call('POST', '/api/documents/receipts', T.staff, { warehouseId: F.whA, partnerId: F.supplier, purchaseOrderId: sp.id, purchaseOrder: { id: sp.id, no: sp.no }, lines: [{ productId: F.P.a, qty: 1, uomId: null, remark: null }] });
check(5, 'ordinary receipt body naming purchaseOrderId/purchaseOrder is ignored (no link)', rr.status === 200 && !rr.json.purchaseOrder, `${rr.status} ${JSON.stringify(rr.json.purchaseOrder)}`);
expect(5, 'ordinary receipt line naming a purchaseOrderLineNo -> 400', await call('POST', '/api/documents/receipts', T.staff, { warehouseId: F.whA, partnerId: F.supplier, lines: [{ productId: F.P.a, qty: 1, uomId: null, remark: null, purchaseOrderLineNo: 1 }] }), 400);
const link = (await receiptDraft(F, T, sp.id)).json;
const forged = await call('PUT', `/api/documents/${link.id}`, T.staff, { warehouseId: link.warehouseId, partnerId: link.partnerId, purchaseOrderId: ghostId(), lines: link.lines.map((l) => ({ productId: l.productId, qty: l.qty, uomId: l.uomId, remark: null, purchaseOrderLineNo: 9 })) }, { 'If-Match': etag(await call('GET', `/api/documents/${link.id}`, T.admin)) });
expect(5, 'linked draft naming a PO line number that does not exist -> 400', forged, 400);
function ghostId() { return '22222222-2222-2222-2222-222222222222'; }
const other = await approvedPo(F, T, { lines: [line(F, { productId: F.P.b })] });
const swap = await call('PUT', `/api/documents/${link.id}`, T.staff, { warehouseId: link.warehouseId, partnerId: link.partnerId, lines: [{ productId: F.P.b, qty: 1, uomId: null, remark: null, purchaseOrderLineNo: 1 }] }, { 'If-Match': etag(await call('GET', `/api/documents/${link.id}`, T.admin)) });
expect(5, 'linked draft with a line for a product that is not on that order line -> 400', swap, 400);
const ap = await act(T.supervisor, (await newPo(T.clerk, F, { lines: [line(F, { unitPrice: 600 })] }).then(async (x) => (await act(T.clerk, x.json.id, 'submit')).json)).id, 'approve', { approverId: 1, status: 'Approved', isLarge: true });
expect(5, 'approve with extra body properties still follows the rules (200 for a Standard order)', ap, 200);

// ---- shape and size
const bad = [['lines [null]', body({ lines: [null] })], ['lines null', body({ lines: null })], ['lines {}', body({ lines: {} })], ['lines empty', body({ lines: [] })], ['501 lines', body({ lines: Array.from({ length: 501 }, () => line(F)) })],
  ['qty 0', body({ lines: [line(F, { qty: 0 })] })], ['qty negative', body({ lines: [line(F, { qty: -1 })] })], ['qty 5 decimals', body({ lines: [line(F, { qty: 1.00001 })] })], ['qty 1e30', body({ lines: [line(F, { qty: 1e30 })] })],
  ['price negative', body({ lines: [line(F, { unitPrice: -0.01 })] })], ['price 3 decimals', body({ lines: [line(F, { unitPrice: 1.001 })] })], ['price 1e30', body({ lines: [line(F, { unitPrice: 1e30 })] })],
  ['amount over the column (qty x price)', body({ lines: [line(F, { qty: 1e9, unitPrice: 1e9 })] })], ['qty as text', body({ lines: [line(F, { qty: 'abc' })] })], ['productId not a GUID', body({ lines: [line(F, { productId: 'x' })] })],
  ['warehouseId empty GUID', body({ warehouseId: '00000000-0000-0000-0000-000000000000' })], ['remark 100k characters', body({ remark: 'x'.repeat(100000) })], ['line remark 100k characters', body({ lines: [line(F, { remark: 'x'.repeat(100000) })] })]];
for (const [n, b] of bad) no500(`create refused cleanly: ${n}`, await call('POST', PO, T.clerk, b));
for (const [n, raw] of [['decimal overflow literal', '{"warehouseId":"' + F.whA + '","supplierId":"' + F.supplier + '","lines":[{"productId":"' + F.P.a + '","qty":79228162514264337593543950336,"unitPrice":1}]}'], ['not JSON', '{{{'], ['empty body', ''], ['array body', '[]'], ['NaN', '{"lines":[{"qty":NaN}]}'], ['duplicate keys', '{"warehouseId":"x","warehouseId":"y"}'], ['depth 20000', '['.repeat(20000)]]) no500(`raw body refused cleanly: ${n}`, await call('POST', PO, T.clerk, raw));
const huge = await call('POST', PO, T.clerk, JSON.stringify(body({ remark: 'x'.repeat(8 * 1024 * 1024) }))); check(5, 'an 8 MB body is refused (413/400), not stored', [400, 413].includes(huge.status), `${huge.status}`);
expect(5, 'wrong content type -> 415/400', await call('POST', PO, T.clerk, JSON.stringify(body()), { 'Content-Type': 'text/plain' }), 400, 415);
{ const t0 = Date.now(); const big = await call('POST', PO, T.clerk, body({ lines: Array.from({ length: 500 }, (_, i) => line(F, { remark: 'L' + i })) })); const g = big.status === 200 ? await call('GET', `${PO}/${big.json.id}`, T.clerk) : big;
  check(5, '500 lines (the limit) are accepted and read back within 5 s', big.status === 200 && g.status === 200 && g.json.lines.length === 500 && Date.now() - t0 < 5000, `${big.status} ${Date.now() - t0} ms`); if (big.status === 200) await act(T.clerk, big.json.id, 'cancel', {}); }
expect(5, 'suggestions without warehouseId -> 400', await call('GET', `${PO}/suggestions`, T.admin), 400);
const d0 = (await newPo(T.clerk, F, { lines: [line(F, { unitPrice: 0 })] })); expect(5, 'price 0.00 saves on a draft', d0, 200);
expect(5, 'but submit of a price-0 line -> 400', await act(T.clerk, d0.json.id, 'submit'), 400);

// ---- text: unicode, control characters, markup
for (const [n, s] of [['NUL', 'a\u0000b'], ['bidi override', 'abc‮def'], ['CRLF + header-looking text', 'x\r\nSet-Cookie: pwn=1\r\nX-Evil: 1'], ['markup', '<img src=x onerror=alert(1)>'], ['emoji and ZWJ', '\u{1F600}‍\u{1F468}'], ['unpaired surrogate', 'a\ud800b'], ['SQL quote', "'; DROP TABLE PurchaseOrder;--"]]) {
  const r = await call('POST', PO, T.clerk, body({ remark: s }));
  check(5, `remark with ${n}: refused cleanly or stored inert`, (r.status === 200 && !r.headers.get('set-cookie') && !r.headers.get('x-evil') && /json/.test(r.headers.get('content-type'))) || (r.status === 400), `${r.status} ct=${r.headers.get('content-type')}`);
}
check(5, 'nosniff header on JSON', (await call('GET', PO, T.clerk)).headers.get('x-content-type-options') === 'nosniff');

// ---- search and filters (parameterised; LIKE wildcards are literal)
const all = (await call('GET', `${PO}?pageSize=1`, T.admin)).json.total;
for (const q of ["'; DROP TABLE PurchaseOrder;--", "' OR 1=1--", '%%%', '___', '[a-z]', '\\\\\\', 'x\u0000y']) {
  const r = await call('GET', `${PO}?search=${encodeURIComponent(q)}`, T.admin);
  check(5, `search ${JSON.stringify(q)}: no error, does not match everything`, r.status === 200 && r.json.total < all, `${r.status} total=${r.json.total} of ${all}`);
}
check(5, 'table still there after the injection strings', (await call('GET', `${PO}?pageSize=1`, T.admin)).json.total >= all);
for (const [n, qs] of [['status=99', 'status=99'], ['status=Draft;DROP', "status=Draft'"], ['page=-1', 'page=-1'], ['pageSize=0', 'pageSize=0'], ['pageSize=1000000', 'pageSize=1000000'], ['page=2147483647', 'page=2147483647'], ['cursor garbage', 'cursor=AAAA'], ['from=notadate', 'from=notadate'], ['supplierId=x', 'supplierId=x'], ['warehouseId=x', 'warehouseId=x']]) {
  const t0 = Date.now(); const r = await call('GET', `${PO}?${qs}`, T.admin);
  check(5, `list ${n}: clean answer (200 clamped or 4xx), quick`, r.status < 500 && Date.now() - t0 < 3000 && (r.status !== 200 || (r.json.items?.length ?? 0) <= 200), `${r.status} ${r.json?.items?.length ?? ''} items ${Date.now() - t0} ms`);
}
const pg = await call('GET', `${PO}?pageSize=1000000`, T.admin); check(5, 'pageSize is capped', r2(pg), `got ${pg.json.items?.length ?? pg.status}`);
function r2(r) { return r.status === 400 || (r.status === 200 && r.json.items.length <= 200); }

// ---- headers: correlation id, If-Match
for (const [n, v] of [['64+ characters', 'a'.repeat(200)], ['markup and spaces', 'x y"<b>\t'], ['unicode', 'ééé']]) {
  const r = await call('GET', `${PO}?pageSize=1`, T.admin, undefined, { 'X-Correlation-Id': v });
  const echoed = r.headers.get('x-correlation-id');
  check(5, `X-Correlation-Id ${n}: not echoed raw`, (r.status === 400 || r.status === 200) && (echoed === null || (echoed.length <= 64 && !/[<>" \t]/.test(echoed))), `echoed=${JSON.stringify(echoed)?.slice(0, 80)}`);
}
for (const v of ['*', 'W/"abc"', '"' + 'a'.repeat(5000) + '"', 'not quoted', '""']) {
  const r = await call('POST', `${PO}/${d0.json.id}/cancel`, T.clerk, {}, { 'If-Match': v }); check(5, `If-Match ${v.slice(0, 12)}: never 200/500`, r.status >= 400 && r.status < 500 && !(r.status === 200), `${r.status}`);
}
expect(5, 'missing If-Match -> 428', await call('POST', `${PO}/${d0.json.id}/cancel`, T.clerk, {}), 428);

console.log(failures() ? `${failures()} FAIL` : 'p5 all ok');

function unzip(b) { // minimal central-directory reader, enough for an xlsx
  let e = b.length - 22; while (e > 0 && b.readUInt32LE(e) !== 0x06054b50) e--;
  const n = b.readUInt16LE(e + 10); let p = b.readUInt32LE(e + 16); const out = {};
  for (let i = 0; i < n; i++) {
    const method = b.readUInt16LE(p + 10), csize = b.readUInt32LE(p + 20), nl = b.readUInt16LE(p + 28), el = b.readUInt16LE(p + 30), cl = b.readUInt16LE(p + 32), off = b.readUInt32LE(p + 42);
    const name = b.toString('utf8', p + 46, p + 46 + nl), ds = off + 30 + b.readUInt16LE(off + 26) + b.readUInt16LE(off + 28), data = b.subarray(ds, ds + csize);
    out[name] = method === 8 ? zlib.inflateRawSync(data) : data; p += 46 + nl + el + cl;
  }
  return out;
}
