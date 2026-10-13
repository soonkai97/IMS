// Ids that arrive in the query string, the JSON body or a multipart form (not in the path): filters on lists and exports, references inside
// create / update bodies, the attachment upload ownerId. For each field it sends the same variants as idor.mjs and prints status + body summary
// per variant, flagging variants that should be indistinguishable but are not.
// usage: IMS_STATE=state.json node idor-fields.mjs
import { raw, login, loadState, sig, normBody, GUID, EMPTY } from './lib.mjs';
import { pngBytes } from './pngbytes.mjs';

const st = loadState(); const I = st.ids, D = st.docs;
const T = {};
for (const [k, u, kind] of [['admin', 'admin'], ['staff', 'staff'], ['staff2', 'staff2', 'created'], ['sup', 'supervisor'], ['clerk', 'clerk'], ['noscope', 'noscope', 'created']]) T[k] = await login(u, kind ?? 'demo');

const summary = (r) => {
  if (r.status === 200 && r.data && typeof r.data === 'object' && 'items' in r.data) return `200 items=${r.data.items.length} total=${r.data.total}`;
  if (r.status === 200 && Array.isArray(r.data)) return `200 n=${r.data.length}`;
  return `${r.status} ${normBody(r).slice(0, 90)}`;
};
const flags = [];

// ---- 1. query-string filters, scoped users (staff in WH01, staff2 in WH02) and the unscoped admin
const lists = [
  ['documents', '/api/documents', ['warehouseId']],
  ['stock', '/api/stock', ['warehouseId', 'productId', 'categoryId']],
  ['ledger', '/api/stock/ledger', ['warehouseId', 'productId', 'sourceDocId']],
  ['low-stock', '/api/stock/low-stock', ['warehouseId']],
  ['movements', '/api/reports/movements', ['warehouseId', 'productId']],
  ['ledger-export', '/api/reports/ledger/export', ['warehouseId', 'productId', 'sourceDocId']],
  ['stock-export', '/api/reports/stock-summary/export', ['warehouseId', 'productId', 'categoryId']],
  ['movements-export', '/api/reports/movements/export', ['warehouseId', 'productId']],
];
const kindOf = { warehouseId: 'wh', productId: 'prod', categoryId: 'cat', sourceDocId: 'doc' };
for (const actor of ['staff', 'staff2', 'sup', 'noscope']) {
  for (const [name, path, fields] of lists) {
    for (const f of fields) {
      const k = kindOf[f];
      const own2 = actor === 'staff2';
      const v = {
        unknown: GUID(), empty: EMPTY, wrongKind: k === 'wh' ? I.p1 : I.wh1, softDeleted: { wh: I.wh3, prod: I.goneProd, cat: I.goneCat, doc: I.goneDoc }[k],
        outOfScope: k === 'wh' ? (own2 ? I.wh1 : I.wh2) : k === 'doc' ? (own2 ? D.wh1Posted.id : D.wh2Posted.id) : null,
        int1: '1', neg: '-1', huge: '99999999999999999999999', abc: 'abc',
      };
      const res = {};
      for (const [vn, val] of Object.entries(v)) {
        if (!val) continue;
        const r = await raw('GET', `${path}?${f}=${val}`, { token: T[actor] });
        res[vn] = r.status === 200 && name.endsWith('export') ? `200 ${r.buf.length}b` : summary(r);
      }
      const well = ['unknown', 'empty', 'wrongKind', 'softDeleted', 'outOfScope'].filter((x) => res[x]).map((x) => res[x]);
      const same = new Set(well).size === 1;
      const shape = new Set(['int1', 'neg', 'huge', 'abc'].map((x) => res[x])).size;
      if (!same) flags.push(`${actor} ${name}?${f}: ${JSON.stringify(res)}`);
      console.log(`${actor.padEnd(8)} ${(name + '?' + f).padEnd(34)} well-formed variants ${same ? 'identical' : 'DIFFER'} -> ${well[0]} ; malformed -> ${res.int1} (${shape} shape${shape > 1 ? 's' : ''})`);
    }
  }
}

