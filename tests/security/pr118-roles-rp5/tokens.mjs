// Re-issue the tokens used by the probes (after a restart or an expiry). Writes tokens.json in RV_OUT.
import fs from 'node:fs';
import { login, PROBE_PW } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = {};
for (const u of ['admin', 'owner', 'supervisor', 'clerk', 'staff']) T[u] = (await login(u)).token;
for (const n of ['dele', 'narrow', 'reader', 'bigu']) T[n] = (await login(n, PROBE_PW)).token;
fs.writeFileSync(`${out}/tokens.json`, JSON.stringify(T));
console.log('tokens ok');
