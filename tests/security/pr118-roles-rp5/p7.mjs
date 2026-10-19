// Probe 7: a scoped delegate changes a role that a wider-scope user holds (user save refuses to touch that user directly).
import fs from 'node:fs';
import { call, expect, short, etag, login, PROBE_PW } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
const F = JSON.parse(fs.readFileSync(`${out}/fixtures.json`, 'utf8'));
const sfx = Date.now().toString(36).slice(-4);
const role = (await call('POST', '/api/roles', T.admin, { name: `Scope Probe ${sfx}`, permissions: ['stock.read'] })).json;
const wide = (await call('POST', '/api/users', T.admin, { username: `wide${sfx}`, email: `wide${sfx}@ims.local`, displayName: 'wide', role: role.code, isActive: true, password: PROBE_PW, warehouseIds: [F.wh1], allWarehouses: true })).json;
console.log('wide user allWarehouses =', wide.allWarehouses, '| delegate (dele) is scoped to one warehouse');
expect('dele edits the wide user directly (user save)', await call('PUT', '/api/users/' + wide.id, T.dele, { username: wide.username, email: wide.email, displayName: 'x', role: role.code, isActive: true, warehouseIds: [F.wh1], allWarehouses: true }), 403);
const tag = etag(await call('GET', '/api/roles/' + role.id, T.dele));
expect('dele widens the role the wide user holds (roles API)', await call('PUT', `/api/roles/${role.id}/permissions`, T.dele, { permissions: ['stock.read', 'masterdata.read', 'cost.view'] }, { 'If-Match': tag }), 200);
const rf = await login(wide.username, PROBE_PW);
const claims = JSON.parse(Buffer.from(rf.token.split('.')[1], 'base64url').toString());
console.log('wide user token now: perm =', JSON.stringify(claims.perm), 'allwh =', claims.allwh);