// ---- 2. ids inside create bodies
const line = (o = {}) => ({ productId: I.p1, qty: 1, uomId: null, unitCost: 1, remark: null, ...o });
const base = (o = {}) => ({ warehouseId: I.wh1, partnerId: I.partner, purpose: null, remark: 'sec', lines: [line()], ...o });
const post = (tok, path, body) => raw('POST', path, { token: tok, body });
const bodyCases = [];
for (const actor of ['staff', 'staff2', 'clerk']) {
  const own2 = actor === 'staff2';
  const mine = own2 ? I.wh2 : I.wh1, other = own2 ? I.wh1 : I.wh2;
  const vv = { unknown: GUID(), empty: EMPTY, wrongKind: I.p1, softDeleted: I.wh3, outOfScope: other, int: 1, str: 'abc' };
  for (const [vn, val] of Object.entries(vv)) bodyCases.push([`${actor} receipt.warehouseId=${vn}`, actor, '/api/documents/receipts', base({ warehouseId: val })]);
  const pv = { unknown: GUID(), empty: EMPTY, wrongKind: I.wh1, softDeleted: I.gonePartner, int: 1 };
  for (const [vn, val] of Object.entries(pv)) bodyCases.push([`${actor} receipt.partnerId=${vn}`, actor, '/api/documents/receipts', base({ warehouseId: mine, partnerId: val })]);
  const lv = { unknown: GUID(), empty: EMPTY, wrongKind: I.wh1, softDeleted: I.goneProd, int: 1 };
  for (const [vn, val] of Object.entries(lv)) bodyCases.push([`${actor} receipt.lines[0].productId=${vn}`, actor, '/api/documents/receipts', base({ warehouseId: mine, lines: [line({ productId: val })] })]);
  const uv = { unknown: GUID(), empty: EMPTY, wrongKind: I.p1, softDeleted: I.goneUom, int: 1 };
  for (const [vn, val] of Object.entries(uv)) bodyCases.push([`${actor} receipt.lines[0].uomId=${vn}`, actor, '/api/documents/receipts', base({ warehouseId: mine, lines: [line({ uomId: val })] })]);
}
for (const [vn, val] of Object.entries({ unknown: GUID(), empty: EMPTY, wrongKind: I.p1, softDeleted: I.goneCat, int: 1 })) bodyCases.push([`admin product.categoryId=${vn}`, 'admin', '/api/products', { sku: 'ZB' + GUID().replaceAll("-", "").slice(0, 8), name: 'x', categoryId: val, baseUomId: I.pcs, safetyStock: 0, isActive: true, barcodes: [], conversions: [] }]);
for (const [vn, val] of Object.entries({ unknown: GUID(), empty: EMPTY, wrongKind: I.p1, softDeleted: I.goneUom, int: 1 })) bodyCases.push([`admin product.baseUomId=${vn}`, 'admin', '/api/products', { sku: 'ZB' + GUID().replaceAll("-", "").slice(0, 8), name: 'x', categoryId: null, baseUomId: val, safetyStock: 0, isActive: true, barcodes: [], conversions: [] }]);
for (const [vn, val] of Object.entries({ unknown: GUID(), empty: EMPTY, wrongKind: I.p1, softDeleted: I.wh3, int: 1 })) bodyCases.push([`admin user.warehouseIds=[${vn}]`, 'admin', '/api/users', { username: 'zb' + GUID().replaceAll("-", "").slice(0, 7), email: `zb${GUID().replaceAll("-", "").slice(0, 7)}@example.test`, displayName: 'x', role: 'Clerk', isActive: true, password: 'Aa1!' + GUID().slice(0, 12), warehouseIds: [val] }]);
const groups = {};
for (const [name, actor, path, body] of bodyCases) {
  const r = await post(T[actor], path, body);
  console.log(`${name.padEnd(46)} ${summary(r)}`);
  const key = name.replace(/=\w+\]?$/, '').replace(/=\[?\w+\]?$/, '');
  (groups[key] ??= []).push([name, r.status, normBody(r)]);
}
for (const [g, rows] of Object.entries(groups)) {
  const wellFormed = rows.filter(([n]) => !/=(int|str)$/.test(n) && !/\[(int|str)\]$/.test(n));
  const set = new Set(wellFormed.map(([, s, b]) => `${s}|${b.replace(/[0-9a-f-]{36}/g, '<g>')}`));
  if (set.size > 1) flags.push(`${g}: ${[...set].map((x) => x.slice(0, 110)).join('  //  ')}`);
}

// ---- 3. upload ownerId as a form field
const up = async (tok, type, ownerId) => {
  const f = new FormData(); f.append('ownerType', type); if (ownerId !== null) f.append('ownerId', ownerId); f.append('file', new Blob([pngBytes()], { type: 'image/png' }), 'x.png');
  return raw('POST', '/api/attachments', { token: tok, form: f });
};
for (const actor of ['staff', 'staff2', 'clerk', 'admin']) {
  for (const type of ['Document', 'Product']) {
    const own2 = actor === 'staff2';
    const vv = { unknown: GUID(), empty: EMPTY, wrongKind: type === 'Document' ? I.p1 : D.wh1Draft.id, softDeleted: type === 'Document' ? I.goneDoc : I.goneProd, outOfScope: type === 'Document' ? (own2 ? D.wh1Posted.id : D.wh2Posted.id) : null, int: '1', neg: '-1', huge: '99999999999999999999', missing: null };
    const res = {};
    for (const [vn, val] of Object.entries(vv)) { if (vn === 'outOfScope' && !val) continue; const r = await up(T[actor], type, val); res[vn] = `${r.status} ${normBody(r).slice(0, 60)}`; }
    const w = ['unknown', 'empty', 'wrongKind', 'softDeleted', 'outOfScope'].filter((x) => res[x]).map((x) => res[x].replace(/[0-9a-f-]{36}/g, '<g>'));
    console.log(`${actor.padEnd(8)} upload ${type.padEnd(9)} well-formed ${new Set(w).size === 1 ? 'identical' : 'DIFFER'} -> ${w[0]} ; int -> ${res.int} ; missing -> ${res.missing}`);
    if (new Set(w).size > 1) flags.push(`upload ${actor} ${type}: ${JSON.stringify(res)}`);
  }
}
console.log('\nFLAGS (variants that should be indistinguishable but are not; inspect each):');
for (const f of flags) console.log(' -', f.slice(0, 600));
if (!flags.length) console.log(' none');
