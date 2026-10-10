// WCAG AA contrast check for the colour palette: npm run contrast
// Parses the $light / $dark maps in libs/shared/styles/src/variables/_palette.var.scss.
import { readFileSync } from 'node:fs';

const file = new URL('../libs/shared/styles/src/variables/_palette.var.scss', import.meta.url);
const scss = readFileSync(file, 'utf8');

function readMap(name) {
  const start = scss.indexOf(`$${name}:`);
  const body = scss.slice(start, scss.indexOf(');', start));
  return Object.fromEntries(
    [...body.matchAll(/'([\w-]+)':\s*(#[0-9a-fA-F]{6})/g)].map(m => [m[1], m[2]])
  );
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

const statuses = ['draft', 'submitted', 'approved', 'posted', 'reversed', 'cancelled'];
const pairs = [
  ['text', 'bg', 4.5],
  ['text', 'surface', 4.5],
  ['text-muted', 'surface', 4.5],
  ['text-muted', 'bg', 4.5],
  ['text-muted', 'surface-2', 4.5],
  ['on-primary', 'primary', 4.5],
  ['on-primary', 'primary-hover', 4.5],
  ['primary', 'surface', 4.5],
  ['primary', 'bg', 4.5],
  ['on-danger', 'danger', 4.5],
  ['on-danger', 'danger-hover', 4.5],
  ['danger', 'surface', 4.5],
  ['on-danger-soft', 'danger-soft', 4.5],
  ['on-warning-soft', 'warning-soft', 4.5],
  ['on-success-soft', 'success-soft', 4.5],
  ['on-info-soft', 'info-soft', 4.5],
  ['on-primary-soft', 'primary-soft', 4.5],
  ['border-strong', 'surface', 3],
  ['focus', 'surface', 3],
  ['focus', 'bg', 3],
  ...statuses.map(s => [`on-status-${s}`, `status-${s}`, 4.5])
];

let failures = 0;
for (const theme of ['light', 'dark']) {
  const palette = readMap(theme);
  for (const [fg, bg, min] of pairs) {
    const value = ratio(palette[fg], palette[bg]);
    if (value < min) {
      failures += 1;
      console.log(`FAIL ${theme}: ${fg} on ${bg} = ${value.toFixed(2)} (need ${min})`);
    }
  }
}
console.log(failures ? `${failures} failing pair(s)` : 'All palette pairs meet WCAG AA.');
process.exit(failures ? 1 : 0);
