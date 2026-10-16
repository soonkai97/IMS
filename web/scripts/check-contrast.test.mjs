// Mutation tests for the contrast gate: each case damages a copy of the palette and the gate must exit 1.
// Run with `node --test scripts/check-contrast.test.mjs` (npm run contrast runs it after the gate itself).
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const script = new URL('./check-contrast.mjs', import.meta.url);
const palette = readFileSync(
  new URL('../libs/shared/styles/src/variables/_palette.var.scss', import.meta.url),
  'utf8'
).replace(/\r\n/g, '\n');
const dir = mkdtempSync(join(tmpdir(), 'ims-contrast-'));

/** Runs the gate on a palette text; returns the exit status and its output. */
function gate(text) {
  const copy = join(dir, 'palette.scss');
  writeFileSync(copy, text);
  const run = spawnSync(process.execPath, [fileURLToPath(script), '--all'], {
    env: { ...process.env, IMS_PALETTE_FILE: copy },
    encoding: 'utf8'
  });
  return { status: run.status, out: run.stdout };
}

/** Replaces the first occurrence of `from`; fails the test when the palette no longer contains it (stale mutation). */
function mutate(from, to) {
  assert.ok(palette.includes(from), `palette no longer contains: ${from}`);
  return palette.replace(from, to);
}

test('the real palette passes with 164 spec checks', () => {
  const { status, out } = gate(palette);
  assert.equal(status, 0, out);
  assert.match(out, /^164 checks, 0 failing$/m);
});

const failing = {
  'text-muted too light for its background': [
    "'text-muted': #4a5361",
    "'text-muted': #aaaaaa",
    /FAIL light: text-muted on/
  ],
  'alias points to a token that does not exist': [
    "'primary': 'accent'",
    "'primary': 'nothing'",
    /does not exist/
  ],
  'alias points to the wrong existing token (typo)': [
    "'primary': 'accent'",
    "'primary': 'accent-soft'",
    /must point to "accent"/
  ],
  'alias missing': ["  'bg': 'canvas',\n", '', /alias "bg" must point to "canvas"/],
  'alias not in the spec mapping': [
    "'bg': 'canvas',",
    "'bg': 'canvas', 'extra-old': 'canvas',",
    /not in the spec 6.2 mapping/
  ],
  'alias named like a canonical token': [
    "'bg': 'canvas',",
    "'bg': 'canvas', 'text': 'canvas',",
    /same name as a canonical token/
  ],
  'token only in dark': [
    "'chart-6': #9aa5b5",
    "'chart-6': #9aa5b5, 'extra': #123456",
    /in \$dark but not in \$light/
  ],
  'new token with no contrast pair': [
    "'chart-6': #5b6472",
    "'chart-6': #5b6472, 'spare': #123456",
    /token "spare" is in no contrast pair/
  ],
  'brand panel drifts from the accent': [
    "'brand': #2447c4",
    "'brand': #2447c5",
    /token "brand" must equal "accent" in light/
  ],
  'eight-digit (alpha) hex': [
    "'canvas': #f4f5f7",
    "'canvas': #f4f5f7cc",
    /not a 6-digit hex colour/
  ],
  'shorthand hex': ["'canvas': #f4f5f7", "'canvas': #f4f", /not a 6-digit hex colour/]
};

for (const [name, [from, to, expected]] of Object.entries(failing)) {
  test(`gate fails: ${name}`, () => {
    const { status, out } = gate(mutate(from, to));
    assert.equal(status, 1, out);
    assert.match(out, expected);
  });
}

test.after(() => rmSync(dir, { recursive: true, force: true }));
