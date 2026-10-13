// Shared helpers for the public-id retest scripts. Local use only: the target must be a throw-away API on localhost.
// Credentials are read at run time from the dev settings file (never printed, never written to disk by these scripts).
import { readFileSync, writeFileSync, existsSync } from 'node:fs';

export const BASE = process.env.IMS_BASE ?? 'http://localhost:5710';
if (!/^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(BASE)) throw new Error('refusing to run against a non-local target');
const SETTINGS = process.env.IMS_DEV_SETTINGS ?? 'D:/Tutorial/IMS/src/Ims.Api/appsettings.Development.json';
export const STATE = process.env.IMS_STATE ?? 'state.json';

const dev = JSON.parse(readFileSync(SETTINGS, 'utf8'));
const PASSWORDS = { admin: dev.Seed.AdminPassword, demo: dev.Seed.DemoPassword };
export const secrets = () => Object.values(PASSWORDS);

export const loadState = () => (existsSync(STATE) ? JSON.parse(readFileSync(STATE, 'utf8')) : {});
export const saveState = (s) => writeFileSync(STATE, JSON.stringify(s, null, 1));

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export const GUID = () => crypto.randomUUID();
export const EMPTY = '00000000-0000-0000-0000-000000000000';

export async function raw(method, path, { token, body, headers = {}, form, json = true } = {}) {
  const h = { ...headers };
  if (token) h.Authorization = `Bearer ${token}`;
  let payload;
  if (form) payload = form;
  else if (body !== undefined) { payload = typeof body === 'string' ? body : JSON.stringify(body); h['Content-Type'] ??= 'application/json'; }
  const t0 = performance.now();
  const res = await fetch(BASE + path, { method, headers: h, body: payload, redirect: 'manual' });
  const buf = Buffer.from(await res.arrayBuffer());
  const ms = performance.now() - t0;
  const text = buf.toString('utf8');
  let data = null;
  if (json) { try { data = JSON.parse(text); } catch { /* not json */ } }
  const hdrs = {};
  res.headers.forEach((v, k) => { hdrs[k] = v; });
  return { status: res.status, headers: hdrs, text, data, buf, ms };
}

/** Sign in and return an access token. username is a demo account name or one created by setup. */
export async function login(username, kind = 'demo') {
  const password = username === 'admin' ? PASSWORDS.admin : (kind === 'created' ? loadState().createdPassword : PASSWORDS.demo);
  const r = await raw('POST', '/api/auth/login', { body: { username, password } });
  if (r.status !== 200) throw new Error(`login ${username} -> ${r.status}`);
  return r.data.accessToken;
}

/** Body with volatile values removed so that two "identical" 404s compare equal (traceId, instance, correlation id). */
export function normBody(r) {
  let t = r.text;
  try {
    const d = JSON.parse(t);
    for (const k of ['traceId', 'correlationId', 'instance']) delete d[k];
    t = JSON.stringify(d, Object.keys(d).sort());
  } catch { /* keep raw text */ }
  return t.replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '<guid>');
}
/** Headers that must not differ between "missing" and "forbidden" answers. */
export function normHeaders(r) {
  const drop = new Set(['date', 'x-correlation-id', 'content-length', 'transfer-encoding', 'server']);
  return Object.entries(r.headers).filter(([k]) => !drop.has(k)).sort(([a], [b]) => a.localeCompare(b)).map(([k, v]) => `${k}: ${v}`).join('\n');
}
export const sig = (r) => `${r.status}|${normBody(r)}|${normHeaders(r)}`;
