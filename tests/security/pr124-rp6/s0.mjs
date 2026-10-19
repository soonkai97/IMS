import { setup, adminCount } from './adm.mjs';
const fx = await setup();
console.log(Object.keys(fx.users).map(u => `${u}:${fx.users[u]?.id ? 'ok' : 'MISSING'}`).join(' '), 'admins', adminCount());
console.log(Object.fromEntries(Object.entries(fx.R).map(([k, v]) => [k, v.code])));
