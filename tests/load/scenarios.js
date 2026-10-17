// Shared helpers for the IMS k6 load tests (how to run them: tests/load/README.md).
//
// Environment (all optional):
//   BASE_URL      default http://localhost:5109
//   ADMIN_PASSWORD / DEMO_PASSWORD   default: Seed:AdminPassword / Seed:DemoPassword of src/Ims.Api/appsettings.Development.json
//   OUT_DIR       where handleSummary writes <scenario>.json (default tests/load/results, git-ignored)
//   WAREHOUSES    number of warehouses to spread writes over (default 5, matching the 1M/2M seed tiers; fewer if the database has fewer)
//   HOT_PRODUCTS  size of the hot product set for the hot variants (default 5)
//   PRODUCT_DRAWS Zipf draws used to sample real product ids in setup() (default 600; about 450 look-ups)
//   DOC_PAGES     pages of 200 documents walked by cursor in setup() to sample document ids (default 100: the newest 20,000)
//
// Ids are opaque public ids (GUID strings) and cursors are encrypted: nothing here computes one. setup() reads real ids from
// the API (warehouses, categories, products by SKU, documents by walking nextCursor) and hands them to the scenarios.
// The API rate limit must be raised for a run (tests/load/README.md): setup() makes about 700 calls in 30 seconds or more.
//
// Login is rate limited (20/min per IP), so every scenario logs in ONCE in setup() and shares the five tokens
// with all VUs. Access tokens last 5 minutes by default, so start the API for a load run with Jwt__AccessMinutes=30 (tests/load/README.md); a scenario longer than ~25 minutes must be split;
// 401s are counted (http_401) so an expiry is visible instead of silent.
import http from 'k6/http';
import { check, fail } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

export const BASE = __ENV.BASE_URL || 'http://localhost:5109';
const SEED = JSON.parse(open('../../src/Ims.Api/appsettings.Development.json')).Seed;   // open() runs in the init context, path is relative to this file
const ADMIN_PW = __ENV.ADMIN_PASSWORD || SEED.AdminPassword;
const DEMO_PW = __ENV.DEMO_PASSWORD || SEED.DemoPassword;
const WAREHOUSES = parseInt(__ENV.WAREHOUSES || '5');
const HOT_N = parseInt(__ENV.HOT_PRODUCTS || '5');
const PRODUCT_DRAWS = parseInt(__ENV.PRODUCT_DRAWS || '600');
const DOC_PAGES = parseInt(__ENV.DOC_PAGES || '100');

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

// Real document ids and cursors: walks /api/documents from the newest document, 200 at a time, by nextCursor (the only way to go deep).
// Keeps 8 random ids of every page and every cursor on the way, so the by-id reads and the deep-cursor reads spread over the walked rows.
function walkDocuments(token) {
  const ids = [];
  const cursors = [];
  let cursor = null;
  for (let page = 0; page < DOC_PAGES; page++) {
    const r = getJson(`/api/documents?pageSize=200&includeTotal=false${cursor ? `&cursor=${cursor}` : ''}`, token);
    for (let i = 0; i < 8 && r.items.length; i++) ids.push(pick(r.items).id);
    cursor = r.nextCursor;
    if (!cursor) break;
    cursors.push(cursor);
  }
  return { ids, cursors };
}

function skuOf(n) { return 'SKU-' + String(n).padStart(5, '0'); }

// Real product ids for a Zipf-skewed workload. The seed names its products SKU-00001... and makes the low numbers the hot ones, so the
// draws are SKU numbers and each distinct one is resolved once through /api/products/lookup. The pool keeps the repeats: picking from it
// is the same skew as drawing a Zipf number, without computing an id.
function sampleProducts(total, token) {
  const draws = [];
  for (let i = 0; i < PRODUCT_DRAWS; i++) draws.push(zipf(total));
  const idOf = {};
  for (const n of draws) {
    if (idOf[n] !== undefined) continue;
    const r = getJson(`/api/products/lookup?code=${skuOf(n)}`, token);
    idOf[n] = r.found ? r.product.id : null;
  }
  return draws.map((n) => idOf[n]).filter((id) => id);
}

