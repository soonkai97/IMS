// Enumeration / counting resistance: crawls every readable GET as several roles and reports (a) JSON properties that look like ids and hold integers,
// (b) integers that look like database counters (any "id"-ish name, any number that grows with the table), (c) ETag / Location / other headers that
// carry a counter, (d) the base64 concurrencyToken (a SQL rowversion = a database-wide counter), (e) the cursor shape.
// usage: IMS_STATE=state.json node enum.mjs
import { raw, login, loadState, GUID } from './lib.mjs';

const st = loadState(); const I = st.ids, D = st.docs;
const roles = { admin: ['admin'], owner: ['owner'], supervisor: ['supervisor'], staff: ['staff'], clerk: ['clerk'], staff2: ['staff2', 'created'] };
const T = {}; for (const [k, [u, kind]] of Object.entries(roles)) T[k] = await login(u, kind ?? 'demo');

const paths = [
  '/api/auth/me', '/api/warehouses', '/api/units', '/api/categories', '/api/partners', '/api/products', `/api/products/${I.p1}`, '/api/products/lookup?code=DEMO-001',
  '/api/users', '/api/approval-rules', '/api/settings', '/api/stock', '/api/stock/ledger', '/api/stock/low-stock', '/api/stock/summary', '/api/documents', '/api/documents/counts',
  `/api/documents/${D.wh1Posted.id}`, `/api/documents/${D.wh2Posted.id}`, `/api/attachments?ownerType=Document&ownerId=${D.wh1Draft.id}`, '/api/reports/movements', '/api/audit', '/api/audit/filters',
];
const idLike = /(^|[a-z])id$|^id$|ids$|key$|version$|token$|seq|counter/i;
const hits = new Map();
const note = (role, path, jsonPath, value) => { const k = `${jsonPath}`; const h = hits.get(k) ?? { roles: new Set(), paths: new Set(), sample: value }; h.roles.add(role); h.paths.add(path.split('?')[0].replace(/[0-9a-f-]{36}/g, '{id}')); hits.set(k, h); };
const walk = (role, path, v, p) => {
  if (Array.isArray(v)) { v.slice(0, 3).forEach((x) => walk(role, path, x, p + '[]')); return; }
  if (v && typeof v === 'object') { for (const [k, x] of Object.entries(v)) walk(role, path, x, p ? `${p}.${k}` : k); return; }
  const name = p.split('.').pop().replace('[]', '');
  if (typeof v === 'number' && Number.isInteger(v) && idLike.test(name)) note(role, path, p, v);
  if (typeof v === 'string' && /(^|[a-z])id$|^id$/i.test(name) && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v)) note(role, path, p + ' (non-guid string)', v);
  if (typeof v === 'string' && /^\d{1,19}$/.test(v) && /id$/i.test(name)) note(role, path, p + ' (numeric string)', v);
};
const hdrHits = new Map();
const statuses = {};
for (const [role, tok] of Object.entries(T)) for (const path of paths) {
  const r = await raw('GET', path, { token: tok });
  statuses[`${role} ${path}`] = r.status;
  if (r.data) walk(role, path, r.data, '');
  for (const h of ['etag', 'location', 'content-location', 'link', 'x-total-count', 'x-request-id']) if (r.headers[h]) { const k = `${h}`; const e = hdrHits.get(k) ?? { n: 0, sample: r.headers[h], paths: new Set() }; e.n++; e.paths.add(path.split('?')[0].replace(/[0-9a-f-]{36}/g, '{id}')); hdrHits.set(k, e); }
}
console.log('== integer / non-GUID id-like JSON properties (property: roles, endpoints, sample)');
for (const [k, h] of [...hits].sort()) console.log(`  ${k}: roles=${[...h.roles].join(',')} paths=${[...h.paths].join(',')} sample=${String(h.sample).slice(0, 40)}`);
console.log('== counter-ish headers');
for (const [k, h] of hdrHits) console.log(`  ${k}: n=${h.n} on ${[...h.paths].join(',')} sample=${h.sample}`);

// concurrencyToken is a rowversion: decode it as a big-endian 64-bit number
const dec = (b64) => Buffer.from(b64, 'base64').readBigUInt64BE(0);
const t1 = (await raw('GET', `/api/documents/${D.wh1Posted.id}`, { token: T.staff })).data.concurrencyToken;
console.log('== concurrencyToken seen by scoped staff in WH01:', t1, '-> as 64-bit counter', dec(t1).toString());
