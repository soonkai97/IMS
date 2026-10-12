// Shared helpers for the IMS k6 load tests (how to run them: tests/load/README.md).
//
// Environment (all optional):
//   BASE_URL      default http://localhost:5109
//   ADMIN_PASSWORD / DEMO_PASSWORD   default: Seed:AdminPassword / Seed:DemoPassword of src/Ims.Api/appsettings.Development.json
//   OUT_DIR       where handleSummary writes <scenario>.json (default tests/load/results, git-ignored)
//   WAREHOUSES    number of warehouses to spread writes over (default 5, matching the 1M/2M seed tiers)
//   HOT_PRODUCTS  size of the hot product set for the hot variants (default 5)
//
// Login is rate limited (20/min per IP), so every scenario logs in ONCE in setup() and shares the five tokens
// with all VUs. Access tokens last 15 minutes: scenarios longer than ~12 minutes re-login in setup() hourly
// or are split; 401s are counted (http_401) so an expiry is visible instead of silent.
import http from 'k6/http';
import { check, fail } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

export const BASE = __ENV.BASE_URL || 'http://localhost:5109';
const SEED = JSON.parse(open('../../src/Ims.Api/appsettings.Development.json')).Seed;   // open() runs in the init context, path is relative to this file
const ADMIN_PW = __ENV.ADMIN_PASSWORD || SEED.AdminPassword;
const DEMO_PW = __ENV.DEMO_PASSWORD || SEED.DemoPassword;
const WAREHOUSES = parseInt(__ENV.WAREHOUSES || '5');
const HOT_N = parseInt(__ENV.HOT_PRODUCTS || '5');

// --- custom metrics ---------------------------------------------------------------------------------------------
export const c5xx = new Counter('http_5xx');             // any 5xx except 504 (counted separately)
export const c504 = new Counter('http_504');             // request timeout (30 s default, 120 s exports)
export const c409 = new Counter('http_409');             // concurrency conflict that survived the server-side retry
export const c401 = new Counter('http_401');             // expired/invalid token
export const c4xx = new Counter('http_4xx_other');       // other client errors (business rule rejections)
export const writeFlows = new Counter('write_flows_ok'); // completed create->submit->(approve)->post flows
export const writeFlowFail = new Counter('write_flows_failed');
export const flowTime = new Trend('flow_duration', true); // whole create->post flow

// default thresholds shared by all scenarios
export const THRESHOLDS = {
  'http_req_duration{kind:read}': ['p(95)<1000'],
  'http_req_duration{kind:write}': ['p(95)<2000'],
  http_req_failed: ['rate<0.01'],
  http_5xx: ['count==0'],
  http_504: ['count==0'],
  http_409: ['count==0'],
};

// --- setup: login once, discover ids -----------------------------------------------------------------------------
function login(username, password) {
  const r = http.post(`${BASE}/api/auth/login`, JSON.stringify({ username, password }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } });
  if (r.status !== 200) fail(`login ${username} failed: ${r.status} ${r.body}`);
  return r.json('accessToken');
}

function getJson(path, token) {
  const r = http.get(`${BASE}${path}`, { headers: { Authorization: `Bearer ${token}` }, tags: { name: 'setup' } });
  if (r.status !== 200) fail(`GET ${path} -> ${r.status} ${r.body}`);
  return r.json();
}

// The nextCursor of up to 10 OFFSET pages spread over the first 10,000 rows (the OFFSET limit): a seek costs the same at any depth, so these are deep enough.
function cursorsOf(path, size, token) {
  const lastPage = Math.floor(10000 / size);
  const step = Math.ceil(lastPage / 10);
  const cursors = [];
  for (let page = 1; page <= lastPage; page += step) {
    const next = getJson(`${path}?page=${page}&pageSize=${size}&includeTotal=false`, token).nextCursor;
    if (next) cursors.push(next);
  }
  return cursors;
}

