// PR 134 p3: a user with no warehouse and allWarehouses=false: API behaviour and data exposure.
import fs from 'node:fs';
import { call, login, short, PROBE_PW, sfx, decode } from './lib.mjs';
const F = JSON.parse(fs.readFileSync(`${process.env.RV_OUT ?? '.'}/fixtures.json`));
const { T, wh1, R } = F;
const A = T.admin;
const rich = await call('POST', '/api/roles', A, { name: 'RichNoWh' + sfx, permissions: ['stock.read','reports.read','document.create','document.post','document.approve','masterdata.read','cost.view','valuation.view','audit.read','users.read'] });
const u = 'nowh' + sfx;
const cr = await call('POST', '/api/users', A, { username: u, email: `${u}@ims.local`, displayName: u, role: rich.json.code, isActive: true, password: PROBE_PW, warehouseIds: [], allWarehouses: false });
console.log('create user with empty warehouseIds + allWarehouses=false ->', short(cr), JSON.stringify({ wh: cr.json.warehouseIds, all: cr.json.allWarehouses }));
if (cr.status !== 200) process.exit();
const lg = await login(u, PROBE_PW); const claims = decode(lg.token);
console.log('claims allwh/wh:', Object.entries(claims).filter(([k]) => /wh|ware/i.test(k)).map(([k, v]) => `${k}=${JSON.stringify(v)}`).join(' '));
const tk = lg.token;
// baseline: what does a full-scope user see for the same endpoints
const wl = (await call('GET', '/api/warehouses', A)).json; const whId = (Array.isArray(wl) ? wl : wl.items)[0].id;
const eps = ['/api/stock', '/api/stock/ledger', '/api/stock/low', '/api/documents', '/api/reports/movements', '/api/reports/stock-valuation', '/api/reports/low-stock', '/api/audit', '/api/warehouses', '/api/products', '/api/dashboard', `/api/warehouses/${whId}`, `/api/stock?warehouseId=${whId}`, `/api/documents?warehouseId=${whId}`, '/api/auth/me'];
const count = (j) => Array.isArray(j) ? j.length : (j?.items?.length ?? j?.totalCount ?? j?.total ?? '-');
for (const e of eps) {
  const a = await call('GET', e, A), n = await call('GET', e, tk);
  console.log(`${e.padEnd(40)} admin ${a.status} n=${count(a.json)} | nowh ${n.status} n=${count(n.json)}`);
}
// an existing doc of wh1 by id and attachment
const docs = (await call('GET', '/api/documents?pageSize=5', A)).json; const d = docs.items?.[0];
if (d) {
  console.log('doc by id ->', short(await call('GET', `/api/documents/${d.id}`, tk)));
  console.log('doc attachments ->', short(await call('GET', `/api/documents/${d.id}/attachments`, tk)));
}
// create a doc in wh1
const prods = (await call('GET', '/api/products?pageSize=1', A)).json; const p = prods.items?.[0];
if (p) {
  const body = { type: 'Receipt', warehouseId: whId, lines: [{ productId: p.id, quantity: 1, unitCost: 1 }] };
  console.log('create document in wh1 as no-wh user ->', short(await call('POST', '/api/documents', tk, body)));
}
