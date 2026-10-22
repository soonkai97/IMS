// Clean-up: leaves no open order, no waiting or posted receipt of the probes, no active SEC- user, role, partner or product. Run `node z-cleanup.mjs orders` first (needs purchasing ON),
// then `node p8-modules.mjs off`, then `node z-cleanup.mjs people` (users, roles, partners, products; works with the modules OFF) and `node z-cleanup.mjs accounting` is part of p8 (both modules OFF).
import { call, tokens, fx, check, failures, useAdmin, PO, docAct, items, reverseDoc, act } from './lib.mjs';
const stage = process.argv[2] ?? 'orders';
const F = fx(), T = await tokens(); useAdmin(T.admin);
const pageAll = async (path, tok = T.admin) => { const out = []; for (let p = 1; p < 20; p++) { const r = await call('GET', `${path}${path.includes('?') ? '&' : '?'}page=${p}&pageSize=200`, tok); if (r.status !== 200) throw new Error(`${path} page ${p}: ${r.status}`); const it = items(r); out.push(...it); if (it.length < 200) break; } return out; };
const keyOf = Object.fromEntries([['admin', 'admin'], ['owner', 'owner'], ['supervisor', 'supervisor'], ['clerk', 'clerk'], ['staff', 'staff'], ...Object.entries(F.U).map(([k, u]) => [u.username, k])]);
const asCreator = (name) => T[keyOf[name]] ?? T.admin;

if (stage === 'orders') {
  // 1. documents of the probes: cancel what waits, reverse what posted
  const sweepDocs = async () => {
    const docs = [...(await pageAll('/api/documents?search=SEC-')), ...(await pageAll('/api/documents?search=HYPERLINK')), ...(F.docs ?? []).length ? await Promise.all(F.docs.map(async (id) => (await call('GET', `/api/documents/${id}`, T.admin)).json)) : []];
    let revd = 0, canc = 0, left = 0;
    for (const d of docs) {
      if (!d?.id || !/SEC-|HYPERLINK/.test(d.partnerName ?? '') && !(F.docs ?? []).includes(d.id)) continue;
      if (d.status === 'Posted') { const r = await reverseDoc(T.admin, d.id); r.status === 200 ? revd++ : (left++, console.log('not reversed:', d.no, r.status, String(r.json?.detail).slice(0, 100))); }
      else if (['Draft', 'Submitted', 'Approved'].includes(d.status)) { const r = (await docAct(asCreator(d.createdBy), d.id, 'cancel', { reason: 'SEC retest cleanup' })); if (r.status === 200) canc++; else { const r2 = await docAct(T.admin, d.id, 'cancel', { reason: 'SEC retest cleanup' }); r2.status === 200 ? canc++ : (left++, console.log('left waiting:', d.no, d.status, r2.status, String(r2.json?.detail).slice(0, 100))); } }
    }
    return { revd, canc, left };
  };
  await sweepDocs();
  // 2. orders of the probes
  let open = 0;
  for (let pass = 0; pass < 3; pass++) {
    const orders = (await pageAll(`${PO}?supplierId=${F.supplier}`)).filter((o) => !['Closed', 'Cancelled'].includes(o.status));
    for (const o of orders) {
      const g = (await call('GET', `${PO}/${o.id}`, T.admin)).json;
      if (g.status === 'Submitted') { const who = g.submittedBy === 'admin' ? T.admin2 : T.admin; const a = await act(who, g.id, 'approve'); if (a.status !== 200) await act(asCreator(g.submittedBy), g.id, 'cancel', {}); continue; }
      if (g.status === 'Approved') { for (const r of g.receipts.filter((x) => ['Draft', 'Submitted', 'Approved'].includes(x.status))) await docAct(T.admin, r.id, 'cancel', { reason: 'SEC retest cleanup' });
        const c = await act(T.admin, g.id, 'cancel', { reason: 'SEC retest cleanup' }); if (c.status !== 200 && g.lines.some((l) => l.receivedQty > 0)) await act(T.admin, g.id, 'close', { reason: 'SEC retest cleanup' }); continue; }
      if (g.status === 'Draft') await act(T.admin, g.id, 'cancel', {});
    }
  }
  const sw = await sweepDocs(); // receipts that the order passes released or that were waiting behind an order
  check(9, `documents of the probes: ${sw.revd} reversed, ${sw.canc} cancelled in the last sweep, none left waiting or posted`, sw.left === 0, `left=${sw.left}`);
  const rest = (await pageAll(`${PO}?supplierId=${F.supplier}`)).filter((o) => !['Closed', 'Cancelled'].includes(o.status)); open = rest.length;
  check(9, 'no open purchase order of the probes remains (Draft, Submitted, Approved)', open === 0, `${open} left`);
  const stock = (await pageAll('/api/stock?search=SEC-')).filter((s) => s.qtyOnHand !== 0);
  check(9, 'stock of SEC- products is back to 0 (receipts reversed; the ledger rows stay as history)', stock.length === 0, stock.slice(0, 3).map((s) => `${s.sku}=${s.qtyOnHand}`).join(' '));
}

if (stage === 'people') {
  const stamp = F.sfx; let off = 0, fail = 0;
  // A role cannot be switched off while any user (inactive ones too) still holds it, so each throw-away user is first moved to the least-privileged system role, inactive.
  for (const u of (await pageAll('/api/users?search=sec')).filter((x) => /^SEC /.test(x.displayName))) {
    const r = await call('PUT', `/api/users/${u.id}`, T.admin, { username: u.username, email: u.email, displayName: u.displayName, role: 'WarehouseStaff', isActive: false, password: null, warehouseIds: u.warehouseIds, allWarehouses: u.allWarehouses }); r.status === 200 ? off++ : (fail++, console.log('refused', r.status, String(r.json?.detail ?? r.text).slice(0, 110)));
  }
  for (const r of (await pageAll('/api/roles')).filter((x) => x.name.startsWith('SEC-') && x.isActive)) { const q = await call('PUT', `/api/roles/${r.id}`, T.admin, { name: r.name, description: r.description, isActive: false }); (q.status === 200 ? off++ : (fail++, console.log('refused', q.status, String(q.json?.detail ?? q.text).slice(0, 110)))); }
  for (const p of (await pageAll('/api/partners?includeInactive=false')).filter((x) => /^SEC-|^=HYPERLINK|^@SUM/.test(x.name) || x.id === F.supplier || x.id === F.customer)) { const q = await call('PUT', `/api/partners/${p.id}`, T.admin, { type: p.type, name: p.name, contact: p.contact, isActive: false }); (q.status === 200 ? off++ : (fail++, console.log('refused', q.status, String(q.json?.detail ?? q.text).slice(0, 110)))); }
  for (const p of (await pageAll('/api/products?search=SEC-')).filter((x) => x.isActive)) { const q = await call('PUT', `/api/products/${p.id}`, T.admin, { sku: p.sku, name: p.name, categoryId: p.categoryId, baseUomId: p.baseUomId, safetyStock: p.safetyStock, isActive: false, barcodes: p.barcodes, conversions: p.conversions }); (q.status === 200 ? off++ : (fail++, console.log('refused', q.status, String(q.json?.detail ?? q.text).slice(0, 110)))); }
  check(9, `throw-away users, roles, partners, products switched off (${off}), none refused`, fail === 0, `refused=${fail}`);
}
console.log(failures() ? `${failures()} FAIL` : `z-cleanup ${stage} done`);
