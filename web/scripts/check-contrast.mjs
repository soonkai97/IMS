// WCAG AA contrast gate for the colour palette: npm run contrast  (add --all to print every pair)
// Parses the $light, $dark and $aliases maps in libs/shared/styles/src/variables/_palette.var.scss and exits 1 when
//  - a pair of the pair list below falls under its WCAG ratio (4.5 text, 3 UI components) in light or dark,
//  - $light and $dark do not have the same keys, a value is not a 6-digit hex colour (no alpha, no shorthand),
//  - an alias differs from the spec 6.2 mapping, has no target in both themes or shares its name with a canonical token,
//  - a pair names an unknown token, or a token is in no pair (and not in the NOT_PAIRED list with its reason).
// The pair list is Appendix A of docs/design/2026-refresh-visual.md. A new token enters the palette only together with the
// pairs that use it. IMS_PALETTE_FILE points the script at another palette file (used to prove that the gate fails).
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const file = process.env.IMS_PALETTE_FILE
  ? pathToFileURL(resolve(process.env.IMS_PALETTE_FILE))
  : new URL('../libs/shared/styles/src/variables/_palette.var.scss', import.meta.url);
const scss = readFileSync(file, 'utf8')
  .split('\n')
  .filter(line => !line.trim().startsWith('//'))
  .join('\n');

function mapBody(name) {
  const start = scss.indexOf(`$${name}:`);
  if (start < 0) {
    return null;
  }
  return scss.slice(start, scss.indexOf(');', start));
}

