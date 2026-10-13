// PR-6 review minor M1: ShortageNamingBehavior falls back to the original message (with INTEGER product and warehouse keys) when the product cannot be
// named (soft-deleted). Reproduces it on a throw-away database: a product with 1 unit on hand, a draft issue of 5, the product soft-deleted by SQL,
// then post. Step 1 prints the sqlcmd line to run; run again with STEP=2.   usage: STEP=1|2 IMS_STATE=state.json node shortage-fallback.mjs
import { raw, login, loadState, saveState } from './lib.mjs';

const st = loadState(); const I = st.ids; const admin = await login('admin');
if (process.env.STEP !== '2') {
  const sku = 'LEAK' + Date.now().toString(36).slice(-5);
  const p = (await raw('POST', '/api/products', { token: admin, body: { sku, name: 'leak probe', categoryId: null, baseUomId: I.pcs, safetyStock: 0, isActive: true, barcodes: [], conversions: [] } })).data;
  const line = (q) => ({ productId: p.id, qty: q, uomId: null, unitCost: 1, remark: null });
  const rc = (await raw('POST', '/api/documents/receipts', { token: admin, body: { warehouseId: I.wh1, partnerId: I.partner, purpose: null, remark: 'leak', lines: [line(1)] } })).data;
  await raw('POST', `/api/documents/${rc.id}/submit`, { token: admin, headers: { 'If-Match': `"${rc.concurrencyToken}"` } });
  const g = (await raw('GET', `/api/documents/${rc.id}`, { token: admin })).data;
  await raw('POST', `/api/documents/${rc.id}/post`, { token: admin, headers: { 'If-Match': `"${g.concurrencyToken}"` } });
  const cust = (await raw('GET', '/api/partners?Type=Customer&pageSize=5', { token: admin })).data.items[0].id; const isr = await raw('POST', '/api/documents/issues', { token: admin, body: { warehouseId: I.wh1, partnerId: cust, purpose: 'Use', remark: 'leak', lines: [line(5)] } }); if (isr.status >= 300) throw new Error('issue create ' + isr.status + ' ' + isr.text.slice(0, 200)); const is = isr.data;
  st.leak = { product: p.id, issue: is.id }; saveState(st);
  console.log(`-- run: UPDATE [Product] SET REC_DELETED = 1 WHERE PublicId = '${p.id}'  (sqlcmd -I, throw-away database), then STEP=2`);
} else {
  let d = (await raw('GET', `/api/documents/${st.leak.issue}`, { token: admin })).data;
  let r = await raw('POST', `/api/documents/${d.id}/submit`, { token: admin, headers: { 'If-Match': `"${d.concurrencyToken}"` } });
  d = (await raw('GET', `/api/documents/${d.id}`, { token: admin })).data;
  r = await raw('POST', `/api/documents/${d.id}/post`, { token: admin, headers: { 'If-Match': `"${d.concurrencyToken}"` } });
  console.log('post ->', r.status, r.text.slice(0, 400));
}
