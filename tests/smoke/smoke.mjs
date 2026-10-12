// End-to-end smoke test against a running API (default http://localhost:5109, Development seed).
// Usage: IMS_URL=http://localhost:5109 IMS_ADMIN_PASSWORD=... IMS_DEMO_PASSWORD=... node tests/smoke/smoke.mjs
const BASE = process.env.IMS_URL ?? 'http://localhost:5109';
const ADMIN_PASSWORD = process.env.IMS_ADMIN_PASSWORD;
const DEMO_PASSWORD = process.env.IMS_DEMO_PASSWORD;
if (!ADMIN_PASSWORD || !DEMO_PASSWORD) {
  console.error('Set IMS_ADMIN_PASSWORD and IMS_DEMO_PASSWORD (see src/Ims.Api/appsettings.Development.json).');
  process.exit(2);
}

let passed = 0;
const failures = [];
const check = (name, ok, detail = '') => {
  if (ok) { passed++; console.log(`  ok   ${name}`); }
  else { failures.push(name); console.log(`  FAIL ${name} ${detail}`); }
};

// Every document command must carry the document version in If-Match (428 otherwise); the smoke test uses the explicit "any version" wildcard.
const DOCUMENT_COMMAND = /^\/api\/documents\/\d+(\/(submit|approve|reject|cancel|post|reverse))?$/;
const needsIfMatch = (method, path) => DOCUMENT_COMMAND.test(path) && (method === 'PUT' || (method === 'POST' && path.split('/').length === 5));

