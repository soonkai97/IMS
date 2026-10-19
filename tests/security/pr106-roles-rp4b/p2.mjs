import { call, short, T } from './lib.mjs';
const log = console.log;
const wh = (await call('GET', '/api/warehouses', T.admin)).json[0].id;
const count = async () => (await call('GET', '/api/users?pageSize=200', T.admin)).json.total;
const PW = 'Probe#Pass12345';
let n = 0;
const mk = (role, extra = {}) => ({ username: 'p' + (++n) + '_' + Date.now().toString(36).slice(-4), email: `p${n}${Date.now().toString(36).slice(-4)}@ims.local`, displayName: 'probe', role, isActive: true, password: PW, warehouseIds: [wh], ...extra });
log('== role shape on create (admin caller)');
const variants = {
  'Supervisor': 'Supervisor', 'lowercase': 'supervisor', 'UPPER': 'SUPERVISOR', 'trailing space': 'Supervisor ', 'leading space': ' Supervisor',
  'trailing tab': 'Supervisor\t', 'trailing newline': 'Supervisor\n', 'zero-width': 'Supervisor​', 'fullwidth': 'Ｓupervisor', 'turkish dotless': 'Supervısor', 'cyrillic A in Admin': 'Аdmin',
  'null char': 'Supervisor\u0000', 'embedded space': 'Super visor', '33 chars': 'S'.repeat(33), '32 chars unknown': 'S'.repeat(32), 'empty': '', 'whitespace': '   ',
  'sql': "Supervisor' OR '1'='1", 'percent': 'Super%', 'underscore': 'Supervisor_', 'unknown': 'Hacker', 'inactive role': 'Dormant', 'soft-deleted role': 'Gone', 'custom ok': 'Peek'
};
for (const [k, v] of Object.entries(variants)) {
  const before = await count();
  const r = await call('POST', '/api/users', T.admin, mk(v));
  const after = await count();
  log(`  ${k.padEnd(22)} -> ${short(r).padEnd(90)} ${r.status === 200 ? 'stored role=' + JSON.stringify(r.json.role) : ''} created=${after - before}`);
}
log('  role null/missing/number/object:');
for (const [k, v] of [['null', null], ['number', 5], ['object', { a: 1 }], ['array', ['Admin']]]) {
  const b = mk('x'); b.role = v; const r = await call('POST', '/api/users', T.admin, b); log('   ', k, short(r));
}
{ const b = mk('x'); delete b.role; const r = await call('POST', '/api/users', T.admin, b); log('    missing', short(r)); }
