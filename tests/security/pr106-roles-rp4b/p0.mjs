import { call, login, short, decode } from './lib.mjs';
const out = {};
for (const u of ['admin', 'owner', 'supervisor', 'clerk', 'staff']) {
  const l = await login(u);
  const c = decode(l.token);
  out[u] = l.token;
  const perms = [].concat(c.perm ?? []);
  console.log(u, 'keys:', Object.keys(l.raw).join(','), '| role', c.role, '| allwh', c.allwh, '| wh', JSON.stringify(c.wh), '| nperm', perms.length, '| approve', perms.includes('document.approve'), 'large', perms.includes('document.approve.large'));
}
import fs from 'node:fs';
fs.writeFileSync('D:/ClaudeTemp/rv106/probe/tokens.json', JSON.stringify(out));
const a = out.admin;
console.log('rules', JSON.stringify((await call('GET', '/api/approval-rules', a)).json));
console.log('wh', JSON.stringify((await call('GET', '/api/warehouses', a)).json).slice(0, 300));
console.log('users', JSON.stringify((await call('GET', '/api/users', a)).json).slice(0, 1200));
