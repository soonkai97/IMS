// Proof of concept: a warehouse-scoped user (staff2, warehouse WH02) measures activity in another warehouse (WH01) through the two global counters
// that still leave the API: LedgerRow.id (bigint identity of the stock ledger) and the document concurrencyToken / ETag (a SQL rowversion, one
// counter for the whole database). D2 of docs/architecture/adr-guid-keys.md says ids must not be countable.
// usage: IMS_STATE=state.json node counters.mjs
import { raw, login, loadState } from './lib.mjs';

const st = loadState(); const I = st.ids;
const admin = await login('admin'); const staff2 = await login('staff2', 'created'); const staff = await login('staff');
const rv = (b64) => Number(Buffer.from(b64, 'base64').readBigUInt64BE(0));
const own = async (tok, wh) => (await raw('POST', '/api/documents/receipts', { token: tok, body: { warehouseId: wh, partnerId: I.partner, purpose: null, remark: 'counter probe', lines: [{ productId: I.p1, qty: 1, uomId: null, unitCost: 1, remark: null }] } })).data;
const post = async (tok, d) => { await raw('POST', `/api/documents/${d.id}/submit`, { token: tok, headers: { 'If-Match': `"${d.concurrencyToken}"` } }); const g = (await raw('GET', `/api/documents/${d.id}`, { token: tok })).data; return raw('POST', `/api/documents/${d.id}/post`, { token: tok, headers: { 'If-Match': `"${g.concurrencyToken}"` } }); };
const maxLedger = async (tok) => { const l = (await raw('GET', '/api/stock/ledger?pageSize=5', { token: tok })).data; return { status: l.items ? 200 : 0, ids: l.items.map((x) => x.id), warehouses: [...new Set(l.items.map((x) => x.warehouseCode))] }; };

console.log('1) ledger row ids as seen by staff2 (WH02 only):', JSON.stringify(await maxLedger(staff2)));
const a = await own(staff2, I.wh2);                       // staff2 creates a draft: token A
const aLedgerBefore = (await maxLedger(staff2)).ids[0];
// activity in the OTHER warehouse by the admin: 3 documents posted in WH01 (each writes a ledger row, a balance, audit rows ...)
for (let i = 0; i < 3; i++) { const d = await own(admin, I.wh1); await post(admin, d); }
const b = await own(staff2, I.wh2);                       // staff2 creates another draft: token B
const aLedgerAfter = (await maxLedger(staff2)).ids[0];
console.log('2) staff2 rowversion token A =', rv(a.concurrencyToken), ' B =', rv(b.concurrencyToken), ' delta =', rv(b.concurrencyToken) - rv(a.concurrencyToken), '(admin wrote 3 posted documents in WH01 in between)');
// staff2 posts a WH02 document itself, to get a ledger id of its own before and after the other warehouse's activity
const p1 = await post(staff2, a); const l1 = (await maxLedger(staff2)).ids[0];
for (let i = 0; i < 4; i++) { const d = await own(admin, I.wh1); await post(admin, d); }
const c = await own(staff2, I.wh2); const p2 = await post(staff2, c); const l2 = (await maxLedger(staff2)).ids[0];
console.log('3) staff2 own ledger rows: first posting -> id', l1, '(HTTP', p1.status + '), second posting after admin posted 4 documents in WH01 -> id', l2, '(HTTP', p2.status + ') delta =', l2 - l1, '(4 foreign rows + 1 own = 5 expected)');
console.log('   staff2 never sees a WH01 row, but the ids it sees skip the foreign rows, so the gap counts them.');
const audit = await raw('GET', '/api/audit?pageSize=1', { token: staff }); console.log('4) /api/audit as WarehouseStaff ->', audit.status);
const led = await raw('GET', '/api/stock/ledger?pageSize=1', { token: staff }); console.log('5) /api/stock/ledger as WarehouseStaff ->', led.status, 'first row keys:', Object.keys(led.data.items[0] ?? {}).join(','));