/** Reads `'name': value` entries; value is a quoted token name for $aliases, a hex colour otherwise. */
function readMap(name, valuePattern) {
  const body = mapBody(name);
  if (body === null) {
    return null;
  }
  const entries = [...body.matchAll(new RegExp(`'([\\w-]+)':\\s*(${valuePattern})`, 'g'))];
  return Object.fromEntries(entries.map(m => [m[1], m[2].replace(/'/g, '')]));
}

function luminance(hex) {
  const [r, g, b] = [1, 3, 5]
    .map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map(c => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function ratio(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

// Exactly six hex digits: an 8-digit value (alpha) or a shorthand does not match and is reported below.
const HEX = '#[0-9a-fA-F]{6}(?![0-9a-fA-F])';
const themes = { light: readMap('light', HEX), dark: readMap('dark', HEX) };
const aliases = readMap('aliases', "'[\\w-]+'");
const problems = [];

for (const [name, map] of Object.entries({ ...themes, aliases })) {
  if (!map || Object.keys(map).length === 0) {
    problems.push(`$${name} map not found or empty in ${file.pathname}`);
  }
}
if (problems.length > 0) {
  problems.forEach(p => console.log(`FAIL ${p}`));
  process.exit(1);
}

// Structure: same keys in both themes, every value a 6-digit hex (a shorthand or rgb() value would be skipped silently).
const lightKeys = Object.keys(themes.light);
const darkKeys = Object.keys(themes.dark);
for (const key of lightKeys.filter(k => !darkKeys.includes(k))) {
  problems.push(`token "${key}" is in $light but not in $dark`);
}
for (const key of darkKeys.filter(k => !lightKeys.includes(k))) {
  problems.push(`token "${key}" is in $dark but not in $light`);
}
const hexCount = body => (body.match(/#[0-9a-fA-F]+\b/g) ?? []).length;
for (const name of ['light', 'dark']) {
  const found = Object.keys(themes[name]).length;
  if (hexCount(mapBody(name)) !== found) {
    problems.push(
      `$${name} has a value that is not a 6-digit hex colour (found ${found} valid entries)`
    );
  }
}

// Aliases: old name -> canonical token (visual spec 6.2). Never a copy of a value, always a target that exists in both
// themes, and exactly the mapping below: a typo in a target that still exists (primary -> accent-soft) must fail too.
// R24 deletes the aliases and this table with them.
const EXPECTED_ALIASES = {
  bg: 'canvas',
  'surface-2': 'surface-sunken',
  'surface-3': 'surface-strong',
  skeleton: 'surface-strong',
  primary: 'accent',
  'primary-hover': 'accent-hover',
  'on-primary': 'on-accent',
  'primary-soft': 'accent-soft',
  'on-primary-soft': 'on-accent-soft',
  'warning-border': 'warning',
  'status-draft': 'surface-strong',
  'on-status-draft': 'text',
  'status-submitted': 'accent-soft',
  'on-status-submitted': 'on-accent-soft',
  'status-approved': 'info-soft',
  'on-status-approved': 'on-info-soft',
  'status-posted': 'success-soft',
  'on-status-posted': 'on-success-soft',
  'status-reversed': 'warning-soft',
  'on-status-reversed': 'on-warning-soft',
  'status-cancelled': 'surface-strong',
  'on-status-cancelled': 'text-muted'
};
for (const [alias, target] of Object.entries(EXPECTED_ALIASES)) {
  if (aliases[alias] !== target) {
    problems.push(
      `alias "${alias}" must point to "${target}" (spec 6.2), found "${aliases[alias] ?? 'nothing'}"`
    );
  }
}
for (const alias of Object.keys(aliases).filter(a => !(a in EXPECTED_ALIASES))) {
  problems.push(`alias "${alias}" is not in the spec 6.2 mapping of this script`);
}
for (const [alias, target] of Object.entries(aliases)) {
  if (lightKeys.includes(alias)) {
    problems.push(`alias "${alias}" has the same name as a canonical token`);
  }
  for (const theme of ['light', 'dark']) {
    if (!(target in themes[theme])) {
      problems.push(`alias "${alias}" points to "${target}", which does not exist in $${theme}`);
    }
  }
}
const aliasBody = mapBody('aliases');
if ((aliasBody.match(/'[\w-]+':/g) ?? []).length !== Object.keys(aliases).length) {
  problems.push('$aliases has an entry that is not "old-name": "canonical-name"');
}

// Pair list (Appendix A of the visual spec): 81 pairs x 2 themes = 162 checks.
const TEXT = 4.5;
const UI = 3;
const surfaces = ['canvas', 'surface', 'surface-raised', 'surface-sunken', 'surface-strong'];
const roles = ['success', 'warning', 'danger', 'info'];
const pairs = [];
const add = (fg, bgs, min, kind) => bgs.forEach(bg => pairs.push([fg, bg, min, kind]));

add('text', [...surfaces, 'accent-soft'], TEXT, 'text');
add('text-muted', [...surfaces, 'accent-soft'], TEXT, 'text');
add('accent', ['canvas', 'surface', 'surface-raised', 'surface-sunken'], TEXT, 'text (link)');
add('on-accent-soft', ['accent-soft'], TEXT, 'text');
add('on-accent', ['accent', 'accent-hover', 'accent-pressed'], TEXT, 'text');
for (const role of roles) {
  add(role, ['canvas', 'surface', 'surface-raised', 'surface-sunken'], TEXT, 'text');
  add(`on-${role}`, [role], TEXT, 'text');
  add(`on-${role}-soft`, [`${role}-soft`], TEXT, 'text');
  add(role, [`${role}-soft`], UI, 'icon / edge');
}
add('on-danger', ['danger-hover', 'danger-pressed'], TEXT, 'text');
add('accent', ['accent-soft'], UI, 'icon / edge');
add('text-muted', ['surface-strong'], TEXT, 'text (disabled button)');
add('border-strong', ['canvas', 'surface', 'surface-raised'], UI, 'control boundary');
add('accent', ['canvas', 'surface', 'surface-raised'], UI, 'indicator / fill');
add(
  'focus',
  [...surfaces, 'accent-soft', 'success-soft', 'warning-soft', 'danger-soft', 'info-soft'],
  UI,
  'focus ring'
);
add('danger', ['surface'], UI, 'invalid border');
for (let i = 1; i <= 6; i += 1) {
  add(`chart-${i}`, ['surface', 'surface-raised'], UI, 'chart series');
}

// Call sites that still read a soft-tint text colour straight on a plain surface (reports stock effect "+5", the line-editor
// override hint, the warning flag chip). The spec list does not hold these; they leave when R24 moves the call sites to the
// role colours. Counted and reported separately so the spec count stays at 162.
const callSitePairs = [];
for (const fg of ['on-success-soft', 'on-warning-soft']) {
  for (const bg of ['surface', 'surface-sunken']) {
    callSitePairs.push([fg, bg, TEXT, 'text on a plain surface (call site until R24)']);
  }
}

for (const [fg, bg] of [...pairs, ...callSitePairs]) {
  for (const token of [fg, bg]) {
    if (!(token in themes.light)) {
      problems.push(`pair "${fg}" on "${bg}" names an unknown token "${token}"`);
    }
  }
}
// Every token is either in a pair or on this list. A token nobody checks is how a palette drifts (the pair list is not
// derived from the palette), so a new token needs its pairs here, or a stated reason to be exempt.
const NOT_PAIRED = {
  border: 'decorative divider and card outline, never the only cue (spec 2.3)',
  overlay: 'scrim colour, used at 35 to 60% over content'
};
const paired = new Set([...pairs, ...callSitePairs].flatMap(([fg, bg]) => [fg, bg]));
for (const token of lightKeys) {
  if (!paired.has(token) && !(token in NOT_PAIRED)) {
    problems.push(`token "${token}" is in no contrast pair and not in NOT_PAIRED: add its pairs`);
  }
  if (paired.has(token) && token in NOT_PAIRED) {
    problems.push(`token "${token}" is in NOT_PAIRED but has a pair: remove it from NOT_PAIRED`);
  }
}
for (const token of Object.keys(NOT_PAIRED).filter(t => !lightKeys.includes(t))) {
  problems.push(`NOT_PAIRED lists "${token}", which is not a token`);
}
if (problems.length > 0) {
  problems.forEach(p => console.log(`FAIL ${p}`));
  console.log(`${problems.length} palette problem(s)`);
  process.exit(1);
}

const showAll = process.argv.includes('--all');
function check(list) {
  let failures = 0;
  for (const theme of ['light', 'dark']) {
    for (const [fg, bg, min, kind] of list) {
      const value = ratio(themes[theme][fg], themes[theme][bg]);
      const ok = value >= min;
      failures += ok ? 0 : 1;
      if (!ok || showAll) {
        console.log(
          `${ok ? 'ok  ' : 'FAIL'} ${theme}: ${fg} on ${bg} = ${value.toFixed(2)} (need ${min}) [${kind}]`
        );
      }
    }
  }
  return { checks: list.length * 2, failures };
}

const spec = check(pairs);
const extra = check(callSitePairs);
console.log(`${extra.checks} call-site checks, ${extra.failures} failing`);
console.log(`${spec.checks} checks, ${spec.failures} failing`);
console.log(`${Object.keys(aliases).length} aliases, all point to an existing token`);
process.exit(spec.failures + extra.failures ? 1 : 0);
