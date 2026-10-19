// Self-approval: a submitter who holds the approval permission of the rule's tier is refused for Standard and Large documents; others approve. Uses the seeded demo users.
import { call, login, expect, failures, PROBE_PW, short, sql } from './lib.mjs';
sql("UPDATE [User] SET IsActive=1, LockoutUntil=NULL WHERE Username='admin'");
const T = {}; for (const u of ['admin', 'owner', 'supervisor', 'clerk', 'staff']) T[u] = (await login(u)).token;
const sfx = Date.now().toString().slice(-6);
const wh = (await call('GET', '/api/warehouses', T.admin)).json[0];
const uom = (await call('GET', '/api/units', T.admin)).json.find(u => u.code === 'PCS');
const p = (await call('POST', '/api/products', T.admin, { sku: `S${sfx}`, name: `Self ${sfx}`, baseUomId: uom.id, safetyStock: 1, isActive: true, barcodes: [`88${sfx}`] })).json;
const supplier = (await call('GET', '/api/partners?type=Supplier', T.admin)).json.items[0];
const customer = (await call('GET', '/api/partners?type=Customer', T.admin)).json.items[0];
const act = async (id, tok, action, body) => { const g = await call('GET', '/api/documents/' + id, T.admin); return call('POST', '/api/documents/' + id + '/' + action, tok, body, { 'If-Match': g.headers.get('etag') }); };
const doc = (pid, lines, extra = {}) => ({ warehouseId: wh.id, partnerId: pid, purpose: 'Sale', lines, ...extra });
const rcv = (await call('POST', '/api/documents/receipts', T.admin, doc(supplier.id, [{ productId: p.id, qty: 5000, unitCost: 1 }]))).json;
await act(rcv.id, T.admin, 'submit'); await act(rcv.id, T.owner, 'approve'); await act(rcv.id, T.admin, 'post');
console.log('stock seeded', (await call('GET', `/api/stock?productId=${p.id}`, T.admin)).json.items?.[0]?.qtyOnHand);

async function rule(tier, qty) { return (await call('POST', '/api/approval-rules', T.admin, { docType: 'Issue', qtyThreshold: qty, approverTier: tier, isActive: true })).json; }
const off = (r, qty) => call('PUT', `/api/approval-rules/${r.id}`, T.admin, { docType: 'Issue', qtyThreshold: qty, approverTier: r.approverTier, isActive: false });
const mkRole = async (name, perms) => (await call('POST', '/api/roles', T.admin, { name, permissions: perms })).json;
const mkUser = async (n, role) => (await call('POST', '/api/users', T.admin, { username: n, email: `${n}@ims.local`, displayName: n, role, isActive: true, password: PROBE_PW, warehouseIds: [wh.id], allWarehouses: true })).json;
const rS = await mkRole('SelfStd' + sfx, ['document.create', 'document.approve', 'stock.read']);
const rL = await mkRole('SelfLrg' + sfx, ['document.create', 'document.approve.large', 'stock.read']);
const rB = await mkRole('SelfBoth' + sfx, ['document.create', 'document.approve', 'document.approve.large', 'stock.read']);
const names = { s: 'selfstd' + sfx, l: 'selflrg' + sfx, b: 'selfboth' + sfx };
await mkUser(names.s, rS.code); await mkUser(names.l, rL.code); await mkUser(names.b, rB.code);
for (const k of Object.keys(names)) T[k] = (await login(names[k], PROBE_PW)).token;

async function scenario(tier, qty, submitterKey) {
  const r = await rule(tier, qty);
  const d = (await call('POST', '/api/documents/issues', T[submitterKey], doc(customer.id, [{ productId: p.id, qty: qty + 1 }]))).json;
  const sub = (await act(d.id, T[submitterKey], 'submit')).json;
  console.log(`-- ${tier} rule, submitter ${submitterKey}: status ${sub.status} requiresApproval ${sub.requiresApproval} isLarge ${sub.isLarge}`);
  const self = await act(d.id, T[submitterKey], 'approve');
  expect(`${tier}: ${submitterKey} approves own document -> 400`, self, 400);
  const rej = await act(d.id, T[submitterKey], 'reject', { reason: 'self reject attempt' });
  expect(`${tier}: ${submitterKey} rejects own document -> 400/403`, rej, 400, 403);
  return { d, r };
}
// Standard tier
let x = await scenario('Standard', 300, 'supervisor');
expect('Standard: a different approver (admin) approves', await act(x.d.id, T.admin, 'approve'), 200);
await off(x.r, 300);
x = await scenario('Standard', 301, 's');   // custom role holding document.approve
expect('Standard: owner (large tier only) is refused by design', await act(x.d.id, T.owner, 'approve'), 403);
await off(x.r, 301);
// Large tier
x = await scenario('Large', 400, 'l');      // holds document.approve.large only
expect('Large: owner approves', await act(x.d.id, T.owner, 'approve'), 200);
await off(x.r, 400);
x = await scenario('Large', 401, 'b');      // holds both tiers
const std = await act(x.d.id, T.supervisor, 'approve');
expect('Large: a Standard-only approver (supervisor) is refused', std, 403, 400);
expect('Large: owner approves', await act(x.d.id, T.owner, 'approve'), 200);
await off(x.r, 401);
// The old client path: roleRequired only now creates no rule (400), so a document is not held for approval by that rule.
const old = await call('POST', '/api/approval-rules', T.admin, { docType: 'Issue', qtyThreshold: 50, roleRequired: 'Supervisor', isActive: true });
console.log('old client roleRequired-only rule create ->', short(old), '(smoke.mjs sent this before 1512a43: no rule, so the document did not need approval)');
console.log('failures', failures());
