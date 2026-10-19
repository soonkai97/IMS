// Probe 3: catalog validation and the shape of role names, codes, descriptions and search. Needs s0 + tokens.
import fs from 'node:fs';
import { call, expect, short, etag, failures } from './lib.mjs';
const out = process.env.RV_OUT ?? '.';
const T = JSON.parse(fs.readFileSync(`${out}/tokens.json`, 'utf8'));
let n = 0;
const nm = (s) => `${s} ${++n}`;
const mk = (perms, name) => call('POST', '/api/roles', T.admin, { name: name ?? nm('Cat'), permissions: perms });
const raw = (b) => call('POST', '/api/roles', T.admin, b);

console.log('== permission codes (exact match against the catalog)');
for (const [label, perms, want] of [
  ['wrong case Users.Read', ['Users.Read'], 400], ['upper USERS.READ', ['USERS.READ'], 400], ['trailing space', ['users.read '], 400], ['leading space', [' users.read'], 400],
  ['trailing newline', ['users.read\n'], 400], ['NUL suffix', ['users.read\u0000'], 400], ['fullwidth u', ['ｕsers.read'], 400], ['Cyrillic e', ['stock.rеad'], 400], ['zero-width', ['stock.​read'], 400],
  ['empty string', [''], 400], ['null element', [null], 400], ['number element', [1], 400], ['object element', [{}], 400], ['nested list', [['stock.read']], 400],
  ['wildcard *', ['*'], 400], ['wildcard stock.*', ['stock.*'], 400], ['duplicate', ['stock.read', 'stock.read'], 400], ['dup different case', ['stock.read', 'Stock.Read'], 400],
  ['requires missing: users.manage alone', ['users.manage'], 400], ['requires missing: valuation.view alone', ['valuation.view'], 400], ['requires missing: document.post.approved alone', ['document.post.approved'], 400],
  ['closed: users.manage+users.read', ['users.manage', 'users.read'], 200], ['approve alone', ['document.approve'], 200], ['approve.large alone', ['document.approve.large'], 200],
  ['both approve tiers', ['document.approve', 'document.approve.large'], 200], ['empty set', [], 200], ['null list = blank role', null, 200]
]) {
  const r = await (perms === null ? raw({ name: nm('Cat') }) : mk(perms));
  expect(`create ${label}`, r, want);
}
expect('permissions is a string', await raw({ name: nm('Cat'), permissions: 'stock.read' }), 400);
expect('permissions is an object', await raw({ name: nm('Cat'), permissions: { a: 1 } }), 400);
expect('permissions + copy both', await raw({ name: nm('Cat'), permissions: ['stock.read'], copyFromRoleId: '00000000-0000-0000-0000-000000000000' }), 400);
expect('copyFromRoleId not a guid', await raw({ name: nm('Cat'), copyFromRoleId: 'abc' }), 400);
expect('copyFromRoleId random guid', await raw({ name: nm('Cat'), copyFromRoleId: 'f3a1c2d4-1111-4222-8333-444455556666' }), 400, 404);

console.log('== approval tiers: held independently');
{
  const a = (await mk(['document.approve'])).json; const l = (await mk(['document.approve.large'])).json;
  console.log('   standard-only role perms:', JSON.stringify(a.permissions), '| large-only role perms:', JSON.stringify(l.permissions));
}

console.log('== huge lists');
{
  const unknown = Array.from({ length: 3000 }, (_, i) => `x${i}`);
  let t0 = Date.now(); let r = await mk(unknown); console.log('   3000 unknown codes ->', r.status, 'response bytes', r.text.length, 'ms', Date.now() - t0);
  const dup = Array.from({ length: 5000 }, () => 'stock.read');
  t0 = Date.now(); r = await mk(dup); console.log('   5000 duplicate codes ->', r.status, 'response bytes', r.text.length, 'ms', Date.now() - t0);
  const near = Array.from({ length: 100000 }, (_, i) => 'a' + i);
  t0 = Date.now(); r = await mk(near); console.log('   100000 short unknown codes (~0.9 MB body) ->', r.status, 'response bytes', r.text.length, 'ms', Date.now() - t0);
  const many = Array.from({ length: 200000 }, (_, i) => `abcdefghij${i}`);
  t0 = Date.now(); r = await mk(many); console.log('   200000 unknown codes (~3 MB body) ->', r.status, 'response bytes', r.text.length, 'ms', Date.now() - t0);
  const big = 'x'.repeat(2_000_000);
  try { t0 = Date.now(); r = await mk([big]); console.log('   one 2 MB code ->', r.status, 'response bytes', r.text.length, 'ms', Date.now() - t0); } catch (e) { console.log('   one 2 MB code -> connection error', e.cause?.code ?? e.message); }
  const huge = 'y'.repeat(40_000_000);
  try { t0 = Date.now(); r = await call('POST', '/api/roles', T.admin, `{"name":"${huge}"}`); console.log('   40 MB body ->', r.status, 'ms', Date.now() - t0); } catch (e) { console.log('   40 MB body -> connection error', e.cause?.code ?? e.message); }
}

