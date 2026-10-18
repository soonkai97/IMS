// End-to-end IDOR / existence-oracle matrix for the public-id switch. Local, throw-away API only.
// For every route that takes an id in the path, every actor, and every id variant, it records status + normalised body + headers and
// reports where the answers for "does not exist / not yours / soft-deleted / wrong kind" differ from each other (an oracle).
// usage: IMS_STATE=state.json node idor.mjs [out.json]
import { writeFileSync } from 'node:fs';
import { raw, login, loadState, sig, normBody, GUID, EMPTY } from './lib.mjs';

const st = loadState();
const I = st.ids, D = st.docs, A = st.att;
const actors = {
  admin: { user: 'admin', wh: null, kind: 'demo' }, owner: { user: 'owner', wh: null },
  supervisor: { user: 'supervisor', wh: 'wh1' }, supervisor2: { user: 'supervisor2', wh: 'wh2', kind: 'created' },
  staff: { user: 'staff', wh: 'wh1' }, staff2: { user: 'staff2', wh: 'wh2', kind: 'created' },
  clerk: { user: 'clerk', wh: 'wh1' }, clerk2: { user: 'clerk2', wh: 'wh2', kind: 'created' },
  noscope: { user: 'noscope', wh: 'none', kind: 'created' }, anon: { user: null },
};
const tokens = {};
for (const [k, a] of Object.entries(actors)) tokens[k] = a.user ? await login(a.user, a.kind ?? 'demo') : undefined;

const mkVariants = (kind, actor) => {
  const own = actors[actor].wh;
  const v = {
    unknown: GUID(), empty: EMPTY, int1: '1', neg: '-1', huge: '99999999999999999999999', abc: 'abc', sqli: encodeURIComponent("1' OR '1'='1"),
    upper: null,
  };
  const wrong = { doc: I.p1, product: I.wh1, warehouse: I.p1, partner: I.wh1, uom: I.wh1, category: I.wh1, user: I.wh1, rule: I.wh1, att: D.wh1Draft.id };
  v.wrongKind = wrong[kind];
  const gone = { doc: I.goneDoc, product: I.goneProd, warehouse: I.wh3, partner: I.gonePartner, uom: I.goneUom, category: I.goneCat, user: I.gone, rule: I.goneRule, att: I.goneAtt };
  v.softDeleted = gone[kind];
  if (kind === 'doc') { v.outOfScope = own === 'wh2' ? D.wh1Posted.id : D.wh2Posted.id; v.inScope = own === 'wh2' ? D.wh2Posted.id : D.wh1Posted.id; v.draftInScope = own === 'wh2' ? D.wh2Draft.id : D.wh1Draft.id; }
  if (kind === 'att') { v.outOfScope = own === 'wh2' ? A.wh1Doc : A.wh2Doc; v.inScope = own === 'wh2' ? A.wh2Doc : A.wh1Doc; }
  if (kind === 'warehouse') { v.outOfScope = own === 'wh2' ? I.wh1 : I.wh2; v.inScope = own === 'wh2' ? I.wh2 : I.wh1; }
  if (kind === 'user') { v.outOfScope = I.staff2; v.inScope = I.noscope; }
  delete v.upper;
  return v;
};
const EXIST = ['unknown', 'empty', 'wrongKind', 'softDeleted', 'outOfScope'];   // answers that must not tell these apart
const SHAPE = ['int1', 'neg', 'huge', 'abc', 'sqli'];                          // routing level: a shape check only

