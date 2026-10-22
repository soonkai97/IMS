// Setup: switches `purchasing` ON (remembering how they were), creates the SEC- fixtures through the API and writes fixtures.json to RV_OUT.
// Fixtures: custom roles, throw-away users (made-up shared password), one supplier, one customer, products (one with a BOX conversion), warehouses A (WH01) and B (QAW1).
import { call, login, items, saveFx, expect, check, PROBE_PW, sfx, short } from './lib.mjs';
const A = await login('admin');
const mods = Object.fromEntries(items(await call('GET', '/api/modules', A)).map((m) => [m.code, m.enabled]));
for (const code of ['purchasing']) if (!mods[code]) expect(8, `switch ${code} on`, await call('PUT', `/api/modules/${code}`, A, { enabled: true, reason: 'SEC-PL2.11 retest' }), 200);

const whs = items(await call('GET', '/api/warehouses', A));
const whA = whs.find((w) => w.code === 'WH01').id, whB = whs.find((w) => w.code === 'QAW1').id;
const role = async (name, permissions) => { const r = await call('POST', '/api/roles', A, { name: `SEC-${sfx}-${name}`, permissions }); if (r.status !== 200) throw new Error(`role ${name} ${short(r)}`); return r.json.code; };
const R = {
  poRead: await role('poread', ['masterdata.read', 'po.read']),
  postNoRead: await role('postnoread', ['masterdata.read', 'stock.read', 'document.create', 'document.post']),
  none: await role('none', ['masterdata.read', 'stock.read']),
  ownerLike: await role('ownerlike', ['masterdata.read', 'stock.read', 'cost.view', 'po.read', 'po.manage', 'document.approve.large']),
  staffPlus: await role('staffplus', ['masterdata.read', 'stock.read', 'document.create', 'document.post', 'po.read', 'cost.view']),
};
const user = async (name, roleCode, whIds, all = false) => {
  const username = `sec${sfx}${name}`;
  const r = await call('POST', '/api/users', A, { username, email: `${username}@ims.local`, displayName: `SEC ${name}`, role: roleCode, isActive: true, password: PROBE_PW, warehouseIds: all ? [] : whIds, allWarehouses: all });
  if (r.status !== 200) throw new Error(`user ${name} ${short(r)}`); return { username, id: r.json.id };
};
const U = {
  poRead: await user('poread', R.poRead, [], true), postNoRead: await user('postnr', R.postNoRead, [], true), none: await user('none', R.none, [], true),
  ownerLike: await user('ownlike', R.ownerLike, [whA]), staffPlus: await user('staffplus', R.staffPlus, [whA]),
  sup2: await user('sup2', 'Supervisor', [whA]), clerk2: await user('clerk2', 'Clerk', [whA]), admin2: await user('admin2', 'Admin', [], true),
  scopeA: await user('scopea', 'Clerk', [whA]), scopeB: await user('scopeb', 'Clerk', [whB]), supB: await user('supb', 'Supervisor', [whB]),
};

const supplier = (await call('POST', '/api/partners', A, { type: 'Supplier', name: `SEC-${sfx} Supplier`, contact: null, isActive: true })).json;
const customer = (await call('POST', '/api/partners', A, { type: 'Customer', name: `SEC-${sfx} Customer`, contact: null, isActive: true })).json;
const units = items(await call('GET', '/api/units?pageSize=50', A)); const pcs = units.find((u) => u.code === 'PCS').id, box = units.find((u) => u.code === 'BOX').id;
const product = async (n, extra = {}) => { const r = await call('POST', '/api/products', A, { sku: `SEC-${sfx}-${n}`, name: `SEC ${sfx} ${n}`, categoryId: null, baseUomId: pcs, safetyStock: 0, isActive: true, barcodes: [], conversions: [], ...extra }); if (r.status !== 200) throw new Error(`product ${n} ${short(r)}`); return r.json; };
const P = { a: await product('A'), b: await product('B'), c: await product('C'), box: await product('BOX', { conversions: [{ fromUomId: box, toUomId: pcs, factor: 12 }] }), low: await product('LOW', { safetyStock: 50 }) };
check(0, 'fixtures created', !!(supplier.id && customer.id && P.a.id), `modules were ${JSON.stringify(mods)}`);
saveFx({ modsBefore: mods, whA, whB, R, U, supplier: supplier.id, customer: customer.id, pcs, box, P: Object.fromEntries(Object.entries(P).map(([k, v]) => [k, v.id])), sfx });
