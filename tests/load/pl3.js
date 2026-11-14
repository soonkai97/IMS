// Shared helpers for the PL3 (sales, price lists, returns, reversal) k6 scenarios: sales-read.js, sales-write.js, returns-reversal.js, pl3-mixed.js.
// Conventions, environment and thresholds are those of scenarios.js (tests/load/README.md); the extra knobs are:
//   SWITCH_MODULES  default 1: setup() turns the purchasing, sales and returns modules ON through PUT /api/modules/{code} as the admin (the returns module needs the other two).
//                   This writes to the database, so it is for the throw-away Ims_Perf database only, like every scenario here. 0 = only check that they are on.
//   SO_SEED         default 150: how many sales orders the database must hold for the list and detail reads (setup() creates the missing ones, approved, not delivered).
//   PRICE_LISTS     default 10: customer price lists setup() creates (plus one default list), 150 prices each; they are skipped when lists named "k6 ..." already exist.
//
// Users (Development seed): the clerk, supervisor and admin create and submit orders (so.manage), the admin or the owner approves them (a sales-order rule fires from
// RM 5,000 for a Supervisor, from RM 20,000 for the Owner: about 12 % of the flows are that big), the demo staff create the delivery, a supervisor or the admin post and reverse.
// The supervisor, staff and clerk are scoped to the main warehouse; every flow that needs an approval stays there (self-approval is off), the other warehouses get small flows.
import http from 'k6/http';
import { fail } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { BASE, THRESHOLDS, setupData, get, post, headers, pick, rnd, writeFlows, writeFlowFail, flowTime } from './scenarios.js';

const SWITCH = (__ENV.SWITCH_MODULES || '1') !== '0';
const SO_SEED = parseInt(__ENV.SO_SEED || '150');
const PRICE_LISTS = parseInt(__ENV.PRICE_LISTS || '10');

// Per flow figures (the flow tag makes k6 keep one sub-metric per flow: see pl3Thresholds).
export const flowOk = new Counter('pl3_flows_ok');
export const flowFailed = new Counter('pl3_flows_failed');
export const flowTrend = new Trend('pl3_flow_duration', true);

// Every request name used below. k6 keeps a duration sub-metric per name only when a threshold names it, so each one gets a threshold that cannot fail ('max>=0'):
// that is what makes the per-endpoint P50/P95/P99 appear in the summary JSON (summaryTo, by_name).
export const PL3_NAMES = [
  'so.list', 'so.list?status', 'so.list?customer', 'so.list?warehouse', 'so.list?search', 'so.list?cursor', 'so/{id}', 'price.lookup',
  'so.create', 'so.submit', 'so.approve', 'delivery.create', 'delivery.submit', 'delivery.approve', 'delivery.post',
  'ret.issue.create', 'ret.issue.submit', 'ret.issue.post', 'return.create', 'return.submit', 'return.approve', 'return.post',
  'rev.create', 'rev.submit', 'rev.post', 'doc.reverse',
];
export const PL3_FLOWS = ['sales', 'return', 'reversal'];

export function pl3Thresholds() {
  const t = Object.assign({}, THRESHOLDS);
  for (const n of PL3_NAMES) t[`http_req_duration{name:${n}}`] = ['max>=0'];
  for (const f of PL3_FLOWS) {
    t[`pl3_flow_duration{flow:${f}}`] = ['max>=0'];
    t[`pl3_flows_failed{flow:${f}}`] = ['count>=0'];
  }
  return t;
}

// --- setup ------------------------------------------------------------------------------------------------------------
function getJson(path, token) {
  const r = http.get(`${BASE}${path}`, { headers: headers(token), tags: { name: 'setup' } });
  if (r.status !== 200) fail(`GET ${path} -> ${r.status} ${r.body}`);
  return r.json();
}

function ensureModules(token) {
  const mods = getJson('/api/modules', token);
  for (const code of ['purchasing', 'sales', 'returns']) {
    const m = mods.find((x) => x.code === code);
    if (m && m.enabled) continue;
    if (!SWITCH) fail(`module ${code} is off: switch it on (PUT /api/modules/${code}) or run with SWITCH_MODULES=1 on the perf database`);
    const r = http.put(`${BASE}/api/modules/${code}`, JSON.stringify({ enabled: true, reason: 'k6 load test on the perf database' }), { headers: headers(token), tags: { name: 'setup' } });
    if (r.status !== 200) fail(`switching ${code} on -> ${r.status} ${r.body}`);
  }
}

