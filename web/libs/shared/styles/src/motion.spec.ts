import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { compile } from 'sass';

// The motion contract (globals/_motion.scss) and the finite skeleton shimmer, proven on the compiled CSS.
const SRC = __dirname;
const css = compile(join(SRC, 'index.scss'), { loadPaths: [SRC] }).css;

/** One top-level block: from the first `start` to the next closing brace in column 0 (nested braces are indented). */
function block(start: string): string {
  const from = css.indexOf(start);
  expect(from).toBeGreaterThan(-1);
  return css.slice(from, css.indexOf('\n}', from) + 2);
}

const OS_SETTING = block('@media (prefers-reduced-motion: reduce)');
const SWITCH_TOKENS = block(':root[data-motion=off] {');
const SWITCH_RULES = block(':root[data-motion=off] *');
const COLLAPSED = /--ims-dur-fast: 1ms[\s\S]*--ims-dur-base: 1ms[\s\S]*--ims-dur-slow: 1ms/;

/** Every animation runs once for 0.01ms, so no loop keeps restarting. */
function expectStopped(rules: string): void {
  expect(rules).toContain('animation-iteration-count: 1 !important');
  expect(rules).toContain('animation-duration: 0.01ms !important');
  expect(rules).toContain('transition-duration: 0.01ms !important');
}

describe('motion tokens', () => {
  it('declares three durations and two easings at :root', () => {
    const root = block(':root {\n  --ims-dur-fast');

    expect(root).toContain('--ims-dur-fast: 120ms');
    expect(root).toContain('--ims-dur-base: 200ms');
    expect(root).toContain('--ims-dur-slow: 280ms');
    expect(root).toContain('--ims-ease-standard: cubic-bezier(0.4, 0, 0.2, 1)');
    expect(root).toContain('--ims-ease-out: cubic-bezier(0, 0, 0.2, 1)');
  });

  it('collapses every duration under prefers-reduced-motion', () => {
    expect(OS_SETTING).toMatch(COLLAPSED);
  });

  it('collapses every duration with the test switch data-motion=off', () => {
    expect(SWITCH_TOKENS).toMatch(COLLAPSED);
  });
});

describe('no infinite animation under reduced motion or in tests', () => {
  it('stops every animation with the OS setting (its own block, not the test switch)', () => {
    expect(OS_SETTING).not.toContain('data-motion');
    expectStopped(OS_SETTING);
  });

  it('stops every animation with data-motion=off', () => {
    expectStopped(SWITCH_RULES);
  });

  it('the skeleton shimmer is finite on its own (6 sweeps), not only through the safety net', () => {
    const skeleton = readFileSync(
      join(SRC, '../../ui/src/lib/components/skeleton/skeleton.component.scss'),
      'utf8'
    );

    expect(skeleton).toContain('$sweeps: 6;');
    expect(skeleton).not.toContain('infinite');
  });
});