export function setupData() {
  const tokens = {
    admin: login('admin', ADMIN_PW),
    owner: login('owner', DEMO_PW),
    supervisor: login('supervisor', DEMO_PW),
    staff: login('staff', DEMO_PW),
    clerk: login('clerk', DEMO_PW),
  };
  // warehouses: the first one is the main warehouse, the only one the demo supervisor, staff and clerk are assigned to
  const mainId = getJson('/api/auth/me', tokens.staff).warehouseIds[0];
  const all = getJson('/api/warehouses', tokens.admin).filter((w) => w.isActive);
  const warehouses = [all.find((w) => w.id === mainId)].concat(all.filter((w) => w.id !== mainId)).slice(0, WAREHOUSES).map((w) => w.id);
  const categories = getJson('/api/categories', tokens.admin);
  const seeded = categories.filter((c) => /^Category /.test(c.name));   // the perf seed's categories; the built-in ones hold no products
  const categoryIds = (seeded.length ? seeded : categories).map((c) => c.id);
  // inactive products are rejected by the business rules (400): exclude them so the load test measures the system, not validation
  const inactive = {};
  for (let pg = 1; pg <= 30; pg++) {
    const r = getJson(`/api/products?active=false&pageSize=200&page=${pg}`, tokens.admin);
    for (const it of r.items) inactive[it.id] = 1;
    if (r.items.length < 200) break;
  }
  const products = getJson('/api/products?pageSize=1', tokens.admin).total;
  const hot = [];   // SKU-00001 is the hottest product of the seed
  for (let i = 1; i <= HOT_N; i++) {
    const r = getJson(`/api/products/lookup?code=${skuOf(i)}`, tokens.admin);
    if (r.found) hot.push(r.product.id);
  }
  const productPool = sampleProducts(products, tokens.admin);
  const stocked = warehouses.map((w) => {   // per warehouse (same order as `warehouses`): products with plenty of stock to issue from [{id, qty, avg}]
    const page = getJson(`/api/stock?warehouseId=${w}&sortBy=QtyOnHand&desc=true&pageSize=300`, tokens.admin);
    return page.items.filter((x) => x.qtyOnHand >= 500 && x.avgCost > 0 && !inactive[x.productId])
      .map((x) => ({ id: x.productId, qty: x.qtyOnHand, avg: x.avgCost }));
  });
  const stockedHot = [];   // hot products that have stock in the main warehouse (hot variant issues)
  const main = getJson(`/api/stock?warehouseId=${warehouses[0]}&sortBy=QtyOnHand&desc=true&pageSize=300`, tokens.admin).items;
  for (const p of main) if (hot.indexOf(p.productId) >= 0) stockedHot.push({ id: p.productId, qty: p.qtyOnHand, avg: p.avgCost });
  const ledger = getJson('/api/stock/ledger?pageSize=1', tokens.admin);
  const docs = getJson('/api/documents?pageSize=1', tokens.admin);
  const sku = getJson('/api/stock?pageSize=1', tokens.admin);
  const ledgerCursors = cursorsOf('/api/stock/ledger', 500, tokens.admin);
  const walked = walkDocuments(tokens.admin);
  return {
    ledgerCursors, docCursors: walked.cursors, docIds: walked.ids,
    tokens, warehouses, categoryIds, hot, productPool, stocked, stockedHot, inactive,
    ledgerTotal: ledger.total, docTotal: docs.total, stockTotal: sku.total, products,
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

// A document command must carry the document version it acts on in If-Match (428 otherwise): pass the previous response of the same document as `after`.
export function post(token, path, body, name, kind = 'write', after = undefined) {
  const h = headers(token);
  if (after) h['If-Match'] = `"${after.json('concurrencyToken')}"`;
  const r = http.post(`${BASE}${path}`, body === undefined ? null : JSON.stringify(body),
    { headers: h, tags: { kind, name }, timeout: '35s' });
  check(r, { [`${name} 200`]: (x) => x.status === 200 });
  return track(r);
}

// --- random helpers ----------------------------------------------------------------------------------------------
export function rnd(n) { return Math.floor(Math.random() * n); }
export function pick(a) { return a[rnd(a.length)]; }
// A cursor is opaque and encrypted: it is collected from the API in setup(), never computed from an id.
function cursorQuery(cursors) { return cursors.length ? `cursor=${pick(cursors)}&` : ''; }
export function zipf(n) { return 1 + Math.floor(n * Math.pow(Math.random(), 2)); }   // same skew as the seed
// A Zipf-skewed real product id (the pool was drawn that way in setup()).
function product(d) { return pick(d.productPool); }

// --- READ operations (weights roughly follow a stock-clerk day) ----------------------------------------------------
export function readOp(d) {
  const t = Math.random() < 0.5 ? d.tokens.admin : d.tokens.owner;   // both see every warehouse
  const x = Math.random() * 100;
  if (x < 20) {                         // stock enquiry, various filters
    const f = rnd(7);
    if (f === 0) return get(t, '/api/stock', 'stock');
    if (f === 1) return get(t, `/api/stock?warehouseId=${pick(d.warehouses)}`, 'stock?warehouse');
    if (f === 2) return get(t, '/api/stock?search=' + skuOf(zipf(d.products)), 'stock?search sku');
    if (f === 3) return get(t, '/api/stock?search=bolt', 'stock?search name');
    if (f === 4) return get(t, '/api/stock?lowOnly=true', 'stock?lowOnly');
    if (f === 5) return get(t, `/api/stock?categoryId=${pick(d.categoryIds)}`, 'stock?category');
    return get(t, `/api/stock?productId=${product(d)}`, 'stock?product');
  }
  if (x < 40) {                         // ledger by product (Zipf: hot products are asked for more often)
    const f = rnd(4);
    const pid = product(d);
    if (f === 0) return get(t, `/api/stock/ledger?productId=${pid}`, 'ledger?product');
    if (f === 1) return get(t, `/api/stock/ledger?productId=${pid}&warehouseId=${pick(d.warehouses)}`, 'ledger?product+wh');
    if (f === 2) return get(t, '/api/stock/ledger?includeTotal=false', 'ledger?p1');
    return get(t, `/api/stock/ledger?${cursorQuery(d.ledgerCursors)}includeTotal=false`, 'ledger?cursor');
  }
  if (x < 60) {                         // document lists
    const f = rnd(5);
    if (f === 0) return get(t, '/api/documents', 'documents');
    if (f === 1) return get(t, '/api/documents?type=2&status=3', 'documents?type+status');
    if (f === 2) return get(t, `/api/documents?warehouseId=${pick(d.warehouses)}&type=1`, 'documents?wh+type');
    if (f === 3) return get(t, `/api/documents?${cursorQuery(d.docCursors)}includeTotal=false`, 'documents?cursor');
    return get(t, '/api/documents?status=0', 'documents?draft');
  }
  if (x < 75) return get(t, `/api/documents/${pick(d.docIds)}`, 'documents/{id}');
  if (x < 87) {                         // product search
    const f = rnd(3);
    if (f === 0) return get(t, '/api/products?search=' + skuOf(zipf(d.products)), 'products?search sku');
    if (f === 1) return get(t, '/api/products?search=bolt', 'products?search name');
    return get(t, `/api/products/lookup?code=${skuOf(zipf(d.products))}`, 'products/lookup');
  }
  if (x < 94) return get(t, '/api/stock/low-stock', 'low-stock');
  return get(t, '/api/stock/summary', 'summary');
}

// --- WRITE flows ---------------------------------------------------------------------------------------------------
// opts.hot: every flow touches the same few hot products in warehouse 1 (maximum balance + sequence contention)
export function writeFlow(d, opts = {}) {
  const hot = !!opts.hot;
  const w = hot ? 0 : rnd(d.warehouses.length);                    // index into d.warehouses; 0 is the main warehouse
  const wh = d.warehouses[w];
  const isIssue = Math.random() < 0.5;
  const approval = isIssue && w === 0 && Math.random() < 0.1;      // ~5 % of all flows take the approval path
  // supervisor/staff/clerk are scoped to the main warehouse in the demo seed; admin and owner see every warehouse
  const creator = approval ? d.tokens.staff : (w === 0 ? pick([d.tokens.admin, d.tokens.supervisor, d.tokens.staff]) : d.tokens.admin);
  const poster = (w === 0 && Math.random() < 0.5) ? d.tokens.supervisor : d.tokens.admin;
  const approver = d.tokens.admin;
  const started = Date.now();
  const lines = [];
  const n = hot ? 1 + rnd(3) : 1 + rnd(5);
  const used = {};
  if (!isIssue) {
    for (let i = 0; i < n; i++) {
      const pid = hot ? pick(d.hot) : product(d);
      if (d.inactive[pid]) continue;
      if (used[pid]) continue; used[pid] = 1;
      lines.push({ productId: pid, qty: 10 + rnd(90), unitCost: 5 + rnd(50) });
    }
  } else {
    const pool = hot ? (d.stockedHot.length ? d.stockedHot : d.stocked[0]) : d.stocked[w];
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
  const submitted = post(creator, `/api/documents/${id}/submit`, undefined, 'doc.submit', 'write', created);
  if (submitted.status !== 200) { writeFlowFail.add(1); return; }
  let last = submitted;
  if (submitted.json('requiresApproval') === true && submitted.json('status') !== 'Approved' && submitted.json('status') !== 2) {
    const ap = post(approver, `/api/documents/${id}/approve`, undefined, 'doc.approve', 'write', submitted);
    if (ap.status !== 200) { writeFlowFail.add(1); return; }
    last = ap;
  }
  const posted = post(poster, `/api/documents/${id}/post`, undefined, 'doc.post', 'write', last);
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
