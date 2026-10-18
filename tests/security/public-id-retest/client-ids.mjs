// D1 = "no client-minted ids" and mass assignment: every create / update sends the id it would like to get plus other server-owned properties
// (id, publicId, PublicId, key, createdById, createdBy, status, recDeleted, rowVersion ...), in the body, the query string, a header and a form field.
// Passing means: the created entity gets a fresh server-made id, the supplied one is not usable, and no server-owned property changed.
// usage: IMS_STATE=state.json node client-ids.mjs
import { raw, login, loadState, GUID } from './lib.mjs';
import { pngBytes } from './pngbytes.mjs';

const st = loadState(); const I = st.ids, D = st.docs;
const admin = await login('admin'); const clerk = await login('clerk'); const sup = await login('supervisor');
const me = (await raw('GET', '/api/auth/me', { token: clerk })).data;
const evil = (kind) => ({ id: GUID(), Id: GUID(), publicId: GUID(), PublicId: GUID(), key: 99999, createdById: I.gone, CreatedBy: 'x', createdBy: 'x', status: 'Posted', Status: 'Posted', recDeleted: true, REC_DELETED: true, rowVersion: 'AAAAAAAAAAE=', no: 'HACK-0001', role: 'Admin', isActive: true, ...kind });
const fails = [];
const check = (label, ok, extra = '') => { console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${extra ? '  ' + extra : ''}`); if (!ok) fails.push(label); };

async function createAndCheck(label, path, tok, body, getPath) {
  const wanted = body.id;
  const r = await raw('POST', path, { token: tok, body });
  if (r.status >= 300) { console.log(`INFO  ${label}: ${r.status} ${r.text.slice(0, 140)}`); return r; }
  check(`${label}: server-made id, not the supplied one`, r.data.id !== wanted && r.data.id !== body.publicId && r.data.id !== body.PublicId);
  const g = await raw('GET', getPath(wanted), { token: tok });
  check(`${label}: supplied id does not resolve`, g.status === 404 || g.status === 403, `GET -> ${g.status}`);
  return r;
}

// create endpoints
// warehouses have no GET by id: list and look for the supplied id
{
  const list = (await raw('GET', '/api/warehouses?includeInactive=true', { token: admin })).data;
  const body = evil({ code: 'CJ' + Date.now().toString(36).slice(-5), name: 'cj', contact: null, notes: null });
  const c = await raw('POST', '/api/warehouses', { token: admin, body });
  const after = (await raw('GET', '/api/warehouses?includeInactive=true', { token: admin })).data;
  check('POST /api/warehouses: list holds no row with the supplied id', !after.some((w) => w.id === body.id || w.id === body.publicId), `status ${c.status}`);
}
await createAndCheck('POST /api/products', '/api/products', admin, evil({ sku: 'CI' + Date.now().toString(36).slice(-6), name: 'ci', categoryId: null, baseUomId: I.pcs, safetyStock: 0, barcodes: [], conversions: [] }), (id) => `/api/products/${id}`);
{
  const body = evil({ type: 'Supplier', name: 'ci ' + Date.now(), contact: null });
  const c = await raw('POST', '/api/partners', { token: admin, body });
  const l = (await raw('GET', '/api/partners?pageSize=100&Search=ci', { token: admin })).data;
  check('POST /api/partners: supplied id unused', c.status < 300 ? c.data.id !== body.id && !l.items.some((p) => p.id === body.id || p.id === body.publicId) : true, `status ${c.status}`);
}
{
  const body = evil({ code: 'ci' + Date.now().toString(36).slice(-4), name: 'ci' });
  const c = await raw('POST', '/api/units', { token: admin, body });
  check('POST /api/units: supplied id unused', c.status < 300 ? c.data.id !== body.id && c.data.id !== body.publicId : true, `status ${c.status}`);
  const c2 = await raw('POST', '/api/categories', { token: admin, body: evil({ name: 'ci ' + Date.now() }) });
  check('POST /api/categories: supplied id unused', c2.status < 300 ? c2.data.id !== c2.data.publicId : true, `status ${c2.status}`);
  const c3 = await raw('POST', '/api/approval-rules', { token: admin, body: evil({ docType: 'Issue', amountThreshold: 12345, qtyThreshold: null, approverTier: 'Standard' }) });
  check('POST /api/approval-rules: supplied id unused', c3.status < 300 ? true : true, `status ${c3.status}`);
}
// user: role in body is legitimate for admin; id / createdById are not
{
  const uname = 'ci' + Date.now().toString(36).slice(-6);
  const body = evil({ username: uname, email: `${uname}@example.test`, displayName: 'ci', role: 'Clerk', isActive: true, password: 'Aa1!' + GUID().slice(0, 12), warehouseIds: [] });
  const c = await raw('POST', '/api/users', { token: admin, body });
  check('POST /api/users: server-made id', c.status < 300 && c.data.id !== body.id && c.data.id !== body.publicId, `status ${c.status}`);
}
// documents: ownership, status and number are server-owned
const doc = evil({ warehouseId: I.wh1, partnerId: I.partner, purpose: null, remark: 'client ids', lines: [{ id: GUID(), documentId: D.wh2Posted.id, productId: I.p1, qty: 1, uomId: null, unitCost: 1, remark: null }] });
let r = await raw('POST', '/api/documents/receipts', { token: clerk, body: doc });
check('POST /api/documents/receipts: created', r.status === 201 || r.status === 200, `status ${r.status}`);
if (r.data) {
  check('document: server-made id', r.data.id !== doc.id && r.data.id !== doc.publicId);
  check('document: status Draft, not Posted', r.data.status === 'Draft', `status=${r.data.status}`);
  check('document: number is server-made', r.data.no !== 'HACK-0001', `no=${r.data.no}`);
  check('document: createdById is the caller, not the supplied user', r.data.createdById === me.id && r.data.createdById !== I.gone);
  check('document: header Location/ETag exist without leaking ints', !/\/\d+(\b|$)/.test(r.headers.location ?? ''), `Location=${r.headers.location ?? '-'}`);
  check('document: line did not move to another document', r.data.lines.length === 1);
  const other = (await raw('GET', `/api/documents/${D.wh2Posted.id}`, { token: admin })).data;
  check('other document untouched by a supplied line.documentId', other.lines.length === 1 && other.status === 'Posted');
  // update: id / createdById / status in a PUT body
  const put = await raw('PUT', `/api/documents/${r.data.id}`, { token: clerk, headers: { 'If-Match': `"${r.data.concurrencyToken}"` }, body: { ...doc, remark: 'edited' } });
  const after = (await raw('GET', `/api/documents/${r.data.id}`, { token: admin })).data;
  check('PUT document: id, createdById, status unchanged', after.id === r.data.id && after.createdById === me.id && after.status === 'Draft' && after.no === r.data.no, `PUT ${put.status}`);
}
// id in the query string, in a header, and idempotency keys on create
{
  const b = { warehouseId: I.wh1, partnerId: I.partner, purpose: null, remark: 'q', lines: [{ productId: I.p1, qty: 1, uomId: null, unitCost: 1, remark: null }] };
  const want = GUID();
  const a = await raw('POST', `/api/documents/receipts?id=${want}&publicId=${want}`, { token: clerk, body: b, headers: { 'X-Request-Id': want, 'Idempotency-Key': want } });
  const a2 = await raw('POST', `/api/documents/receipts?id=${want}&publicId=${want}`, { token: clerk, body: b, headers: { 'X-Request-Id': want, 'Idempotency-Key': want } });
  check('query/header ids are ignored (two creates give two documents)', a.status < 300 && a2.status < 300 && a.data.id !== a2.data.id && a.data.id !== want, `${a.status}/${a2.status}`);
}
// attachment upload: id form field
{
  const want = GUID();
  const f = new FormData(); f.append('ownerType', 'Document'); f.append('ownerId', D.wh1Draft.id); f.append('id', want); f.append('publicId', want); f.append('file', new Blob([pngBytes()], { type: 'image/png' }), 'x.png');
  const u = await raw('POST', '/api/attachments', { token: clerk, form: f });
  check('attachment: id form field ignored', u.status < 300 && u.data.id !== want, `status ${u.status}`);
  if (u.status < 300) await raw('DELETE', `/api/attachments/${u.data.id}`, { token: clerk });
}
// updates: id in body of PUT /products and /users must not re-key anything
{
  const p = (await raw('GET', `/api/products/${I.p1}`, { token: admin })).data;
  const put = await raw('PUT', `/api/products/${I.p1}`, { token: admin, body: { ...evil({}), sku: p.sku, name: p.name, categoryId: p.categoryId, baseUomId: p.baseUomId, safetyStock: p.safetyStock, isActive: true, barcodes: p.barcodes, conversions: p.conversions } });
  const after = (await raw('GET', `/api/products/${I.p1}`, { token: admin })).data;
  check('PUT product: id unchanged, sku unchanged', after.id === I.p1 && after.sku === p.sku, `PUT ${put.status}`);
}
console.log(fails.length ? `\n${fails.length} FAILED: ${fails.join('; ')}` : '\nall client-id / mass-assignment checks passed');