function round2(x) { return Math.round(x * 100) / 100; }

function ensurePriceLists(d) {
  const admin = d.tokens.admin;
  const have = getJson('/api/price-lists?pageSize=100', admin).items.filter((x) => /^k6 /.test(x.name));
  if (have.length > 0) return;
  const unique = Array.from(new Set(d.productPool)).filter((id) => !d.inactive[id]);   // a price for an inactive product is refused (400)
  const items = (n) => {
    const out = []; const seen = {};
    for (let i = 0; i < n * 3 && out.length < n; i++) {
      const id = pick(unique);
      if (seen[id]) continue; seen[id] = 1;
      out.push({ productId: id, uomId: null, price: round2(5 + Math.random() * 295) });
    }
    return out;
  };
  const create = (body) => {
    const r = http.post(`${BASE}/api/price-lists`, JSON.stringify(body), { headers: headers(admin), tags: { name: 'setup' } });
    if (r.status !== 200) fail(`creating price list ${body.name} -> ${r.status} ${r.body}`);
  };
  create({ name: 'k6 default', isDefault: true, customerId: null, items: items(400) });
  for (let i = 0; i < Math.min(PRICE_LISTS, d.customers.length); i++) create({ name: `k6 customer ${i + 1}`, isDefault: false, customerId: d.customers[i], items: items(150) });
}

// What the PL3 scenarios need on top of setupData(): modules on, customers, price lists, a stock of sales orders to read.
export function setupPl3() {
  const d = setupData();
  ensureModules(d.tokens.admin);
  d.customers = getJson('/api/partners?type=Customer&pageSize=100', d.tokens.admin).items.filter((c) => c.isActive !== false).map((c) => c.id);
  if (d.customers.length === 0) fail('the database has no customers (docs/dba/01-seed-perf.sql creates 80)');
  ensurePriceLists(d);
  const list = getJson('/api/sales-orders?pageSize=200&includeTotal=false', d.tokens.admin);
  d.orderIds = list.items.map((o) => o.id);
  d.orderNoPrefix = list.items.length ? String(list.items[0].no).split('-').slice(0, 2).join('-') : 'SO';
  for (let i = d.orderIds.length; i < SO_SEED; i++) {
    const id = salesOrderFlow(d, { deliver: false, quiet: true });
    if (id) d.orderIds.push(id);
  }
  d.orderCursors = [];
  let cursor = null;
  for (let p = 0; p < 5; p++) {
    const r = getJson(`/api/sales-orders?pageSize=20&includeTotal=false${cursor ? `&cursor=${cursor}` : ''}`, d.tokens.admin);
    cursor = r.nextCursor;
    if (!cursor) break;
    d.orderCursors.push(cursor);
  }
  return d;
}

// --- reads -------------------------------------------------------------------------------------------------------------
export function salesReadOp(d) {
  const x = Math.random() * 100;
  if (x < 40) {   // price lookup: what an order-entry screen asks for every line (a customer with a list of its own, or the default list)
    const t = pick([d.tokens.admin, d.tokens.supervisor, d.tokens.clerk]);
    const customer = Math.random() < 0.5 ? d.customers[rnd(Math.min(PRICE_LISTS, d.customers.length))] : pick(d.customers);
    return get(t, `/api/price-lists/lookup?customerId=${customer}&productId=${pick(d.productPool)}`, 'price.lookup');
  }
  const t = pick([d.tokens.admin, d.tokens.owner]);   // so.read; both see every warehouse
  if (x < 65) return get(t, `/api/sales-orders/${pick(d.orderIds)}`, 'so/{id}');
  const f = rnd(6);
  if (f === 0) return get(t, '/api/sales-orders', 'so.list');
  if (f === 1) return get(t, '/api/sales-orders?status=Approved', 'so.list?status');
  if (f === 2) return get(t, `/api/sales-orders?customerId=${pick(d.customers)}`, 'so.list?customer');
  if (f === 3) return get(t, `/api/sales-orders?warehouseId=${pick(d.warehouses)}`, 'so.list?warehouse');
  if (f === 4) return get(t, `/api/sales-orders?search=${encodeURIComponent(d.orderNoPrefix)}`, 'so.list?search');
  return get(t, `/api/sales-orders?${d.orderCursors.length ? `cursor=${pick(d.orderCursors)}&` : ''}includeTotal=false`, 'so.list?cursor');
}

