// Shared helpers for the PR 118 (RP-5) probes. Config comes from the environment so no secret is stored:
//   RV_BASE (default http://127.0.0.1:5318), RV_ADMIN_PW and RV_DEMO_PW (the seed passwords the throw-away API was started with).
export const BASE = process.env.RV_BASE ?? 'http://127.0.0.1:5318';
export const PW = { admin: process.env.RV_ADMIN_PW, other: process.env.RV_DEMO_PW };
export const PROBE_PW = 'Probe#Pass12345'; // made-up, used only for users these scripts create in the throw-away database

export async function call(method, path, token, body, headers = {}) {
  const h = { ...headers };
  if (token) h.Authorization = 'Bearer ' + token;
  let payload;
  if (body !== undefined) { h['Content-Type'] = 'application/json'; payload = typeof body === 'string' ? body : JSON.stringify(body); }
  const r = await fetch(BASE + path, { method, headers: h, body: payload });
  const text = await r.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: r.status, json, headers: r.headers, text };
}
export async function login(user, pw) {
  const password = pw ?? (user === 'admin' ? PW.admin : PW.other);
  const r = await call('POST', '/api/auth/login', null, { username: user, password });
  if (r.status !== 200) throw new Error(`login ${user} ${r.status} ${r.text.slice(0, 200)}`);
  return { token: r.json.accessToken, info: r.json.user ?? r.json, raw: r.json };
}
export const short = (r) => `${r.status}${r.status >= 400 ? ' ' + String(r.json?.detail ?? r.json?.title ?? r.text).slice(0, 220) : ''}`;
export const decode = (t) => JSON.parse(Buffer.from(t.split('.')[1], 'base64url').toString('utf8'));
export const etag = (r) => r.headers.get('etag');
export const ALL = ['users.read','users.manage','roles.read','roles.manage','settings.manage','audit.read','masterdata.read','masterdata.manage','partners.create','document.create','document.edit.any','document.approve','document.approve.large','document.post','document.post.approved','document.reverse','stock.negative.override','stock.read','reports.read','cost.view','valuation.view'];
export const sfx = Date.now().toString(36).slice(-4);

let fails = 0;
export function expect(label, r, ...codes) {
  const ok = codes.includes(r.status);
  if (!ok) fails++;
  console.log(`${ok ? "ok  " : "FAIL"} ${label} -> ${short(r)}${ok ? "" : `   (expected ${codes.join("/")})`}`);
  return r;
}
export const failures = () => fails;

// Login that also returns the refresh token taken from the Set-Cookie header (the body carries it only when ReturnRefreshTokenInBody is on).
export async function loginFull(user, pw) {
  const password = pw ?? (user === "admin" ? PW.admin : PW.other);
  const r = await fetch(BASE + "/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ username: user, password }) });
  const text = await r.text(); let json; try { json = JSON.parse(text); } catch { json = text; }
  const sc = (r.headers.getSetCookie?.() ?? [])[0] ?? "";
  const refresh = decodeURIComponent(sc.split(";")[0].split("=").slice(1).join("=")) || null;
  return { status: r.status, json, token: json?.accessToken, refresh };
}
export const refreshWith = (rt) => call("POST", "/api/auth/refresh", null, { refreshToken: rt });

// ---- PR 124 additions ----
import { execFileSync } from 'node:child_process';
export const DB = process.env.RV_DB ?? 'Ims_Rv124';
const INST = '(localdb)' + String.fromCharCode(92) + 'ImsDev';
export function sql(q) {
  return execFileSync('sqlcmd', ['-I', '-S', INST, '-d', DB, '-h', '-1', '-W', '-b', '-Q', 'SET NOCOUNT ON; ' + q], { encoding: 'utf8' }).trim();
}
// Administrators by the rule of the PR, computed straight from the tables (independent of the application code).
export const CORE = ['users.read', 'users.manage', 'roles.read', 'roles.manage'];
export const adminCount = () => Number(sql(`SELECT COUNT(*) FROM [User] u JOIN [Role] r ON r.Id=u.RoleId AND r.REC_DELETED=0 AND r.IsActive=1
  WHERE u.IsActive=1 AND u.REC_DELETED=0 AND (SELECT COUNT(*) FROM RolePermission p WHERE p.RoleId=r.Id AND p.REC_DELETED=0 AND p.PermissionCode IN ('users.read','users.manage','roles.read','roles.manage'))=4`));
export const adminNames = () => sql(`SELECT u.Username FROM [User] u JOIN [Role] r ON r.Id=u.RoleId AND r.REC_DELETED=0 AND r.IsActive=1
  WHERE u.IsActive=1 AND u.REC_DELETED=0 AND (SELECT COUNT(*) FROM RolePermission p WHERE p.RoleId=r.Id AND p.REC_DELETED=0 AND p.PermissionCode IN ('users.read','users.manage','roles.read','roles.manage'))=4`).split(/\r?\n/).filter(Boolean);
export const sfx2 = () => Math.random().toString(36).slice(2, 7);
