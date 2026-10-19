// Re-issue the tokens in fixtures.json against RV_BASE (a second instance has its own signing key).
import fs from 'node:fs';
import { login, PROBE_PW } from './lib.mjs';
const f = `${process.env.RV_OUT ?? '.'}/fixtures.json`; const F = JSON.parse(fs.readFileSync(f));
F.T.admin = (await login('admin')).token;
for (const u of ['owner','supervisor','clerk','staff']) F.T[u] = (await login(u)).token;
for (const n of ['dele','deleall','ronly','uread','admcore','bigu']) F.T[n] = (await login(n, PROBE_PW)).token;
fs.writeFileSync(f, JSON.stringify(F)); console.log('tokens ok');
