// Lists every message key referenced in the sources and compares it with the English catalog.
//   node tools/scan-i18n-keys.mjs            -> prints missing keys, exit 1 when any are missing
//   node tools/scan-i18n-keys.mjs --unused   -> also prints catalog keys nothing references
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const PREFIXES = [
  'app',
  'ui',
  'validation',
  'error',
  'nav',
  'shell',
  'theme',
  'role',
  'status',
  'docType',
  'purpose',
  'partnerType',
  'ledgerReason',
  'login',
  'home',
  'products',
  'product',
  'import',
  'catalog',
  'warehouses',
  'warehouse',
  'partners',
  'partner',
  'stock',
  'ledger',
  'lowStock',
  'approvals',
  'reports',
  'users',
  'user',
  'settings',
  'rule',
  'audit',
  'doc',
  'docs',
  'dialog',
  'receipts',
  'issues',
  'line',
  'picker',
  'filter',
  'common',
  'forbidden',
  'notFound',
  'system',
  'pwa',
  'language'
];
// Permission names look like keys ('stock.read') but are not.
const NOT_KEYS = new Set([
  'audit.read',
  'document.create',
  'document.approve',
  'document.post',
  'document.reverse',
  'stock.read',
  'users.manage',
  'users.read',
  'reports.read',
  'settings.manage'
]);
const KEY = new RegExp("'((?:" + PREFIXES.join('|') + ")(?:[.][A-Za-z0-9]+)+)'", 'g');

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === 'node_modules' || name === 'dist') continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, out);
    else if (
      /[.](ts|html)$/.test(name) &&
      !name.endsWith('.spec.ts') &&
      !path.endsWith('.messages.ts') &&
      !path.includes('api-client')
    )
      out.push(path);
  }
  return out;
}

const used = new Set();
for (const file of [...walk('libs'), ...walk('apps')]) {
  for (const match of readFileSync(file, 'utf8').matchAll(KEY)) {
    if (!NOT_KEYS.has(match[1])) used.add(match[1]);
  }
}

const catalog = readFileSync('libs/core/i18n/src/lib/messages/en.messages.ts', 'utf8');
const defined = new Set([...catalog.matchAll(/^\s*'([^']+)':/gm)].map(match => match[1]));
const missing = [...used].filter(key => !defined.has(key)).sort();
const unused = [...defined].filter(key => !used.has(key)).sort();
console.log('used ' + used.size + ', defined ' + defined.size);
if (missing.length) console.log('MISSING:\n' + missing.join('\n'));
if (process.argv.includes('--unused') && unused.length)
  console.log('UNUSED:\n' + unused.join('\n'));
process.exit(missing.length ? 1 : 0);