export function setupData() {
  const tokens = {
    admin: login('admin', ADMIN_PW),
    owner: login('owner', DEMO_PW),
    supervisor: login('supervisor', DEMO_PW),
    staff: login('staff', DEMO_PW),
    clerk: login('clerk', DEMO_PW),
  };
  // product ids: SKU-00001 is the hottest product of the seed; ids are sequential after the two demo products
  // inactive products are rejected by the business rules (400): exclude them so the load test measures the system, not validation
  const inactive = {};
  for (let pg = 1; pg <= 30; pg++) {
    const r = getJson(`/api/products?active=false&pageSize=200&page=${pg}`, tokens.admin);
    for (const it of r.items) inactive[it.id] = 1;
    if (r.items.length < 200) break;
  }
  const first = getJson('/api/products/lookup?code=SKU-00001', tokens.admin);
  const offset = first.product.id - 1;
  const hot = [];
  for (let i = 1; i <= HOT_N; i++) hot.push(offset + i);
  const stocked = {};   // per warehouse: products with plenty of stock to issue from [{id, qty, avg}]
  for (let w = 1; w <= WAREHOUSES; w++) {
    const page = getJson(`/api/stock?warehouseId=${w}&sortBy=QtyOnHand&desc=true&pageSize=300`, tokens.admin);
    stocked[w] = page.items.filter((x) => x.qtyOnHand >= 500 && x.avgCost > 0 && !inactive[x.productId])
      .map((x) => ({ id: x.productId, qty: x.qtyOnHand, avg: x.avgCost }));
  }
  const stockedHot = [];   // hot products that have stock in warehouse 1 (hot variant issues)
  const w1 = getJson('/api/stock?warehouseId=1&sortBy=QtyOnHand&desc=true&pageSize=300', tokens.admin).items;
  for (const p of w1) if (hot.indexOf(p.productId) >= 0) stockedHot.push({ id: p.productId, qty: p.qtyOnHand, avg: p.avgCost });
  const ledger = getJson('/api/stock/ledger?pageSize=1', tokens.admin);
  const docs = getJson('/api/documents?pageSize=1', tokens.admin);
  const sku = getJson('/api/stock?pageSize=1', tokens.admin);
  const ledgerCursors = cursorsOf('/api/stock/ledger', 500, tokens.admin);
  const docCursors = cursorsOf('/api/documents', 200, tokens.admin);
  return {
    ledgerCursors, docCursors,
    tokens, offset, hot, stocked, stockedHot, inactive, maxDocId: docs.items[0].id, ledgerTotal: ledger.total, docTotal: docs.total,
    stockTotal: sku.total, products: 50000,
  };
}

// --- request helpers ---------------------------------------------------------------------------------------------
let dbgCount = 0;
function track(r) {
  if (__ENV.DEBUG && r.status >= 400 && dbgCount++ < 8) console.log(`${r.request.method} ${r.url} -> ${r.status} ${String(r.body).slice(0, 300)}`);
  if (r.status === 401) c401.add(1);
  else if (r.status === 409) c409.add(1);
  else if (r.status === 504) c504.add(1);
  else if (r.status >= 500) c5xx.add(1);
  else if (r.status >= 400) c4xx.add(1);
  return r;
}

export function headers(token) { return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }; }

export function get(token, path, name, kind = 'read', timeout = '35s') {
  const r = http.get(`${BASE}${path}`, { headers: headers(token), tags: { kind, name }, timeout });
  check(r, { [`${name} 200`]: (x) => x.status === 200 });
  return track(r);
}

export function post(token, path, body, name, kind = 'write') {
  const r = http.post(`${BASE}${path}`, body === undefined ? null : JSON.stringify(body),
    { headers: headers(token), tags: { kind, name }, timeout: '35s' });
  check(r, { [`${name} 200`]: (x) => x.status === 200 });
  return track(r);
}

// --- random helpers ----------------------------------------------------------------------------------------------
export function rnd(n) { return Math.floor(Math.random() * n); }
export function pick(a) { return a[rnd(a.length)]; }
// A cursor is opaque and encrypted: it is collected from the API in setup(), never computed from an id.
function cursorQuery(cursors) { return cursors.length ? `cursor=${pick(cursors)}&` : ''; }
export function zipf(n) { return 1 + Math.floor(n * Math.pow(Math.random(), 2)); }   // same skew as the seed

