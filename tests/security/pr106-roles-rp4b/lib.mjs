import fs from 'node:fs';
export const BASE = 'http://127.0.0.1:5316';
const cfg = JSON.parse(fs.readFileSync('D:/ClaudeTemp/rv106/pub/appsettings.Development.json', 'utf8'));
export const PW = { admin: cfg.Seed.AdminPassword, other: cfg.Seed.DemoPassword };

export async function call(method, path, token, body, headers = {}) {
  const h = { ...headers };
  if (token) h.Authorization = 'Bearer ' + token;
  let payload;
  if (body !== undefined) { h['Content-Type'] = 'application/json'; payload = typeof body === 'string' ? body : JSON.stringify(body); }
  const r = await fetch(BASE + path, { method, headers: h, body: payload });
  const text = await r.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: r.status, json, headers: r.headers, cookie: (r.headers.getSetCookie?.() ?? []).map(c => c.split(";")[0]).join("; ") };
}
export async function login(user) {
  const pw = user === 'admin' ? PW.admin : PW.other;
  const r = await call('POST', '/api/auth/login', null, { username: user, password: pw });
  if (r.status !== 200) throw new Error('login ' + user + ' ' + r.status + ' ' + JSON.stringify(r.json));
  return { token: r.json.accessToken, info: r.json.user ?? r.json, raw: r.json };
}
export const short = (r) => `${r.status}${r.status >= 400 ? ' ' + (r.json?.detail ?? r.json?.title ?? JSON.stringify(r.json)).toString().slice(0, 160) : ''}`;
export function decode(token) {
  return JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8'));
}
export const T = JSON.parse(fs.readFileSync('D:/ClaudeTemp/rv106/probe/tokens.json', 'utf8'));
