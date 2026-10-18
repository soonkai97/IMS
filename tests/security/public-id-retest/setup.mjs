// Seeds a throw-away database through the public API: second and third warehouse, scoped users in each warehouse, a user with no warehouse,
// products, drafts / submitted / posted documents, attachments. Writes the resulting ids to $IMS_STATE (a file outside the repository).
// Afterwards run softdelete.sql (sqlcmd) to mark the "gone" rows soft-deleted.
import { raw, login, loadState, saveState } from './lib.mjs';
import { pngBytes } from './pngbytes.mjs';
import { randomBytes } from 'node:crypto';

const must = (r, what) => { if (r.status >= 300) throw new Error(`${what} -> ${r.status} ${r.text.slice(0, 200)}`); return r.data; };


const st = loadState();
st.createdPassword ??= 'Pt!' + randomBytes(12).toString('base64url');   // used for throw-away users only
saveState(st);
const T = {};
for (const u of ['admin', 'owner', 'supervisor', 'staff', 'clerk']) T[u] = await login(u);

const warehouses = must(await raw('GET', '/api/warehouses', { token: T.admin }), 'warehouses');
const wh1 = warehouses.find((w) => w.code === 'WH01');
const wh2 = warehouses.find((w) => w.code === 'WH02') ?? must(await raw('POST', '/api/warehouses', { token: T.admin, body: { code: 'WH02', name: 'Second', isActive: true } }), 'wh2');
const wh3 = warehouses.find((w) => w.code === 'WH03') ?? must(await raw('POST', '/api/warehouses', { token: T.admin, body: { code: 'WH03', name: 'Third (soft-deleted later)', isActive: true } }), 'wh3');

const users = must(await raw('GET', '/api/users?pageSize=100', { token: T.admin }), 'users').items;
const mk = async (username, role, whs) => {
  const body = { username, email: `${username}@example.test`, displayName: username, role, isActive: true, password: st.createdPassword, warehouseIds: whs };
  const existing = users.find((x) => x.username === username);
  if (existing) return must(await raw("PUT", `/api/users/${existing.id}`, { token: T.admin, body }), "reset " + username);
  return must(await raw("POST", "/api/users", { token: T.admin, body }), "user " + username);
};
const staff2 = await mk('staff2', 'WarehouseStaff', [wh2.id]);
const noscope = await mk('noscope', 'WarehouseStaff', []);
const sup2 = await mk('supervisor2', 'Supervisor', [wh2.id]);
const clerk2 = await mk('clerk2', 'Clerk', [wh2.id]);
const gone = await mk('gone', 'Clerk', [wh1.id]);
for (const u of ['staff2', 'noscope', 'supervisor2', 'clerk2']) T[u] = await login(u, 'created');

const units = must(await raw('GET', '/api/units', { token: T.admin }), 'units');
const pcs = units.find((u) => u.code === 'PCS');
const goneUom = units.find((u) => u.code === 'ZZGONE') ?? must(await raw('POST', '/api/units', { token: T.admin, body: { code: 'ZZGONE', name: 'Gone unit' } }), 'uom');
const cats = must(await raw('GET', '/api/categories', { token: T.admin }), 'cats');
const goneCat = cats.find((c) => c.name === 'ZZ Gone category') ?? must(await raw('POST', '/api/categories', { token: T.admin, body: { name: 'ZZ Gone category' } }), 'cat');
const partners = must(await raw('GET', '/api/partners?pageSize=100', { token: T.admin }), 'partners').items;
const sup = partners.find((p) => p.type === 'Supplier' && p.name !== 'ZZ Gone partner');
const gonePartner = partners.find((p) => p.name === 'ZZ Gone partner') ?? must(await raw('POST', '/api/partners', { token: T.admin, body: { type: 'Supplier', name: 'ZZ Gone partner', isActive: true } }), 'partner');
const prods = must(await raw('GET', '/api/products?pageSize=100', { token: T.admin }), 'products').items;
const p1 = prods.find((p) => p.sku === 'DEMO-001');
const goneProd = prods.find((p) => p.sku === 'ZZ-GONE') ?? must(await raw('POST', '/api/products', { token: T.admin, body: { sku: 'ZZ-GONE', name: 'Gone product', baseUomId: pcs.id, safetyStock: 0, isActive: true, barcodes: [], conversions: [] } }), 'prod');
const rules = must(await raw('GET', '/api/approval-rules', { token: T.admin }), 'rules');
const goneRule = rules.find((r) => r.amountThreshold === 77777) ?? must(await raw('POST', '/api/approval-rules', { token: T.admin, body: { docType: 'Issue', amountThreshold: 77777, qtyThreshold: null, approverTier: 'Standard', isActive: true } }), 'rule');

