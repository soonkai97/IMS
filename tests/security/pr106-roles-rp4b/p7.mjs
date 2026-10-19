import fs from 'node:fs';
import { call, short, T, decode } from './lib.mjs';
const log = console.log;
const PW = 'Probe#Pass12345';
const ids = JSON.parse(fs.readFileSync('p3ids.json','utf8'));
const wh1 = (await call('GET', '/api/warehouses', T.admin)).json[0].id;
const users = async () => (await call('GET','/api/users?pageSize=200',T.admin)).json.items;
const u = (await users()).find(x=>x.id===ids.allwhId);
const put = (o) => call('PUT','/api/users/'+u.id,T.admin,{username:u.username,email:u.email,displayName:'x',role:'Supervisor',isActive:true,warehouseIds:[wh1],...o});
const stockCount = async (t) => { const r = await call('GET','/api/stock?pageSize=1',t); return r.status+':'+(r.json.total ?? JSON.stringify(r.json).slice(0,40)); };
// a second warehouse with stock would be best; here compare /me and claims
log('set flag true:', short(await put({allWarehouses:true})));
let l = await call('POST','/api/auth/login',null,{username:u.username,password:PW});
const A1 = l.json.accessToken, R1 = l.cookie;
log('login1 allwh=', decode(A1).allwh, 'wh=', JSON.stringify(decode(A1).wh), 'refreshToken in body:', JSON.stringify(R1.slice(0,12)));
log('flip false:', short(await put({allWarehouses:false})));
log('  old access token claim still allwh=', decode(A1).allwh, '; /me (from claims) seesAll=', (await call('GET','/api/auth/me',A1)).json.seesAllWarehouses);
const rf = await call('POST','/api/auth/refresh',null,{},{Cookie:R1,'X-Requested-With':'XMLHttpRequest',Origin:'http://localhost:4200'});
log('refresh ->', short(rf), 'allwh=', rf.status===200?decode(rf.json.accessToken).allwh:'', 'wh=', rf.status===200?JSON.stringify(decode(rf.json.accessToken).wh):'');
log('flip true again + refresh:', short(await put({allWarehouses:true})));
const rf2 = await call('POST','/api/auth/refresh',null,{},{Cookie:rf.cookie,'X-Requested-With':'XMLHttpRequest',Origin:'http://localhost:4200'});
log('  refresh ->', short(rf2), 'allwh=', decode(rf2.json.accessToken).allwh, 'wh claims=', JSON.stringify(decode(rf2.json.accessToken).wh));
log('replay of used refresh token ->', short(await call('POST','/api/auth/refresh',null,{},{Cookie:rf.cookie,'X-Requested-With':'XMLHttpRequest',Origin:'http://localhost:4200'})));
// custom role user: claims
const peek = (await users()).find(x=>x.role==='Peek');
const pl = await call('POST','/api/auth/login',null,{username:peek.username,password:PW});
const pc = decode(pl.json.accessToken);
log('custom role Peek login:', pl.status, 'role claim keys:', Object.keys(pc).filter(k=>/role/i.test(k)).map(k=>k+'='+pc[k]).join(','), 'perms=', JSON.stringify(pc.perm), 'UserInfo=', JSON.stringify({role:pl.json.user.role, roleName:pl.json.user.roleName, perms:pl.json.user.permissions}));
