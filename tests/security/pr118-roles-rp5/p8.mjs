// Probe 8: a non-Administrator who holds every permission (custom role "Big", all warehouses) against the system roles.
import fs from 'node:fs';
import { call, expect, short, etag } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
const roles = (await call('GET', '/api/roles', T.admin)).json; const S = Object.fromEntries(roles.filter((r) => r.isSystem).map((r) => [r.code, r]));
for (const c of ['Admin', 'Owner']) {
  const g = await call('GET', '/api/roles/' + S[c].id, T.bigu); const tag = etag(g);
  const r1 = await call('PUT', '/api/roles/' + S[c].id, T.bigu, { name: S[c].name, description: S[c].description, isActive: true });
  console.log(`bigu: PUT text ${c} (same values) ->`, short(r1));
  const r2 = await call('PUT', `/api/roles/${S[c].id}/permissions`, T.bigu, { permissions: S[c].permissions }, { 'If-Match': tag });
  console.log(`bigu: PUT perms ${c} (same set) ->`, short(r2));
}
