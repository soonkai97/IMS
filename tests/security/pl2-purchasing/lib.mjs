// Shared helpers for the PL2.11 purchasing probes. Everything comes from the environment, nothing secret is stored:
//   RV_BASE       API address (default http://127.0.0.1:5960)
//   RV_ADMIN_PW   password of the seeded 'admin' user, RV_DEMO_PW the one of owner/supervisor/clerk/staff (read them from the dev settings file at run time)
//   RV_JWT_KEY    the Jwt:Key the throw-away API was started with (only the forged-token probes need it)
//   RV_OUT        scratch folder OUTSIDE the repository (fixtures.json, results.jsonl, captured logs)
import crypto from 'node:crypto';
import fs from 'node:fs';
export const BASE = process.env.RV_BASE ?? 'http://127.0.0.1:5960';
export const OUT = process.env.RV_OUT ?? '.';
export const PROBE_PW = 'Probe#Pass12345'; // made-up; used only for the SEC- users these scripts create
export const sfx = Date.now().toString(36).slice(-4);

export async function call(method, path, token, body, headers = {}) {
  const h = { ...headers };
  if (token) h.Authorization = 'Bearer ' + token;
  let payload;
  if (body !== undefined) { h['Content-Type'] ??= 'application/json'; payload = typeof body === 'string' ? body : JSON.stringify(body); }
  const t0 = performance.now();
  const r = await fetch(BASE + path, { method, headers: h, body: payload });
  const text = await r.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: r.status, json, headers: r.headers, text, ms: performance.now() - t0 };
}
export async function login(user, pw) {
  const password = pw ?? (user === 'admin' ? process.env.RV_ADMIN_PW : process.env.RV_DEMO_PW);
  const r = await call('POST', '/api/auth/login', null, { username: user, password });
  if (r.status !== 200) throw new Error(`login ${user} ${r.status} ${r.text.slice(0, 160)}`);
  return r.json.accessToken;
}
export const decode = (t) => JSON.parse(Buffer.from(t.split('.')[1], 'base64url').toString('utf8'));
export const etag = (r) => r.headers.get('etag');
export const short = (r) => `${r.status}${r.status >= 400 ? ' ' + String(r.json?.detail ?? r.json?.title ?? r.text).slice(0, 160) : ''}`;
export const fx = () => JSON.parse(fs.readFileSync(`${OUT}/fixtures.json`, 'utf8'));
export const saveFx = (o) => fs.writeFileSync(`${OUT}/fixtures.json`, JSON.stringify(o));

/** A token signed with RV_JWT_KEY (HS256) from the claims of a real one, so the probe can change what it likes: roles without perms, expiry, audience. */
export function forge(claims, { key = process.env.RV_JWT_KEY, alg = 'HS256', exp = 300, aud, iss } = {}) {
  const now = Math.floor(Date.now() / 1000);
  const b = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const payload = { ...claims, nbf: now - 3600, exp: now + exp }; payload.aud = aud ?? 'ims-clients'; payload.iss = iss ?? 'ims';
  const head = b({ alg: alg === 'none' ? 'none' : 'HS256', typ: 'JWT' });
  const body = `${head}.${b(payload)}`;
  if (alg === 'none') return body + '.';
  return body + '.' + crypto.createHmac('sha256', key).update(body).digest('base64url');
}

let fails = 0;
const log = (o) => fs.appendFileSync(`${OUT}/results.jsonl`, JSON.stringify(o) + '\n');
/** Records one check: area (1-9), label, pass flag and the evidence line. */
export function check(area, label, ok, evidence = '') {
  if (!ok) fails++;
  console.log(`${ok ? 'ok  ' : 'FAIL'} [${area}] ${label}${evidence ? ' -> ' + evidence : ''}`);
  log({ area, label, ok, evidence });
  return ok;
}
export function expect(area, label, r, ...codes) {
  return check(area, label, codes.includes(r.status), `${short(r)}${codes.includes(r.status) ? '' : `  (expected ${codes.join('/')})`}`), r;
}
export const failures = () => fails;

/** Scans a JSON value for any property named like money. Returns the paths whose value is not null. */
export function moneyPaths(v, p = '$', out = []) {
  const MONEY = /^(unitPrice|amount|totalAmount|unitCost|suggestedValue|price|cost|value|enteredUnitCost|threshold|amountThreshold|grandTotal)$/i;
  if (Array.isArray(v)) v.forEach((x, i) => moneyPaths(x, `${p}[${i}]`, out));
  else if (v && typeof v === 'object') for (const [k, x] of Object.entries(v)) {
    if (MONEY.test(k) && x !== null && x !== undefined && typeof x !== 'object') out.push(`${p}.${k}=${x}`);
    else moneyPaths(x, `${p}.${k}`, out);
  }
  return out;
}
export const items = (r) => (Array.isArray(r.json) ? r.json : r.json?.items ?? []);

/** Access tokens (5 minutes) of every fixture caller, cached in RV_OUT for 3 minutes. Login allows 20 per minute per address, so a refused login waits a minute and retries. */
export async function tokens(force = false) {
  const f = `${OUT}/tokens.json`;
  if (!force && fs.existsSync(f)) { const c = JSON.parse(fs.readFileSync(f, 'utf8')); if (Date.now() - c.at < 180000) return c.T; }
  const F = fx(); const T = {};
  const names = [['admin', 'admin', undefined], ...['owner', 'supervisor', 'clerk', 'staff'].map((n) => [n, n, undefined]), ...Object.entries(F.U).map(([n, u]) => [n, u.username, PROBE_PW])];
  for (const [key, user, pw] of names) {
    for (let i = 0; ; i++) {
      try { T[key] = await login(user, pw); break; } catch (e) { if (/ 401 /.test(String(e)) && pw === PROBE_PW) break; if (i > 1 || !/ 429 /.test(String(e))) throw e; await new Promise((r) => setTimeout(r, 62000)); }
    }
  }
  fs.writeFileSync(f, JSON.stringify({ at: Date.now(), T })); return T;
}

