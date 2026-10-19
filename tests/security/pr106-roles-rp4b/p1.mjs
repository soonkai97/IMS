import { call, short, T } from './lib.mjs';
const wh = (await call('GET', '/api/warehouses', T.admin)).json[0].id;
const prods = (await call('GET', '/api/products', T.admin)).json;
const prod = (prods.items ?? prods)[0];
console.log('product', prod.sku, prod.id);
const settings = (await call('GET', '/api/settings', T.admin)).json;
console.log('settings keys', JSON.stringify(settings).slice(0,300));
const log = (...a) => console.log(...a);
const mkRule = async (body, who='admin') => { const r = await call('POST', '/api/approval-rules', T[who], body); log('  create rule', JSON.stringify(body), '->', short(r), r.status===200?JSON.stringify({t:r.json.approverTier,r:r.json.roleRequired,id:r.json.id}):''); return r.json; };
const doc = async (qty, who="clerk") => {
  const r = await call("POST", "/api/documents/receipts", T[who], { warehouseId: wh, lines: [{ productId: prod.id, qty, unitCost: 1 }] });
  if (r.status !== 200) { log("  create doc failed", short(r)); return null; }
  const s = await call("POST", `/api/documents/${r.json.id}/submit`, T[who], {}, {"If-Match": r.headers.get("etag")});
  log(`  doc qty ${qty} by ${who}: submit ${short(s)} status=${s.json.status} required=${s.json.requiresApproval} large=${s.json.isLarge}`);
  return { ...s.json, etag: s.headers.get("etag") };
};
const act = async (d, who, verb="approve", body={}) => call("POST", `/api/documents/${d.id}/${verb}`, T[who], verb==="reject"?{reason:"no"}:body, {"If-Match": d.etag});
log('== Q5: Admin-only rule via deprecated roleRequired');
const radm = await mkRule({ docType: 'Receipt', qtyThreshold: 500, roleRequired: 'Admin', isActive: true });
const rstd = await mkRule({ docType: 'Receipt', qtyThreshold: 100, roleRequired: 'Supervisor', isActive: true });
let d = await doc(600);
for (const who of ['staff','clerk','supervisor','owner']) { const r = await act(d, who); log('  approve by', who, '->', short(r)); if (r.status===200) break; }
d = await doc(600);
const ra = await act(d, 'admin'); log('  approve large doc by admin ->', short(ra));
log('== Standard doc (qty 150)');
d = await doc(150);
for (const who of ['staff','clerk','owner','supervisor']) { const r = await act(d, who); log('  approve by', who, '->', short(r)); if (r.status===200) break; }
log('== reject routing');
d = await doc(600); log('  reject large by supervisor ->', short(await act(d,'supervisor','reject'))); 
log('  reject large by owner ->', short(await act(d,'owner','reject')));
d = await doc(150); log('  reject std by owner ->', short(await act(d,'owner','reject')));
log('  reject std by supervisor ->', short(await act(d,'supervisor','reject')));
log('== rule tightened after submit: std doc, rule -> Large');
d = await doc(150);
const up = await call('PUT', `/api/approval-rules/${rstd.id}`, T.admin, { docType:'Receipt', qtyThreshold:100, approverTier:'Large', isActive:true });
log('  PUT rule tier Large ->', short(up), up.json.approverTier, up.json.roleRequired);
log('  supervisor approve stored-Standard doc under Large rule ->', short(await act(d,'supervisor')));
log('  owner approve stored-Standard doc ->', short(await act(d,'owner')));
log('  admin approve ->', short(await act(d,'admin')));
log('  (restore rule to Standard)');
await call('PUT', `/api/approval-rules/${rstd.id}`, T.admin, { docType:'Receipt', qtyThreshold:100, approverTier:'Standard', isActive:true });
require_state: {
  log('== rule loosened after submit: large doc, Large rules -> inactive');
  d = await doc(600);
  await call('PUT', `/api/approval-rules/${radm.id}`, T.admin, { docType:'Receipt', qtyThreshold:500, approverTier:'Large', isActive:false });
  log('  supervisor approve stored-Large doc, rule inactive ->', short(await act(d,'supervisor')));
  log('  owner approve ->', short(await act(d,'owner')));
  await call('PUT', `/api/approval-rules/${radm.id}`, T.admin, { docType:'Receipt', qtyThreshold:500, approverTier:'Large', isActive:true });
}
log('== self approval');
for (const who of ['supervisor','admin']) {
  d = await doc(150, who); if (!d) continue;
  log('  ', who, 'approves own standard doc ->', short(await act(d, who)));
}
log('== self approval large by admin');
d = await doc(600, 'admin'); if (d) log('  admin own large ->', short(await act(d,'admin')));
