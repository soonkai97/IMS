// PR 134 p4: with real data in wh1, a user with no warehouse and allwh=false must see nothing (and a scoped user must see it).
import fs from 'node:fs';
import { call, login, short, PROBE_PW, sfx } from './lib.mjs';
const F = JSON.parse(fs.readFileSync(`${process.env.RV_OUT ?? '.'}/fixtures.json`));
const { T, wh1 } = F; const A = T.admin;
const perms = ['stock.read','reports.read','document.create','document.post','document.approve','masterdata.read','cost.view','valuation.view','users.read','document.reverse','document.edit.any'];
const role = (await call('POST', '/api/roles', A, { name: 'Rich' + sfx, permissions: perms })).json;
const mkU = async (n, extra) => { await call('POST', '/api/users', A, { username: n + sfx, email: `${n}${sfx}@ims.local`, displayName: n, role: role.code, isActive: true, password: PROBE_PW, ...extra }); return (await login(n + sfx, PROBE_PW)).token; };
const none = await mkU('nowh', { warehouseIds: [], allWarehouses: false });
const scoped = await mkU('scp', { warehouseIds: [wh1], allWarehouses: false });
// data
const prod = (await call('GET', '/api/products?pageSize=1', A)).json.items[0];
const rc = await call('POST', '/api/documents/receipts', A, { warehouseId: wh1, partnerId: null, purpose: null, remark: 'rv134', lines: [{ productId: prod.id, qty: 5, uomId: null, unitCost: 2, remark: null }] });
console.log('receipt', short(rc)); const doc = rc.json;
if (doc.status === 'Draft' || doc.status === 0 || true) { const sb = await call('POST', `/api/documents/${doc.id}/submit`, A, undefined, { 'If-Match': doc.concurrencyToken ?? '*' }); console.log('submit', short(sb)); }
const ps = await call('POST', `/api/documents/${doc.id}/post`, A, undefined, { 'If-Match': '*' }); console.log('post', short(ps));
const cnt = (j) => Array.isArray(j) ? j.length : (j?.items?.length ?? (j ? Object.keys(j).length + 'k' : '-'));
const eps = ['/api/stock', '/api/stock/ledger', '/api/stock/low-stock', '/api/stock/summary', '/api/documents', '/api/documents/counts', '/api/reports/movements', `/api/stock?warehouseId=${wh1}`, `/api/stock/ledger?warehouseId=${wh1}`, `/api/documents?warehouseId=${wh1}`, `/api/reports/movements?warehouseId=${wh1}`, '/api/audit', '/api/warehouses', '/api/reports/stock-summary/export', '/api/reports/movements/export', '/api/reports/ledger/export', '/api/reports/low-stock/export'];
let bad = 0;
for (const e of eps) {
  const a = await call('GET', e, A), s = await call('GET', e, scoped), n = await call('GET', e, none);
  const flag = (n.status === 200 && n.text.length > 2 && cnt(n.json) !== 0 && !/audit|counts|stock\/summary/.test(e) && n.text.length >= s.text.length && s.text.length > 200) ? '  <-- CHECK' : '';
  console.log(`${e.padEnd(52)} admin ${a.status}/${a.text.length}b | scoped ${s.status}/${s.text.length}b n=${cnt(s.json)} | nowh ${n.status}/${n.text.length}b n=${cnt(n.json)}${flag}`);
}
console.log('doc by id nowh:', short(await call('GET', `/api/documents/${doc.id}`, none)), '| scoped:', short(await call('GET', `/api/documents/${doc.id}`, scoped)));
console.log('counts nowh:', JSON.stringify((await call('GET', '/api/documents/counts', none)).json).slice(0, 200));
console.log('summary nowh:', JSON.stringify((await call('GET', '/api/stock/summary', none)).json).slice(0, 200), '| scoped', JSON.stringify((await call('GET', '/api/stock/summary', scoped)).json).slice(0, 200));
console.log('create receipt wh1 nowh ->', short(await call('POST', '/api/documents/receipts', none, { warehouseId: wh1, lines: [{ productId: prod.id, qty: 1, unitCost: 1 }] })));
console.log('ledger-doc post by nowh ->', short(await call('POST', `/api/documents/${doc.id}/reverse`, none, { reason: 'x' }, { 'If-Match': '*' })));
const att = await call('GET', `/api/attachments?documentId=${doc.id}`, none); console.log('attachments (route guess)', short(att));
const wl = await call('GET', '/api/warehouses', none); console.log('warehouses nowh', wl.status, wl.text.slice(0, 120));
fs.writeFileSync(`${process.env.RV_OUT}/p4doc.json`, JSON.stringify({ id: doc.id }));