// ---- purchase-order helpers -------------------------------------------------------------------------------------------------------------------------------
export const PO = '/api/purchase-orders';
let adminTok; export const useAdmin = (t) => { adminTok = t; };
/** The current concurrency token of an order, read as the Administrator (every write needs it as If-Match). */
export const tag = async (id) => etag(await call('GET', `${PO}/${id}`, adminTok));
/** A transition with a fresh If-Match unless `ifMatch` is given ('' = send none). */
export async function act(tok, id, action, body, ifMatch) {
  const h = {}; const t = ifMatch ?? await tag(id); if (t) h['If-Match'] = t;
  return call('POST', `${PO}/${id}/${action}`, tok, body, h);
}
export const line = (F, o = {}) => ({ productId: F.P.a, qty: 10, uomId: null, unitPrice: 12.5, remark: null, ...o });
export const newPo = (tok, F, o = {}) => call('POST', PO, tok, { warehouseId: F.whA, supplierId: F.supplier, remark: 'SEC probe', lines: [line(F)], ...o });
/** Creates a Draft as `creator`, submits it as `creator`; returns the submit response (Approved at once under RM 5,000, else Submitted). */
export async function submitted(creator, F, o = {}) { const c = await newPo(creator, F, o); if (c.status !== 200) throw new Error('create ' + short(c)); const s = await act(creator, c.json.id, 'submit'); if (s.status !== 200) throw new Error('submit ' + short(s)); return s; }
/** An Approved order: small (price x qty < 5,000) submitted by `creator`; `big` = Standard tier (about 6,000), `large` = Large tier (about 25,000) approved by `approver`. */
export async function approvedPo(F, T, { creator = 'clerk', approver, tier = 'none', lines, ...o } = {}) {
  approver ??= tier === 'large' ? 'owner' : 'supervisor';
  const price = { none: 12.5, big: 600, large: 2500 }[tier];
  const s = await submitted(T[creator], F, { lines: lines ?? [line(F, { unitPrice: price })], ...o });
  if (s.json.status === 'Approved') return s.json;
  const a = await act(T[approver], s.json.id, 'approve'); if (a.status !== 200) throw new Error('approve ' + short(a)); return a.json;
}
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const docTag = async (id) => etag(await call('GET', `/api/documents/${id}`, adminTok));
export const docAct = async (tok, id, action, body) => call('POST', `/api/documents/${id}/${action}`, tok, body, { 'If-Match': await docTag(id) });
/** A draft receipt from an order (created by T[creator]); `qty` re-saves every line with that quantity. Returns the draft response, or the failed save. */
export async function receiptDraft(F, T, poId, { creator = 'staff', qty } = {}) {
  const d = await call('POST', `${PO}/${poId}/receipt`, T[creator]); if (d.status !== 200 || qty === undefined) return d;
  const doc = d.json;
  const res = await call('PUT', `/api/documents/${doc.id}`, T[creator], { warehouseId: doc.warehouseId, partnerId: doc.partnerId, remark: null,
    lines: doc.lines.map((l) => ({ productId: l.productId, qty, uomId: l.uomId, unitCost: null, remark: null, purchaseOrderLineNo: l.purchaseOrderLineNo })) }, { 'If-Match': etag(d) });
  if (res.status !== 200) await docAct(T[creator], doc.id, 'cancel', { reason: 'SEC refused save, draft dropped' }); // leave no waiting draft behind
  return res;
}
/** Submits a draft receipt as its creator, approves it as the Supervisor when a rule routed it, and posts it as the poster. Returns the post response. */
export async function postReceipt(T, doc, { creator = 'staff', poster = 'supervisor' } = {}) {
  const s = await docAct(T[creator], doc.id, 'submit'); if (s.status !== 200) return s;
  if (s.json.status === 'Submitted' && s.json.requiresApproval) { const a = await docAct(T.supervisor, doc.id, 'approve', {}); if (a.status !== 200) return a; }
  return docAct(T[poster], doc.id, 'post');
}
export const reverseDoc = (tok, id) => docAct(tok, id, 'reverse', { reason: 'SEC retest cleanup' });

/** A custom role with exactly these permissions plus a user holding it (warehouse list or all); both are remembered in fixtures.json so z-cleanup can switch them off. Returns the user's token. */
export async function roleUser(name, permissions, { warehouses = [], all = true } = {}) {
  const F = fx(); const A = adminTok; const known = (F.extra ?? []).find((x) => x.name === name); if (known) return login(known.username, PROBE_PW);
  const role = await call('POST', '/api/roles', A, { name: `SEC-${F.sfx}-${name}`, permissions });
  if (role.status !== 200) throw new Error(`role ${name} ${short(role)}`);
  const username = `sec${F.sfx}${name}`.toLowerCase().replace(/[^a-z0-9]/g, '');
  const u = await call('POST', '/api/users', A, { username, email: `${username}@ims.local`, displayName: `SEC ${name}`, role: role.json.code, isActive: true, password: PROBE_PW, warehouseIds: all ? [] : warehouses, allWarehouses: all });
  if (u.status !== 200) throw new Error(`user ${name} ${short(u)}`);
  const F2 = fx(); (F2.extra ??= []).push({ name, roleId: role.json.id, roleName: role.json.name, userId: u.json.id, username }); saveFx(F2);
  return login(username, PROBE_PW);
}