// --- READ operations (weights roughly follow a stock-clerk day) ----------------------------------------------------
export function readOp(d) {
  const t = Math.random() < 0.5 ? d.tokens.admin : d.tokens.owner;   // both see every warehouse
  const x = Math.random() * 100;
  if (x < 20) {                         // stock enquiry, various filters
    const f = rnd(7);
    if (f === 0) return get(t, '/api/stock', 'stock');
    if (f === 1) return get(t, `/api/stock?warehouseId=${1 + rnd(WAREHOUSES)}`, 'stock?warehouse');
    if (f === 2) return get(t, '/api/stock?search=SKU-' + String(zipf(d.products)).padStart(5, '0'), 'stock?search sku');
    if (f === 3) return get(t, '/api/stock?search=bolt', 'stock?search name');
    if (f === 4) return get(t, '/api/stock?lowOnly=true', 'stock?lowOnly');
    if (f === 5) return get(t, `/api/stock?categoryId=${2 + rnd(49)}`, 'stock?category');
    return get(t, `/api/stock?productId=${d.offset + zipf(d.products)}`, 'stock?product');
  }
  if (x < 40) {                         // ledger by product (Zipf: hot products are asked for more often)
    const f = rnd(4);
    const pid = d.offset + zipf(d.products);
    if (f === 0) return get(t, `/api/stock/ledger?productId=${pid}`, 'ledger?product');
    if (f === 1) return get(t, `/api/stock/ledger?productId=${pid}&warehouseId=${1 + rnd(WAREHOUSES)}`, 'ledger?product+wh');
    if (f === 2) return get(t, '/api/stock/ledger?includeTotal=false', 'ledger?p1');
    return get(t, `/api/stock/ledger?${cursorQuery(d.ledgerCursors)}includeTotal=false`, 'ledger?cursor');
  }
  if (x < 60) {                         // document lists
    const f = rnd(5);
    if (f === 0) return get(t, '/api/documents', 'documents');
    if (f === 1) return get(t, '/api/documents?type=2&status=3', 'documents?type+status');
    if (f === 2) return get(t, `/api/documents?warehouseId=${1 + rnd(WAREHOUSES)}&type=1`, 'documents?wh+type');
    if (f === 3) return get(t, `/api/documents?${cursorQuery(d.docCursors)}includeTotal=false`, 'documents?cursor');
    return get(t, '/api/documents?status=0', 'documents?draft');
  }
  if (x < 75) return get(t, `/api/documents/${1 + rnd(d.maxDocId)}`, 'documents/{id}');
  if (x < 87) {                         // product search
    const f = rnd(3);
    if (f === 0) return get(t, '/api/products?search=SKU-' + String(zipf(d.products)).padStart(5, '0'), 'products?search sku');
    if (f === 1) return get(t, '/api/products?search=bolt', 'products?search name');
    return get(t, `/api/products/lookup?code=SKU-${String(zipf(d.products)).padStart(5, '0')}`, 'products/lookup');
  }
  if (x < 94) return get(t, '/api/stock/low-stock', 'low-stock');
  return get(t, '/api/stock/summary', 'summary');
}

// --- WRITE flows ---------------------------------------------------------------------------------------------------
// opts.hot: every flow touches the same few hot products in warehouse 1 (maximum balance + sequence contention)
export function writeFlow(d, opts = {}) {
  const hot = !!opts.hot;
  const wh = hot ? 1 : 1 + rnd(WAREHOUSES);
  const isIssue = Math.random() < 0.5;
  const approval = isIssue && wh === 1 && Math.random() < 0.1;     // ~5 % of all flows take the approval path
  // supervisor/staff/clerk are scoped to warehouse 1 in the demo seed; admin and owner see every warehouse
  const creator = approval ? d.tokens.staff : (wh === 1 ? pick([d.tokens.admin, d.tokens.supervisor, d.tokens.staff]) : d.tokens.admin);
  const poster = (wh === 1 && Math.random() < 0.5) ? d.tokens.supervisor : d.tokens.admin;
  const approver = d.tokens.admin;
  const started = Date.now();
  const lines = [];
  const n = hot ? 1 + rnd(3) : 1 + rnd(5);
  const used = {};
  if (!isIssue) {
    for (let i = 0; i < n; i++) {
      const pid = hot ? pick(d.hot) : d.offset + zipf(d.products);
      if (d.inactive[pid]) continue;
      if (used[pid]) continue; used[pid] = 1;
      lines.push({ productId: pid, qty: 10 + rnd(90), unitCost: 5 + rnd(50) });
    }
  } else {
    const pool = hot ? (d.stockedHot.length ? d.stockedHot : d.stocked[1]) : d.stocked[wh];
    if (!pool || pool.length === 0) { writeFlowFail.add(1); return; }
    if (approval) {
      const p = pool[rnd(Math.min(pool.length, 20))];                 // big qty => amount >= 5,000 => needs approval
      lines.push({ productId: p.id, qty: Math.min(Math.ceil(5600 / p.avg), Math.floor(p.qty / 4)) });
    } else {
      for (let i = 0; i < n; i++) {
        const p = hot ? pool[rnd(pool.length)] : pool[Math.floor(pool.length * Math.pow(Math.random(), 2))];
        if (used[p.id]) continue; used[p.id] = 1;
        lines.push({ productId: p.id, qty: 1 + rnd(3) });
      }
    }
  }
  if (lines.length === 0) return;
  const body = { warehouseId: wh, partnerId: null, purpose: isIssue ? 1 : null, remark: 'k6 load', lines };
  const created = post(creator, isIssue ? '/api/documents/issues' : '/api/documents/receipts', body, 'doc.create');
  if (created.status !== 200) { writeFlowFail.add(1); return; }
  const id = created.json('id');
  const submitted = post(creator, `/api/documents/${id}/submit`, undefined, 'doc.submit');
  if (submitted.status !== 200) { writeFlowFail.add(1); return; }
  if (submitted.json('requiresApproval') === true && submitted.json('status') !== 'Approved' && submitted.json('status') !== 2) {
    const ap = post(approver, `/api/documents/${id}/approve`, undefined, 'doc.approve');
    if (ap.status !== 200) { writeFlowFail.add(1); return; }
  }
  const posted = post(poster, `/api/documents/${id}/post`, undefined, 'doc.post');
  if (posted.status === 200) { writeFlows.add(1); flowTime.add(Date.now() - started); } else writeFlowFail.add(1);
}