// --- write flows -------------------------------------------------------------------------------------------------------
function whIndex(d) { return Math.random() < 0.8 || d.warehouses.length < 2 ? 0 : 1 + rnd(d.warehouses.length - 1); }
function poolOf(d, w) { return d.stocked[w] || []; }
function failed(flow, quiet) { if (!quiet) { flowFailed.add(1, { flow }); writeFlowFail.add(1); } return null; }
function done(flow, started) { flowOk.add(1, { flow }); flowTrend.add(Date.now() - started, { flow }); writeFlows.add(1); flowTime.add(Date.now() - started); }
function isApproved(r) { return r.json('status') === 'Approved' || r.json('status') === 2; }

// create -> submit -> (approve) [-> delivery from the order -> submit -> (approve) -> post]. Returns the order id (null if the flow failed).
// About 12 % of the orders reach a sales-order approval rule (amount from RM 5,000: a Supervisor, from RM 20,000: the Owner); the delivery of such an order is big too.
export function salesOrderFlow(d, opts = {}) {
  const deliver = opts.deliver !== false;
  const quiet = !!opts.quiet;
  const w = whIndex(d);
  const wh = d.warehouses[w];
  const pool = poolOf(d, w);
  if (pool.length === 0) return failed('sales', quiet);
  const started = Date.now();
  const r = Math.random();
  const tier = w === 0 ? (r < 0.04 ? 'large' : r < 0.12 ? 'standard' : 'none') : 'none';
  const lines = [];
  if (tier === 'none') {
    const n = 1 + rnd(3); const used = {};
    for (let i = 0; i < n; i++) {
      const p = pool[Math.floor(pool.length * Math.pow(Math.random(), 2))];
      if (used[p.id]) continue; used[p.id] = 1;
      lines.push({ productId: p.id, qty: 1 + rnd(3), uomId: null, unitPrice: round2(p.avg * 1.3 + 1), remark: null });
    }
  } else {
    const p = pool[rnd(Math.min(pool.length, 20))];
    const price = round2(p.avg * 1.3 + 1);
    const qty = Math.ceil((tier === 'large' ? 21000 : 5600) / price);
    if (qty > Math.floor(p.qty / 4)) return failed('sales', quiet);   // not enough stock to deliver it
    lines.push({ productId: p.id, qty, uomId: null, unitPrice: price, remark: null });
  }
  // supervisor, staff and clerk see the main warehouse only; an order that needs approval is created by someone other than the approver
  const creator = tier !== 'none' ? pick([d.tokens.clerk, d.tokens.supervisor]) : (w === 0 ? pick([d.tokens.clerk, d.tokens.supervisor, d.tokens.admin]) : d.tokens.admin);
  const approver = tier === 'large' ? d.tokens.owner : d.tokens.admin;
  const created = post(creator, '/api/sales-orders', { warehouseId: wh, customerId: pick(d.customers), remark: 'k6 load', lines }, 'so.create');
  if (created.status !== 200) return failed('sales', quiet);
  const id = created.json('id');
  let last = post(creator, `/api/sales-orders/${id}/submit`, undefined, 'so.submit', 'write', created);
  if (last.status !== 200) return failed('sales', quiet);
  if (!isApproved(last)) {
    last = post(approver, `/api/sales-orders/${id}/approve`, undefined, 'so.approve', 'write', last);
    if (last.status !== 200) return failed('sales', quiet);
  }
  if (!deliver) return id;
  // the delivery is an issue document made from the order's open lines; the demo staff (main warehouse) or the admin create it
  const dCreator = w === 0 ? d.tokens.staff : d.tokens.admin;
  const delivery = post(dCreator, `/api/sales-orders/${id}/delivery`, undefined, 'delivery.create');
  if (delivery.status !== 200) return failed('sales', quiet);
  const did = delivery.json('id');
  let dLast = post(dCreator, `/api/documents/${did}/submit`, undefined, 'delivery.submit', 'write', delivery);
  if (dLast.status !== 200) return failed('sales', quiet);
  if (dLast.json('requiresApproval') === true && !isApproved(dLast)) {
    dLast = post(d.tokens.admin, `/api/documents/${did}/approve`, undefined, 'delivery.approve', 'write', dLast);
    if (dLast.status !== 200) return failed('sales', quiet);
  }
  const posted = post(w === 0 ? d.tokens.supervisor : d.tokens.admin, `/api/documents/${did}/post`, undefined, 'delivery.post', 'write', dLast);
  if (posted.status !== 200) return failed('sales', quiet);
  done('sales', started);
  return id;
}