async function api(method, path, { token, body, headers } = {}) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(needsIfMatch(method, path) ? { 'If-Match': '*' } : {}), ...(headers ?? {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json; try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  return { status: res.status, body: json, headers: res.headers };
}
const login = async (username, password) => (await api('POST', '/api/auth/login', { body: { username, password } })).body.accessToken;

const admin = await login('admin', ADMIN_PASSWORD);
const supervisor = await login('supervisor', DEMO_PASSWORD);
const staff = await login('staff', DEMO_PASSWORD);
const clerk = await login('clerk', DEMO_PASSWORD);
const owner = await login('owner', DEMO_PASSWORD);

console.log('Setup');
const suffix = Date.now().toString().slice(-6);
const whs = (await api('GET', '/api/warehouses', { token: admin })).body;
const wh = whs[0];
const uom = (await api('GET', '/api/units', { token: admin })).body.find(u => u.code === 'PCS');
const mkProduct = async (n) => (await api('POST', '/api/products', { token: admin, body: { sku: `T${suffix}-${n}`, name: `Smoke ${n}`, baseUomId: uom.id, safetyStock: 5, isActive: true, barcodes: [`99${suffix}${n}`] } })).body;
const p1 = await mkProduct(1), p2 = await mkProduct(2);
const supplier = (await api('GET', '/api/partners?type=Supplier', { token: admin })).body.items[0];
const customer = (await api('GET', '/api/partners?type=Customer', { token: admin })).body.items[0];
check('products created', p1?.id && p2?.id);

const stockOf = async (pid) => {
  const r = (await api('GET', `/api/stock?productId=${pid}`, { token: admin })).body.items[0];
  return r ? { qty: Number(r.qtyOnHand), cost: Number(r.avgCost) } : { qty: 0, cost: 0 };
};
const doc = (partnerId, lines) => ({ warehouseId: wh.id, partnerId, purpose: 'Sale', lines }); // an issue needs a purpose; a receipt ignores it

console.log('Receive -> issue');
// The clerk enters the cost; the warehouse staff receive and post without seeing it (a receipt they enter takes the last known cost).
let r = await api('POST', '/api/documents/receipts', { token: clerk, body: doc(supplier.id, [{ productId: p1.id, qty: 1000, unitCost: 10 }, { productId: p2.id, qty: 1000, unitCost: 2 }]) });
check('clerk creates receipt (draft)', r.status === 200 && r.body.status === 'Draft', JSON.stringify(r.body));
const rcv = r.body;
check('document number has the RCV prefix', /^RCV-/.test(rcv.no), rcv.no);
check('draft does not change stock', (await stockOf(p1.id)).qty === 0);
r = await api('POST', `/api/documents/${rcv.id}/submit`, { token: clerk });
check('submit -> Submitted, no approval needed', r.body.status === 'Submitted' && r.body.requiresApproval === false);
r = await api('GET', `/api/documents/${rcv.id}`, { token: staff });
check('allowedActions offers the staff post', r.body.allowedActions?.includes('post'), JSON.stringify(r.body.allowedActions));
r = await api('POST', `/api/documents/${rcv.id}/post`, { token: staff, body: {} });
check('staff posts the receipt', r.body.status === 'Posted', JSON.stringify(r.body));
check('stock increased to 1000', (await stockOf(p1.id)).qty === 1000);
r = await api('PUT', `/api/documents/${rcv.id}`, { token: clerk, body: doc(supplier.id, [{ productId: p1.id, qty: 1 }]) });
check('posted document cannot be edited (400)', r.status === 400);

r = await api('POST', '/api/documents/issues', { token: staff, body: doc(customer.id, [{ productId: p1.id, qty: 50 }]) });
const iss = r.body;
await api('POST', `/api/documents/${iss.id}/submit`, { token: staff });
r = await api('POST', `/api/documents/${iss.id}/post`, { token: staff, body: {} });
check('issue posts, stock 950', r.body.status === 'Posted' && (await stockOf(p1.id)).qty === 950);
check('the staff who posted the issue see no cost', r.body.lines[0].unitCost == null && r.body.totalAmount == null);
check('issue booked at average cost 10', Number((await api('GET', `/api/documents/${iss.id}`, { token: admin })).body.lines[0].baseUnitCost) === 10);

console.log('Edit a draft (lines replaced, header updated)');
r = await api('POST', '/api/documents/receipts', { token: staff, body: doc(supplier.id, [{ productId: p1.id, qty: 5, unitCost: 1 }, { productId: p2.id, qty: 6, unitCost: 1 }]) });
const draft = r.body;
r = await api('PUT', `/api/documents/${draft.id}`, { token: staff, body: { ...doc(supplier.id, [{ productId: p2.id, qty: 9, unitCost: 3 }]), remark: 'edited' } });
check('draft edit replaces the lines', r.status === 200 && r.body.lines.length === 1 && Number(r.body.lines[0].qty) === 9 && r.body.remark === 'edited', JSON.stringify(r.body));
check('draft keeps its number after the edit', r.body.no === draft.no);
r = await api('POST', `/api/documents/${draft.id}/cancel`, { token: staff, body: { reason: 'smoke' } });
check('draft can be cancelled', r.body.status === 'Cancelled');
r = await api('PUT', `/api/documents/${draft.id}`, { token: staff, body: doc(supplier.id, [{ productId: p1.id, qty: 1 }]) });
check('cancelled document cannot be edited (400)', r.status === 400);
r = await api('POST', '/api/documents/receipts', { token: staff, body: doc(customer.id, [{ productId: p1.id, qty: 1 }]) });
check('receipt with a customer is rejected (400)', r.status === 400, JSON.stringify(r.body));

console.log('Insufficient stock');
r = await api('POST', '/api/documents/issues', { token: staff, body: doc(customer.id, [{ productId: p2.id, qty: 5000 }]) });
r = await api('POST', `/api/documents/${r.body.id}/submit`, { token: staff });
check('over-issue is blocked at submit (409)', r.status === 409 && r.body.title === 'Insufficient stock', JSON.stringify(r.body));

console.log('Approval rule (issue value >= 5000) and segregation of duties');
r = await api('POST', '/api/documents/issues', { token: staff, body: doc(customer.id, [{ productId: p1.id, qty: 600 }]) });
const big = r.body;
r = await api('POST', `/api/documents/${big.id}/submit`, { token: staff });
check('large issue requires approval', r.body.requiresApproval === true);
check('staff is not offered post/approve', !r.body.allowedActions.includes('post') && !r.body.allowedActions.includes('approve'));
r = await api('POST', `/api/documents/${big.id}/post`, { token: staff, body: {} });
check('staff cannot post it (400 needs approval or 403)', r.status === 400 || r.status === 403, String(r.status));
r = await api('POST', `/api/documents/${big.id}/approve`, { token: staff });
check('staff cannot approve (403)', r.status === 403);
r = await api('POST', `/api/documents/${big.id}/approve`, { token: supervisor });
check('supervisor approves', r.body.status === 'Approved', JSON.stringify(r.body));
r = await api('POST', `/api/documents/${big.id}/post`, { token: supervisor, body: {} });
check('supervisor posts approved issue', r.body.status === 'Posted' && (await stockOf(p1.id)).qty === 350);

console.log('Negative-stock override: the approver gives the reason, posting takes no body');
const settings0 = (await api('GET', '/api/settings', { token: admin })).body;
await api('PUT', '/api/settings', { token: admin, body: { ...settings0, allowNegativeStock: true } });
try {
  const p3 = await mkProduct(3);
  r = await api('POST', '/api/documents/issues', { token: staff, body: doc(customer.id, [{ productId: p3.id, qty: 3 }]) });
  const ovr = r.body;
  r = await api('POST', `/api/documents/${ovr.id}/submit`, { token: staff });
  check('short issue is submitted and flagged for override and approval', r.body.status === 'Submitted' && r.body.requiresOverride === true && r.body.requiresApproval === true, JSON.stringify(r.body));
  r = await api('POST', `/api/documents/${ovr.id}/approve`, { token: supervisor });
  check('approving an override without a reason is refused (400)', r.status === 400 && /reason/i.test(r.body.detail), JSON.stringify(r.body));
  r = await api('POST', `/api/documents/${ovr.id}/approve`, { token: supervisor, body: { negativeStockOverrideReason: 'smoke override' } });
  check('supervisor approves with the reason, which is stored', r.body.status === 'Approved' && r.body.negativeStockOverrideReason === 'smoke override', JSON.stringify(r.body));
  r = await api('POST', `/api/documents/${ovr.id}/post`, { token: staff });
  check('staff cannot post an approved override (403)', r.status === 403, String(r.status));
  r = await api('POST', `/api/documents/${ovr.id}/post`, { token: supervisor });
  check('supervisor posts it without a body, stock goes to -3', r.body.status === 'Posted' && r.body.negativeStockOverrideReason === 'smoke override' && (await stockOf(p3.id)).qty === -3, JSON.stringify(r.body));
} finally {
  await api('PUT', '/api/settings', { token: admin, body: settings0 });
}

console.log('Self-approval blocked');
r = await api('POST', '/api/documents/issues', { token: supervisor, body: doc(customer.id, [{ productId: p1.id, qty: 100 }]) });
const mine = r.body;
r = await api('POST', `/api/documents/${mine.id}/submit`, { token: supervisor });
// 100 x 10 = 1000 < 5000 so no approval; force one with a qty rule instead
const rule = (await api('POST', '/api/approval-rules', { token: admin, body: { docType: 'Issue', qtyThreshold: 100, roleRequired: 'Supervisor', isActive: true } })).body;
r = await api('POST', '/api/documents/issues', { token: supervisor, body: doc(customer.id, [{ productId: p1.id, qty: 150 }]) });
const mine2 = r.body;
await api('POST', `/api/documents/${mine2.id}/submit`, { token: supervisor });
r = await api('POST', `/api/documents/${mine2.id}/approve`, { token: supervisor });
check('submitter cannot approve own document', r.status === 400 && /submitted/i.test(r.body.detail), JSON.stringify(r.body));
r = await api('POST', `/api/documents/${mine2.id}/reject`, { token: admin, body: { reason: 'smoke reject' } });
check('reject returns to Draft and keeps the reason', r.body.status === 'Draft' && r.body.rejectionReason === 'smoke reject', JSON.stringify(r.body));
await api('PUT', `/api/approval-rules/${rule.id}`, { token: admin, body: { docType: 'Issue', qtyThreshold: 100, roleRequired: 'Supervisor', isActive: false } });

console.log('Reversal');
const before = await stockOf(p1.id);
r = await api('POST', `/api/documents/${iss.id}/reverse`, { token: supervisor, body: { reason: 'smoke reversal' } });
check('supervisor reverses the 50-unit issue', r.body.status === 'Reversed' && (await stockOf(p1.id)).qty === before.qty + 50, JSON.stringify(r.body));
r = await api('POST', `/api/documents/${iss.id}/reverse`, { token: staff, body: { reason: 'x' } });
check('staff cannot reverse (403)', r.status === 403);
const ledger = (await api('GET', `/api/stock/ledger?productId=${p1.id}&pageSize=100`, { token: admin })).body.items;
check('ledger keeps both the issue and its opposite entry', ledger.filter(l => l.sourceDocNo === iss.no).length === 2);
const sum = ledger.reduce((a, l) => a + Number(l.qtyDelta), 0);
check('balance equals the sum of the ledger (BR-01)', sum === (await stockOf(p1.id)).qty, `${sum}`);

console.log('Permissions and scope');
check('clerk cannot read audit log (403)', (await api('GET', '/api/audit', { token: clerk })).status === 403);
check('owner can read audit log', (await api('GET', '/api/audit', { token: owner })).status === 200);
check('staff cannot manage users (403)', (await api('GET', '/api/users', { token: staff })).status === 403);
check('no token -> 401', (await api('GET', '/api/stock')).status === 401);
check('clerk cannot post (403)', (await api('POST', `/api/documents/${rcv.id}/post`, { token: clerk, body: {} })).status === 403);

console.log('Validation');
r = await api('POST', '/api/products', { token: admin, body: { sku: '', name: '', baseUomId: uom.id, safetyStock: -1, isActive: true } });
check('invalid product -> 400 with field errors', r.status === 400 && r.body.errors, JSON.stringify(r.body));
r = await api('POST', '/api/products', { token: admin, body: { sku: p1.sku, name: 'dup', baseUomId: uom.id, safetyStock: 0, isActive: true } });
check('duplicate SKU -> 409', r.status === 409);

console.log('Concurrency: 24 parallel postings, half with lines in opposite order (deadlock pattern)');
const startA = await stockOf(p1.id), startB = await stockOf(p2.id);
const created = await Promise.all(Array.from({ length: 24 }, async (_, i) => {
  const lines = i % 2 === 0 ? [{ productId: p1.id, qty: 1 }, { productId: p2.id, qty: 1 }] : [{ productId: p2.id, qty: 1 }, { productId: p1.id, qty: 1 }];
  const d = (await api('POST', '/api/documents/issues', { token: staff, body: doc(customer.id, lines) })).body;
  await api('POST', `/api/documents/${d.id}/submit`, { token: staff });
  return d.id;
}));
const results = await Promise.all(created.map(id => api('POST', `/api/documents/${id}/post`, { token: staff, body: {} })));
const statuses = results.map(x => x.status);
check('every concurrent post succeeded (no 5xx, no deadlock victims)', statuses.every(s => s === 200), JSON.stringify(statuses.filter(s => s !== 200)));
const endA = await stockOf(p1.id), endB = await stockOf(p2.id);
check('stock reduced by exactly 24 on both products (no lost update)', endA.qty === startA.qty - 24 && endB.qty === startB.qty - 24, `${startA.qty}->${endA.qty}, ${startB.qty}->${endB.qty}`);

console.log('Concurrency: 12 parallel creates get unique, gap-free numbers');
const nums = await Promise.all(Array.from({ length: 12 }, async () => (await api('POST', '/api/documents/receipts', { token: staff, body: doc(supplier.id, [{ productId: p1.id, qty: 1, unitCost: 1 }]) })).body.no));
check('12 distinct numbers', new Set(nums).size === 12, nums.join(','));

console.log('Attachments: files are stored on disk, checked by content, scoped by owner');
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
const upload = async (token, ownerType, ownerId, bytes, name) => {
  const f = new FormData(); f.append('ownerType', ownerType); f.append('ownerId', String(ownerId)); f.append('file', new Blob([bytes]), name);
  const res = await fetch(BASE + '/api/attachments', { method: 'POST', headers: token ? { Authorization: 'Bearer ' + token } : {}, body: f });
  const text = await res.text(); let body = null; try { body = text ? JSON.parse(text) : null; } catch { body = text; }
  return { status: res.status, body };
};
r = await upload(staff, 'Document', rcv.id, png, '../../evil name.png');
check('png uploads to a posted receipt and the name is sanitised', r.status === 200 && r.body.contentType === 'image/png' && r.body.fileName.endsWith('.png') && !/[\\/]|\.\./.test(r.body.fileName), JSON.stringify(r.body));
const att = r.body;
r = await api('GET', '/api/attachments?ownerType=Document&ownerId=' + rcv.id, { token: staff });
check('attachment list shows the file with its uploader', r.status === 200 && r.body.some(a => a.id === att.id && a.uploadedBy === 'staff'), JSON.stringify(r.body));
const dl = await fetch(BASE + '/api/attachments/' + att.id, { headers: { Authorization: 'Bearer ' + staff } });
const got = Buffer.from(await dl.arrayBuffer());
check('download returns the exact bytes', dl.status === 200 && got.equals(png));
check('download is forced to be inert (nosniff, CSP, attachment)', dl.headers.get('x-content-type-options') === 'nosniff' && /default-src 'none'/.test(dl.headers.get('content-security-policy') ?? '') && /attachment/.test(dl.headers.get('content-disposition') ?? ''));
r = await upload(staff, 'Document', rcv.id, Buffer.from('<script>alert(1)</script>'), 'x.png');
check('a script renamed .png is refused (type is decided by content)', r.status === 400, JSON.stringify(r.body));
r = await upload(staff, 'Document', rcv.id, Buffer.concat([png, Buffer.alloc(11 * 1024 * 1024, 1)]), 'big.png');
check('an 11 MB file is refused', r.status === 400 || r.status === 413, String(r.status));
r = await upload(null, 'Document', rcv.id, png, 'a.png');
check('upload without a token is 401', r.status === 401);
r = await upload(clerk, 'Product', p1.id, png, 'p.png');
check('clerk cannot attach files to a product (403)', r.status === 403, String(r.status));
r = await upload(admin, 'Product', p1.id, png, 'p.png');
check('admin attaches a product image', r.status === 200 && r.body.ownerType === 'Product', JSON.stringify(r.body));
r = await api('DELETE', '/api/attachments/' + att.id, { token: staff });
check('a file on a posted document cannot be removed (409)', r.status === 409, String(r.status));
r = await api('POST', '/api/documents/receipts', { token: staff, body: doc(supplier.id, [{ productId: p1.id, qty: 1, unitCost: 1 }]) });
const draftForFile = r.body;
const up2 = await upload(staff, 'Document', draftForFile.id, png, 'draft.png');
r = await api('DELETE', '/api/attachments/' + up2.body.id, { token: staff });
check('a file on a Draft can be removed (204)', r.status === 204, String(r.status));
r = await fetch(BASE + '/api/attachments/' + up2.body.id, { headers: { Authorization: 'Bearer ' + staff } });
check('a removed file is gone (404)', r.status === 404);

console.log('Paging contracts');
r = await api('GET', '/api/stock/ledger?productId=' + p1.id + '&pageSize=2', { token: admin });
check('ledger page has items, total and a cursor', r.body.items.length === 2 && r.body.total >= 2 && typeof r.body.nextCursor === 'string' && r.body.nextCursor.length > 0, JSON.stringify(r.body).slice(0, 200));
const page1 = r.body;
r = await api('GET', '/api/stock/ledger?productId=' + p1.id + '&pageSize=2&cursor=' + encodeURIComponent(page1.nextCursor) + '&includeTotal=false', { token: admin });
check('seek page continues after the cursor without a count', r.body.items.every(i => i.id < page1.items[page1.items.length - 1].id) && r.body.total === -1 && !page1.items.some(i => r.body.items.some(j => j.id === i.id)), JSON.stringify(r.body).slice(0, 200));
r = await api('GET', '/api/stock/ledger?pageSize=2&cursor=garbage', { token: admin });
check('a cursor the server did not make is a 400, not a 500', r.status === 400 && Array.isArray(r.body.errors?.cursor), JSON.stringify(r.body).slice(0, 200));
for (const path of ['/api/users?pageSize=2', '/api/partners?pageSize=1', '/api/stock/low-stock?pageSize=1', '/api/reports/movements?pageSize=1', '/api/documents?pageSize=1', '/api/audit?pageSize=1']) {
  r = await api('GET', path, { token: admin });
  check(path + ' is server-side paged', r.status === 200 && Array.isArray(r.body.items) && r.body.items.length <= 2 && typeof r.body.total === 'number' && r.body.pageSize >= 1, String(r.status));
}
r = await api('GET', '/api/products?pageSize=999999', { token: admin });
check('an absurd page size is clamped', r.status === 200 && r.body.pageSize <= 200, String(r.status));
r = await api('GET', '/api/products?pageSize=200&page=2147483647', { token: admin });
check('a very deep OFFSET page is refused with a clear 400 (use a cursor), not a slow query or a 500', r.status === 400 && /cursor|too deep/i.test(r.body.detail ?? ''), JSON.stringify(r.body));
r = await api('GET', '/api/products?sortBy=Name&desc=true&pageSize=5', { token: admin });
check('products sort by name descending', r.status === 200 && r.body.items.every((x, i, a) => i === 0 || a[i - 1].name.localeCompare(x.name) >= 0), String(r.status));
r = await api('GET', "/api/products?search=' OR 1=1 --&pageSize=5", { token: admin });
check('SQL-injection text in a filter is just text (200, no rows or a few)', r.status === 200 && r.body.items.length === 0, JSON.stringify(r.body).slice(0, 120));
console.log('API contract (enums, timestamps, flags, lookup, tokens, counts)');
r = await api('GET', `/api/products/lookup?code=${encodeURIComponent('no-such-code-' + suffix)}`, { token: staff });
check('an unknown code is 200 with found=false, not a 404', r.status === 200 && r.body.found === false && r.body.product === null, JSON.stringify(r.body));
r = await api('GET', `/api/products/lookup?code=${p1.sku}`, { token: staff });
check('a known code is found with its product', r.status === 200 && r.body.found === true && r.body.product.id === p1.id);
r = await api('POST', '/api/documents/receipts', { token: staff, body: doc(supplier.id, [{ productId: p1.id, qty: 2, unitCost: 1 }]) });
const tokenDoc = r.body;
check('instants are UTC with a Z', /^\d{4}-\d\d-\d\dT[\d:.]+Z$/.test(tokenDoc.createdAt), tokenDoc.createdAt);
check('the document carries a concurrency token equal to its ETag', typeof tokenDoc.concurrencyToken === 'string' && r.headers.get('etag') === `"${tokenDoc.concurrencyToken}"`, String(r.headers.get('etag')));
check('creator is id, user name and display name', tokenDoc.createdById > 0 && tokenDoc.createdBy && tokenDoc.createdByName);
check('lines show the stock on hand', typeof tokenDoc.lines[0].available === 'number', JSON.stringify(tokenDoc.lines[0]));
r = await api('PUT', `/api/documents/${tokenDoc.id}`, { token: staff, body: doc(supplier.id, [{ productId: p1.id, qty: 3, unitCost: 1 }]), headers: { 'If-Match': `"${tokenDoc.concurrencyToken}"` } });
check('a save with the current token works and moves the token', r.status === 200 && r.body.concurrencyToken !== tokenDoc.concurrencyToken, String(r.status));
r = await api('PUT', `/api/documents/${tokenDoc.id}`, { token: staff, body: doc(supplier.id, [{ productId: p1.id, qty: 4, unitCost: 1 }]), headers: { 'If-Match': `"${tokenDoc.concurrencyToken}"` } });
check('a save with a stale token is refused (409) before anything changes', r.status === 409 && /changed by someone else/.test(r.body.detail ?? ''), JSON.stringify(r.body));
r = await api('GET', `/api/documents?mine=true&search=${tokenDoc.no}`, { token: staff });
check('mine=true lists my document with creator and flags', r.body.items.length === 1 && r.body.items[0].createdById === tokenDoc.createdById && r.body.items[0].requiresApproval === false && typeof r.body.items[0].concurrencyToken === 'string', JSON.stringify(r.body.items[0]));
r = await api('GET', '/api/documents?status=Submitted&requiresApproval=true&pageSize=5', { token: supervisor });
check('requiresApproval=true is accepted (approvals inbox)', r.status === 200 && r.body.items.every(i => i.requiresApproval === true), String(r.status));
r = await api('GET', '/api/documents/counts', { token: staff });
check('counts: returnedToMe, myDrafts, postedToday, approvalsWaiting', r.status === 200 && ['returnedToMe', 'myDrafts', 'postedToday', 'approvalsWaiting'].every(k => typeof r.body[k] === 'number'), JSON.stringify(r.body));
r = await api('GET', `/api/stock?productId=${p1.id}`, { token: admin });
check('stock rows carry the warehouse name', r.body.items[0].warehouseName && r.body.items[0].warehouseCode);
r = await fetch(`${BASE}/openapi/v1.json`);
const spec = await r.json().catch(() => null);
check('OpenAPI: enums are string schemas with values', spec?.components?.schemas?.DocumentStatus?.type === 'string' && spec.components.schemas.DocumentStatus.enum.includes('Posted') && !spec.components.schemas.IssuePurpose.enum.includes(null));
check('OpenAPI: operation ids are Tag_action', spec?.paths?.['/api/documents']?.get?.operationId === 'Documents_list');

console.log('Cost visibility: staff see no cost or value, the clerk sees cost but not value, the supervisor sees both');
{
  const get = async (token, path) => (await api('GET', path, { token })).body;
  check('stock total value: hidden from staff and clerk, shown to the supervisor',
    (await get(staff, '/api/stock/summary')).totalValue == null && (await get(clerk, '/api/stock/summary')).totalValue == null
    && typeof (await get(supervisor, '/api/stock/summary')).totalValue === 'number');
  const row = async (token) => (await get(token, '/api/stock?pageSize=1')).items[0];
  check('stock row: no average cost or value for staff, both for the supervisor', (await row(staff)).avgCost == null && (await row(staff)).value == null && typeof (await row(supervisor)).avgCost === 'number');
  check('staff: sorting stock by value is refused (400)', (await api('GET', '/api/stock?sortBy=Value', { token: staff })).status === 400);
  check('approval rule amounts: hidden from staff, shown to admin', (await get(staff, '/api/approval-rules')).every((r) => r.amountThreshold == null) && (await get(admin, '/api/approval-rules')).some((r) => typeof r.amountThreshold === 'number'));
  check('staff: audit log is closed', (await api('GET', '/api/audit', { token: staff })).status === 403);
  const can = async (token, permission) => (await get(token, '/api/auth/me')).permissions.includes(permission);
  check('permissions: staff none, clerk cost only, supervisor both',
    !(await can(staff, 'cost.view')) && !(await can(staff, 'valuation.view')) && (await can(clerk, 'cost.view')) && !(await can(clerk, 'valuation.view'))
    && (await can(supervisor, 'cost.view')) && (await can(supervisor, 'valuation.view')));
}
console.log('Reports');
const x = await fetch(`${BASE}/api/reports/stock-summary/export`, { headers: { Authorization: `Bearer ${admin}` } });
check('stock summary exports xlsx', x.status === 200 && (x.headers.get('content-type') ?? '').includes('spreadsheetml'));
check('low stock endpoint answers', (await api('GET', '/api/stock/low-stock', { token: admin })).status === 200);

console.log(`\n${passed} passed, ${failures.length} failed`);
if (failures.length) { console.log('Failed:\n - ' + failures.join('\n - ')); process.exit(1); }