const ifm = { 'If-Match': '"AAAAAAAAB9E="' };
const routes = [
  ['PUT warehouses/{id}', 'warehouse', (id) => ['PUT', `/api/warehouses/${id}`, { body: { code: 'ZQ9', name: 'x', isActive: true } }]],
  ['PUT partners/{id}', 'partner', (id) => ['PUT', `/api/partners/${id}`, { body: { type: 'Supplier', name: 'zz', isActive: true } }]],
  ['PUT units/{id}', 'uom', (id) => ['PUT', `/api/units/${id}`, { body: { code: 'ZQ9', name: 'x' } }]],
  ['PUT categories/{id}', 'category', (id) => ['PUT', `/api/categories/${id}`, { body: { name: 'zz' } }]],
  ['PUT users/{id}', 'user', (id) => ['PUT', `/api/users/${id}`, { body: { username: 'zq9', email: 'zq9@example.test', displayName: 'x', role: 'Clerk', isActive: true, password: null, warehouseIds: [] } }]],
  ['GET products/{id}', 'product', (id) => ['GET', `/api/products/${id}`, {}]],
  ['PUT products/{id}', 'product', (id) => ['PUT', `/api/products/${id}`, { body: { sku: 'ZQ9', name: 'x', categoryId: null, baseUomId: I.pcs, safetyStock: 0, isActive: true, barcodes: [], conversions: [] } }]],
  ['PUT approval-rules/{id}', 'rule', (id) => ['PUT', `/api/approval-rules/${id}`, { body: { docType: 'Issue', amountThreshold: 5, qtyThreshold: null, approverTier: 'Standard', isActive: true } }]],
  ['GET documents/{id}', 'doc', (id) => ['GET', `/api/documents/${id}`, {}]],
  ['PUT documents/{id}', 'doc', (id) => ['PUT', `/api/documents/${id}`, { headers: ifm, body: { warehouseId: I.wh1, partnerId: null, purpose: null, remark: 'x', lines: [{ productId: I.p1, qty: 1, uomId: null, unitCost: 1, remark: null }] } }]],
  ['POST documents/{id}/submit', 'doc', (id) => ['POST', `/api/documents/${id}/submit`, { headers: ifm }]],
  ['POST documents/{id}/approve', 'doc', (id) => ['POST', `/api/documents/${id}/approve`, { headers: ifm, body: { negativeStockOverrideReason: null } }]],
  ['POST documents/{id}/reject', 'doc', (id) => ['POST', `/api/documents/${id}/reject`, { headers: ifm, body: { reason: 'not a valid request' } }]],
  ['POST documents/{id}/recall', 'doc', (id) => ['POST', `/api/documents/${id}/recall`, { headers: ifm }]],
  ['POST documents/{id}/cancel', 'doc', (id) => ['POST', `/api/documents/${id}/cancel`, { headers: ifm, body: { reason: 'not a valid request' } }]],
  ['POST documents/{id}/post', 'doc', (id) => ['POST', `/api/documents/${id}/post`, { headers: ifm }]],
  ['POST documents/{id}/reverse', 'doc', (id) => ['POST', `/api/documents/${id}/reverse`, { headers: ifm, body: { reason: 'not a valid request' } }]],
  ['GET attachments/{id}', 'att', (id) => ['GET', `/api/attachments/${id}`, {}]],
  ['DELETE attachments/{id}', 'att', (id) => ['DELETE', `/api/attachments/${id}`, {}]],
  ['GET attachments?ownerType=Document&ownerId=', 'doc', (id) => ['GET', `/api/attachments?ownerType=Document&ownerId=${id}`, {}]],
  ['GET attachments?ownerType=Product&ownerId=', 'product', (id) => ['GET', `/api/attachments?ownerType=Product&ownerId=${id}`, {}]],
];

const results = [];
for (const [name, kind, build] of routes) {
  for (const actor of Object.keys(actors)) {
    const vs = mkVariants(kind, actor);
    const rec = { route: name, actor, answers: {}, groups: {} };
    for (const [vn, id] of Object.entries(vs)) {
      if (!id) continue;
      // mutating routes: a real in-scope draft must not be changed by this matrix, so for them the "inScope" variant uses a posted document (the action is refused)
      if (kind === 'doc' && name.startsWith('POST documents/') && name.endsWith('/reverse') && vn === 'inScope') { rec.answers[vn] = { status: 'skip' }; continue; }
      if (name.startsWith('DELETE ') && (vn === 'inScope' || (vn === 'outOfScope' && actors[actor].wh === null))) { rec.answers[vn] = { status: 'skip' }; continue; }
      if (name.startsWith('PUT ') && ['warehouse', 'user'].includes(kind) && (vn === 'inScope' || (vn === 'outOfScope' && actors[actor].wh === null)) && actor !== 'anon') { /* PUT with a valid body on a real row would edit it: skip real ids */ rec.answers[vn] = { status: 'skip' }; continue; }
      const [method, path, opts] = build(id);
      const r = await raw(method, path, { ...opts, token: tokens[actor] });
      rec.answers[vn] = { status: r.status, sig: sig(r), body: normBody(r).slice(0, 120), ms: Math.round(r.ms) };
    }
    const groups = {};
    for (const vn of EXIST) { const a = rec.answers[vn]; if (!a || a.status === 'skip') continue; (groups[a.sig] ??= []).push(vn); }
    rec.groups = Object.values(groups);
    rec.distinct = rec.groups.length;
    const shapeSigs = new Set(SHAPE.map((vn) => rec.answers[vn]?.sig));
    rec.shapeDistinct = shapeSigs.size;
    results.push(rec);
  }
}

const out = process.argv[2] ?? 'idor-out.json';
writeFileSync(out, JSON.stringify(results, null, 1));
let bad = 0;
for (const r of results) {
  const stat = Object.fromEntries(Object.entries(r.answers).map(([k, v]) => [k, v.status]));
  const flag = r.distinct > 1 ? ' <-- DIFFERENT' : '';
  if (r.distinct > 1) bad++;
  console.log(`${r.route.padEnd(48)} ${r.actor.padEnd(12)} ${JSON.stringify(stat)}${flag}`);
  if (r.distinct > 1) console.log('    groups:', JSON.stringify(r.groups));
}
console.log(`\nroutes x actors: ${results.length}, with distinguishable existence answers: ${bad}`);