// --- summary output ----------------------------------------------------------------------------------------------------
export function summaryTo(name) {
  return function (data) {
    const dir = __ENV.OUT_DIR || 'tests/load/results';
    const m = data.metrics;
    const val = (k, s) => (m[k] && m[k].values ? m[k].values[s] : undefined);
    const compact = {
      scenario: name, duration_s: data.state.testRunDurationMs / 1000,
      vus_max: val('vus_max', 'max'), reqs: val('http_reqs', 'count'), rps: val('http_reqs', 'rate'),
      failed_rate: val('http_req_failed', 'rate'),
      all: { p50: val('http_req_duration', 'med'), p95: val('http_req_duration', 'p(95)'), p99: val('http_req_duration', 'p(99)'), max: val('http_req_duration', 'max') },
      read: { p50: val('http_req_duration{kind:read}', 'med'), p95: val('http_req_duration{kind:read}', 'p(95)'), p99: val('http_req_duration{kind:read}', 'p(99)'), max: val('http_req_duration{kind:read}', 'max'), n: val('http_req_duration{kind:read}', 'count') },
      write: { p50: val('http_req_duration{kind:write}', 'med'), p95: val('http_req_duration{kind:write}', 'p(95)'), p99: val('http_req_duration{kind:write}', 'p(99)'), max: val('http_req_duration{kind:write}', 'max'), n: val('http_req_duration{kind:write}', 'count') },
      flow: { p50: val('flow_duration', 'med'), p95: val('flow_duration', 'p(95)'), ok: val('write_flows_ok', 'count'), failed: val('write_flows_failed', 'count') },
      c5xx: val('http_5xx', 'count') || 0, c504: val('http_504', 'count') || 0, c409: val('http_409', 'count') || 0, c401: val('http_401', 'count') || 0,
      c4xx_other: val('http_4xx_other', 'count') || 0,
      thresholds: Object.fromEntries(Object.entries(m).filter(([, v]) => v.thresholds).map(([k, v]) => [k, Object.fromEntries(Object.entries(v.thresholds).map(([t, r]) => [t, r.ok]))])),
      by_name: {},
    };
    for (const [k, v] of Object.entries(m)) {
      const mt = k.match(/^http_req_duration\{name:(.+)\}$/);
      if (mt) compact.by_name[mt[1]] = { n: v.values.count, p50: v.values.med, p95: v.values['p(95)'], max: v.values.max };
    }
    return { [`${dir}/${name}.json`]: JSON.stringify(compact, null, 2), stdout: textLine(compact) };
  };
}

function textLine(c) {
  const f = (x) => (x === undefined || x === null ? '-' : x.toFixed(0));
  return `\n== ${c.scenario}: ${c.reqs} reqs, ${c.rps.toFixed(1)} req/s, vus_max ${c.vus_max}, failed ${(c.failed_rate * 100).toFixed(2)}%\n` +
    `   read  p50/p95/p99/max = ${f(c.read.p50)}/${f(c.read.p95)}/${f(c.read.p99)}/${f(c.read.max)} ms (n=${c.read.n || 0})\n` +
    `   write p50/p95/p99/max = ${f(c.write.p50)}/${f(c.write.p95)}/${f(c.write.p99)}/${f(c.write.max)} ms (n=${c.write.n || 0})\n` +
    `   flows ok ${c.flow.ok || 0} failed ${c.flow.failed || 0} | 5xx ${c.c5xx} 504 ${c.c504} 409 ${c.c409} 401 ${c.c401} other4xx ${c.c4xx_other}\n`;
}