console.log('== name and description shapes');
const names = [
  ['1 char', 'A', 400], ['2 chars', 'Ab', 200], ['60 chars', 'N'.repeat(60), 200], ['61 chars', 'N'.repeat(61), 400], ['1 char + spaces', '  A  ', 400], ['spaces only', '     ', 400], ['empty', '', 400],
  ['NUL inside', 'ab\u0000cd', null], ['newline inside', 'line1\nline2', null], ['tab + CR', 'a\tb\rc', null], ['bidi override', 'abc‮def', null],
  ['html', '<script>alert(1)</script>', null], ['emoji', '\u{1F600}\u{1F600}x', null], ['Chinese', '仓库管理', null],
  ['Cyrillic A (look-alike of Admin)', 'Аdmin', null], ['fullwidth S (Supervisor)', 'Ｓupervisor', null],
  ['symbols only', '!!!', null], ['digits only', '12345', null],
  ['same as a system role (Supervisor)', 'Supervisor', 409], ['lowercase system role name', 'supervisor', 409], ['system name + trailing spaces', 'Supervisor   ', 409]
];
for (const [label, name, want] of names) {
  const r = await raw({ name });
  const ok = want === null ? [200, 400].includes(r.status) : r.status === want;
  console.log(`${ok ? 'ok  ' : 'FAIL'} name ${label} -> ${short(r)}${r.status === 200 ? ` code=${r.json.code} name=${JSON.stringify(r.json.name)}` : ''}`);
}
{
  const a = await raw({ name: 'Role' }); const b = await raw({ name: '!!!!' }); const c = await raw({ name: 'role' });
  console.log('   fallback codes:', a.json.code, b.json.code, short(c));
  const ad1 = await raw({ name: 'ad min' }); const ad2 = await raw({ name: 'ADMIN X' }); const ad3 = await raw({ name: 'a-d-m-i-n' });
  console.log('   code lookalike of Admin:', ad1.json.code, ad2.json.code, ad3.json.code);
  const l1 = await raw({ name: 'x'.repeat(31) + 'y' + ' extra' }); console.log('   >32 letters code length:', l1.json.code?.length, l1.json.code);
  const l2 = await raw({ name: 'x'.repeat(31) + 'y' + ' extra' }); console.log('   same again ->', short(l2));
  const l3 = await raw({ name: 'x'.repeat(31) + 'y' + ' extrb' }); console.log('   same 32-char prefix, other name ->', short(l3), l3.json.code);
}
{
  const d = (len) => raw({ name: nm('Desc'), description: 'd'.repeat(len) });
  expect('description 200', await d(200), 200); expect('description 201', await d(201), 400);
  expect('description null', await raw({ name: nm('Desc'), description: null }), 200);
  expect('description NUL/newline', await raw({ name: nm('Desc'), description: 'a\u0000b\nc' }), 200, 400);
  expect('description number', await raw({ name: nm('Desc'), description: 5 }), 400);
}

console.log('== search / filter inputs');
for (const [label, q, want] of [['%', 'search=%25', 200], ['_', 'search=_', 200], ['[a]', 'search=%5Ba%5D', 200], ["'", "search=%27", 200], ['300 chars', 'search=' + 'a'.repeat(300), 400], ['includeInactive=maybe', 'includeInactive=maybe', 400], ['includeInactive=true', 'includeInactive=true', 200]]) {
  const r = await call('GET', '/api/roles?' + q, T.admin); expect(`list ${label}`, r, want);
  if (r.status === 200 && label === '%') console.log('   search=% returns', r.json.length, 'roles (LIKE wildcard must be literal: expect 0)');
  if (r.status === 200 && label === '_') console.log('   search=_ returns', r.json.length, 'roles (expect 0)');
}
console.log('failures:', failures());
