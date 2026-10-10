#!/usr/bin/env node
// Re-brands this solution when it is used as a base project: replaces the product-name token everywhere.
//   Tokens:  Ims -> <Name>   IMS -> <NAME>   ims -> <name>
//   node scripts/rename-project.mjs --name Acme            dry run (lists what would change)
//   node scripts/rename-project.mjs --name Acme --apply    rewrite file contents and rename files/folders
// Matches whole tokens only (Ims.Api, Ims_Dev, ims-api, IMS_URL ...), never the inside of a word ("claims").
// Run it on a fresh copy of the repository, before the first commit of the new project.
import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';

const args = process.argv.slice(2);
const nameIdx = args.indexOf('--name');
const apply = args.includes('--apply');
const force = args.includes('--force');
const name = nameIdx >= 0 ? args[nameIdx + 1] : undefined;

if (!name || !/^[A-Z][A-Za-z0-9]*$/.test(name)) {
  console.error('Usage: node scripts/rename-project.mjs --name <PascalCaseName> [--apply] [--force]\nExample: --name Acme');
  process.exit(2);
}
const tokens = [
  { re: /(?<![A-Za-z])Ims(?![a-z])/g, to: name },
  { re: /(?<![A-Za-z])IMS(?![A-Za-z])/g, to: name.toUpperCase() },
  { re: /(?<![A-Za-z])ims(?![a-z])/g, to: name.toLowerCase() },
];
const replaceTokens = (text) => tokens.reduce((t, { re, to }) => t.replace(re, to), text);
const hasToken = (text) => tokens.some(({ re }) => new RegExp(re.source).test(text));

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..');
const SKIP_DIRS = new Set(['.git', 'bin', 'obj', 'node_modules', 'dist', '.angular', '.nx', 'logs', 'TestResults', 'coverage']);
const SKIP_FILES = [/^docs[\\/]prd/i, /package-lock\.json$/];
const TEXT_EXT = /\.(cs|csproj|sln|props|json|ya?ml|md|mjs|js|ts|tsx|html|scss|css|xml|txt|config|editorconfig)$|(^|[\\/])(Dockerfile|\.gitignore|\.editorconfig|CODEOWNERS)$/;

if (apply && !force && fs.existsSync(path.join(root, '.git'))) {
  const dirty = execSync('git status --porcelain', { cwd: root, encoding: 'utf8' }).trim();
  if (dirty) {
    console.error('Working tree has uncommitted changes. Commit or stash first (or pass --force).');
    process.exit(2);
  }
}

const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory()) { if (!SKIP_DIRS.has(e.name)) walk(path.join(dir, e.name)); continue; }
    const rel = path.relative(root, path.join(dir, e.name));
    if (SKIP_FILES.some(r => r.test(rel))) continue;
    files.push(rel);
  }
})(root);

let contentChanges = 0;
for (const rel of files.filter(f => TEXT_EXT.test(f))) {
  const abs = path.join(root, rel);
  const text = fs.readFileSync(abs, 'utf8');
  const next = replaceTokens(text);
  if (next === text) continue;
  contentChanges++;
  if (apply) fs.writeFileSync(abs, next);
}

// Rename deepest paths first so parents still exist while children move.
const renames = new Set();
for (const rel of files) {
  const parts = rel.split(/[\\/]/);
  for (let i = 1; i <= parts.length; i++) {
    const p = parts.slice(0, i).join(path.sep);
    if (hasToken(parts[i - 1])) renames.add(p);
  }
}
const ordered = [...renames].sort((a, b) => b.split(path.sep).length - a.split(path.sep).length);
for (const rel of ordered) {
  const dir = path.dirname(rel);
  const target = path.join(dir, replaceTokens(path.basename(rel)));
  if (apply && fs.existsSync(path.join(root, rel))) fs.renameSync(path.join(root, rel), path.join(root, target));
}

console.log(`${apply ? 'Changed' : 'Would change'}: ${contentChanges} file(s) of content, ${renames.size} path(s) renamed. Name: ${name}`);
if (!apply) {
  console.log('Dry run only. Re-run with --apply.');
} else {
  console.log([
    'Next steps:',
    '  1. dotnet build  (fix anything the token rename missed)',
    '  2. Delete src/*/Persistence/Migrations and create a new Initial migration for your own model.',
    '  3. Follow docs/TEMPLATE.md to remove the sample inventory domain and keep the base.',
  ].join('\n'));
}
