import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { compile } from 'sass';

// The motion contract (globals/_motion.scss) and the finite skeleton shimmer, proven on the compiled CSS.
const SRC = __dirname;
const css = compile(join(SRC, 'index.scss'), { loadPaths: [SRC] }).css;

/** The body of the first rule whose selector line contains `selector`. */
function ruleBody(source: string, selector: string): string {
  const start = source.indexOf(selector);
  expect(start).toBeGreaterThan(-1);
  return source.slice(source.indexOf('{', start), source.indexOf('}', start));
}

describe('motion tokens', () => {
  it('declares three durations and two easings at :root', () => {
    const root = ruleBody(css, ':root {\n  --ims-dur-fast');

    expect(root).toContain('--ims-dur-fast: 120ms');
    expect(root).toContain('--ims-dur-base: 200ms');
    expect(root).toContain('--ims-dur-slow: 280ms');
    expect(root).toContain('--ims-ease-standard: cubic-bezier(0.4, 0, 0.2, 1)');
    expect(root).toContain('--ims-ease-out: cubic-bezier(0, 0, 0.2, 1)');
  });

  it('collapses every duration under prefers-reduced-motion', () => {
    const media = css.slice(css.indexOf('@media (prefers-reduced-motion: reduce)'));

    expect(media).toMatch(
      /--ims-dur-fast: 1ms[\s\S]*--ims-dur-base: 1ms[\s\S]*--ims-dur-slow: 1ms/
    );
  });

  it('collapses every duration with the test switch data-motion=off', () => {
    const off = ruleBody(css, ':root[data-motion=off] {');

    expect(off).toMatch(/--ims-dur-fast: 1ms[\s\S]*--ims-dur-base: 1ms[\s\S]*--ims-dur-slow: 1ms/);
  });
});

describe('no infinite animation under reduced motion or in tests', () => {
  it('runs every animation once and for 0.01ms with the OS setting', () => {
    const media = css.slice(css.indexOf('@media (prefers-reduced-motion: reduce)'));

    expect(media).toContain('animation-iteration-count: 1 !important');
    expect(media).toContain('animation-duration: 0.01ms !important');
    expect(media).toContain('transition-duration: 0.01ms !important');
  });

  it('does the same with data-motion=off', () => {
    const rules = css.slice(css.indexOf(':root[data-motion=off] *'));

    expect(rules).toContain('animation-iteration-count: 1 !important');
    expect(rules).toContain('animation-duration: 0.01ms !important');
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
