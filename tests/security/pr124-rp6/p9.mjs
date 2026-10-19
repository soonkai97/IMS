// Callers of the other locks (document numbering, stock) after the locker change: 20 parallel receipts on one product, then user create under a held roles lock (hold it first for the second part).
import { call, login, sql } from './lib.mjs';
import { setup } from './adm.mjs';
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const admin = (await login('admin')).token;
const sfx = Date.now().toString().slice(-6);
const wh = (await call('GET', '/api/warehouses', admin)).json[0];
const uom = (await call('GET', '/api/units', admin)).json.find(u => u.code === 'PCS');
const p = (await call('POST', '/api/products', admin, { sku: `L${sfx}`, name: `Lock ${sfx}`, baseUomId: uom.id, safetyStock: 1, isActive: true, barcodes: [`77${sfx}`] })).json;
const supplier = (await call('GET', '/api/partners?type=Supplier', admin)).json.items[0];
const etag = async (id) => (await call('GET', '/api/documents/' + id, admin)).headers.get('etag');
const act = async (id, a) => call('POST', `/api/documents/${id}/${a}`, admin, undefined, { 'If-Match': await etag(id) });
const one = async () => {
  const d = (await call('POST', '/api/documents/receipts', admin, { warehouseId: wh.id, partnerId: supplier.id, lines: [{ productId: p.id, qty: 1, unitCost: 1 }] })).json;
  const s = await act(d.id, 'submit');
  const ap = s.json.status === 'Approved' ? s : await act(d.id, 'approve');
  const po = await act(d.id, 'post');
  return { no: d.no, s: [s.status, ap.status, po.status].join('/') };
};
const N = 20;
const rs = await Promise.all(Array.from({ length: N }, one));
const hist = rs.reduce((m, r) => ((m[r.s] = (m[r.s] ?? 0) + 1), m), {});
console.log('parallel receipts', JSON.stringify(hist), 'unique numbers', new Set(rs.map(r => r.no)).size, 'of', N);
const qty = (await call('GET', `/api/stock?productId=${p.id}`, admin)).json.items[0]?.qtyOnHand;
console.log('stock on hand', qty, '(expected', N, 'if all posted)');