const line = (qty = 10) => ({ productId: p1.id, qty, uomId: null, unitCost: 2, remark: null });
const receipt = async (tok, wh, remark) => must(await raw('POST', '/api/documents/receipts', { token: tok, body: { warehouseId: wh.id, partnerId: sup.id, purpose: null, remark, lines: [line()] } }), 'receipt ' + remark);
const docs = {};
docs.wh1Draft = await receipt(T.clerk, wh1, 'wh1 draft');
docs.wh1Posted = await receipt(T.admin, wh1, 'wh1 posted');
docs.wh2Draft = await receipt(T.admin, wh2, 'wh2 draft');
docs.wh2Posted = await receipt(T.admin, wh2, 'wh2 posted');
docs.goneDoc = await receipt(T.admin, wh1, 'to be soft deleted');
docs.wh2Submitted = await receipt(T.admin, wh2, 'wh2 submitted');
const act = async (tok, d, a) => raw('POST', `/api/documents/${d.id}/${a}`, { token: tok, headers: { 'If-Match': `"${d.concurrencyToken}"` } });
for (const k of ['wh1Posted', 'wh2Posted']) {
  let r = await act(T.admin, docs[k], 'submit'); if (r.status >= 300) console.log('submit', k, r.status, r.text.slice(0, 150));
  const d = (await raw('GET', `/api/documents/${docs[k].id}`, { token: T.admin })).data;
  r = await act(T.admin, d, 'post'); if (r.status >= 300) console.log('post', k, r.status, r.text.slice(0, 150));
}
{ const r = await act(T.admin, docs.wh2Submitted, 'submit'); if (r.status >= 300) console.log('submit wh2Submitted', r.status, r.text.slice(0, 150)); }

const upload = async (tok, ownerType, ownerId, name = 'a.png') => {
  const f = new FormData(); f.append('ownerType', ownerType); f.append('ownerId', ownerId); f.append('file', new Blob([pngBytes()], { type: 'image/png' }), name);
  return raw('POST', '/api/attachments', { token: tok, form: f });
};
const att = {};
att.wh1Doc = must(await upload(T.clerk, 'Document', docs.wh1Draft.id), 'att1');
att.wh2Doc = must(await upload(T.admin, 'Document', docs.wh2Draft.id), 'att2');
att.product = must(await upload(T.admin, 'Product', p1.id), 'att3');
att.goneAtt = must(await upload(T.admin, 'Document', docs.wh1Draft.id, 'gone.png'), 'att4');

Object.assign(st, {
  ids: { wh1: wh1.id, wh2: wh2.id, wh3: wh3.id, staff2: staff2.id, noscope: noscope.id, sup2: sup2.id, clerk2: clerk2.id, gone: gone.id, pcs: pcs.id, goneUom: goneUom.id, goneCat: goneCat.id, partner: sup.id, gonePartner: gonePartner.id, p1: p1.id, goneProd: goneProd.id, goneRule: goneRule.id, goneDoc: docs.goneDoc.id, goneAtt: att.goneAtt.id },
  docs: Object.fromEntries(Object.entries(docs).map(([k, v]) => [k, { id: v.id, no: v.no }])),
  att: Object.fromEntries(Object.entries(att).map(([k, v]) => [k, v.id])),
});
saveState(st);
console.log('ok', Object.keys(st.ids).length, 'ids,', Object.keys(st.docs).length, 'docs,', Object.keys(st.att).length, 'attachments');
