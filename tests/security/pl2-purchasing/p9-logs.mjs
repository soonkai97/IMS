// Area 7 (logs): `node p9-logs.mjs mark` pushes known markers through the API (a failed login that carries a marker password, a partner with a TIN, a marker price); `node p9-logs.mjs scan <log files or folders...>`
// then searches everything the API wrote (stdout and the rolling files) for passwords, tokens, the signing key, the TIN and the marker price. Pass the folders with RV_LOGS (path-separated list).
import fs from 'node:fs';
import path from 'node:path';
import { call, tokens, fx, check, failures, useAdmin, PROBE_PW, newPo, line, items } from './lib.mjs';
const F = fx();
if (process.argv[2] === 'mark') {
  const T = await tokens(); useAdmin(T.admin);
  await call('POST', '/api/auth/login', null, { username: 'admin', password: 'Marker#Pw-SEC-77' });
  await call('POST', '/api/auth/login', null, { username: 'sec-marker-user', password: 'Marker#Pw-SEC-78' });
  const p = await call('POST', '/api/partners', T.admin, { type: 'Supplier', name: `SEC-${F.sfx} TIN partner`, contact: null, isActive: true, tin: 'C9876543210', sstRegNo: 'W10-1234-56789012' });
  console.log('partner with TIN:', p.status);
  await newPo(T.clerk, F, { lines: [line(F, { qty: 3, unitPrice: 731.29 })] });
  await call('GET', '/api/accounting/export?from=2026-01-01&to=2026-12-31', T.admin);
}
if (process.argv[2] === 'scan') {
  const files = (process.env.RV_LOGS ?? '').split(path.delimiter).filter(Boolean).flatMap((p) => fs.statSync(p).isDirectory() ? fs.readdirSync(p).map((n) => path.join(p, n)) : [p]).filter((p) => fs.statSync(p).isFile());
  const text = files.map((p) => fs.readFileSync(p, 'utf8')).join('\n');
  check(9, `log scan: ${files.length} files, ${(text.length / 1e6).toFixed(1)} MB read`, files.length > 0 && text.length > 1e5);
  const secrets = { 'seed admin password': process.env.RV_ADMIN_PW, 'seed demo password': process.env.RV_DEMO_PW, 'probe user password': PROBE_PW, 'JWT signing key': process.env.RV_JWT_KEY, 'marker password 1': 'Marker#Pw-SEC-77', 'marker password 2': 'Marker#Pw-SEC-78', 'TIN': 'C9876543210', 'SST number': 'W10-1234-56789012', 'a wrong password': 'wrong-password-1', 'marker price': '731.29' };
  for (const [n, v] of Object.entries(secrets)) check(9, `logs do not contain the ${n}`, !!v && !text.includes(v), text.includes(v ?? '\u0000') ? `FOUND ${(text.split(v).length - 1)}x` : 'absent');
  check(9, 'logs contain no JWT (eyJ... three-part token)', !/eyJ[A-Za-z0-9_-]{15,}\.eyJ[A-Za-z0-9_-]{10,}\./.test(text));
  check(9, 'logs contain no Authorization header value or Set-Cookie value', !/Bearer [A-Za-z0-9._-]{20,}/.test(text) && !/Set-Cookie/i.test(text));
  check(9, 'logs contain no refresh-token value (base64 of 64 bytes next to "refresh")', !/refresh[^\n]{0,40}[A-Za-z0-9+/]{80,}={0,2}/i.test(text));
  check(9, 'logs contain no stack trace for a 4xx (only 5xx may)', !/ 4\d\d in [^\n]*\n\s+at /.test(text));
  const lines = text.split('\n'); const failedLogin = lines.filter((l) => /Login failed/.test(l)).slice(0, 2);
  console.log('sample failed-login log lines:\n' + failedLogin.map((l) => l.slice(0, 220)).join('\n'));
  const reqLine = lines.find((l) => /HTTP POST \/api\/purchase-orders responded/.test(l)); console.log('sample request log line:\n' + (reqLine ?? '').slice(0, 260));
}
console.log(failures() ? `${failures()} FAIL` : 'p9-logs done');