// issue (purpose Sale) -> submit -> post, then a customer return of it: POST /api/returns -> submit -> (approve) -> post.
export function returnFlow(d) {
  const w = 0;   // main warehouse only: a return needs an approval (a rule fires on it) and the admin is the only reviewer who sees the other warehouses, but cannot approve what the admin submitted
  const wh = d.warehouses[w];
  const pool = poolOf(d, w);
  if (pool.length === 0) return failed('return');
  const started = Date.now();
  const creator = w === 0 ? pick([d.tokens.supervisor, d.tokens.staff]) : d.tokens.admin;
  const poster = w === 0 ? d.tokens.supervisor : d.tokens.admin;
  const lines = []; const used = {};
  for (let i = 0, n = 1 + rnd(3); i < n; i++) {
    const p = pool[Math.floor(pool.length * Math.pow(Math.random(), 2))];
    if (used[p.id]) continue; used[p.id] = 1;
    lines.push({ productId: p.id, qty: 1 + rnd(3) });
  }
  const issue = post(creator, '/api/documents/issues', { warehouseId: wh, partnerId: pick(d.customers), purpose: 1, remark: 'k6 load', lines }, 'ret.issue.create');
  if (issue.status !== 200) return failed('return');
  let last = post(creator, `/api/documents/${issue.json('id')}/submit`, undefined, 'ret.issue.submit', 'write', issue);
  if (last.status !== 200) return failed('return');
  last = post(poster, `/api/documents/${issue.json('id')}/post`, undefined, 'ret.issue.post', 'write', last);
  if (last.status !== 200) return failed('return');
  const ret = post(creator, '/api/returns', { originalDocumentId: issue.json('id') }, 'return.create');
  if (ret.status !== 200) return failed('return');
  const rid = ret.json('id');
  let rLast = post(creator, `/api/documents/${rid}/submit`, undefined, 'return.submit', 'write', ret);
  if (rLast.status !== 200) return failed('return');
  if (rLast.json('requiresApproval') === true && !isApproved(rLast)) {
    rLast = post(d.tokens.admin, `/api/documents/${rid}/approve`, undefined, 'return.approve', 'write', rLast);
    if (rLast.status !== 200) return failed('return');
  }
  rLast = post(poster, `/api/documents/${rid}/post`, undefined, 'return.post', 'write', rLast);
  if (rLast.status !== 200) return failed('return');
  done('return', started);
  return rid;
}

// receipt -> submit -> post, then the reversal of the posted receipt (POST /api/documents/{id}/reverse with a reason).
export function reversalFlow(d) {
  const w = whIndex(d);
  const wh = d.warehouses[w];
  const started = Date.now();
  const creator = w === 0 ? pick([d.tokens.supervisor, d.tokens.staff]) : d.tokens.admin;
  const poster = w === 0 ? d.tokens.supervisor : d.tokens.admin;
  const lines = []; const used = {};
  for (let i = 0, n = 1 + rnd(3); i < n; i++) {
    const pid = pick(d.productPool);
    if (used[pid] || d.inactive[pid]) continue; used[pid] = 1;
    lines.push({ productId: pid, qty: 10 + rnd(90), unitCost: 5 + rnd(50) });
  }
  if (lines.length === 0) return failed('reversal');
  const receipt = post(creator, '/api/documents/receipts', { warehouseId: wh, partnerId: null, purpose: null, remark: 'k6 load', lines }, 'rev.create');
  if (receipt.status !== 200) return failed('reversal');
  let last = post(creator, `/api/documents/${receipt.json('id')}/submit`, undefined, 'rev.submit', 'write', receipt);
  if (last.status !== 200) return failed('reversal');
  last = post(poster, `/api/documents/${receipt.json('id')}/post`, undefined, 'rev.post', 'write', last);
  if (last.status !== 200) return failed('reversal');
  const reversed = post(poster, `/api/documents/${receipt.json('id')}/reverse`, { reason: 'k6 load reversal' }, 'doc.reverse', 'write', last);
  if (reversed.status !== 200) return failed('reversal');
  done('reversal', started);
  return receipt.json('id');
}
