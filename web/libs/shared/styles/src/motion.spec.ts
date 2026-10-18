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

describe('press scale token', () => {
  const root = block(':root {\n  --ims-dur-fast');

  it('shrinks a pressed button to 0.97 normally', () => {
    expect(root).toContain('--ims-scale-press: 0.97');
  });

  it('is 1 (no movement) under prefers-reduced-motion', () => {
    expect(OS_SETTING).toContain('--ims-scale-press: 1');
  });

  it('is 1 with the test switch data-motion=off', () => {
    expect(SWITCH_TOKENS).toContain('--ims-scale-press: 1');
  });

  it.each([
    '../../ui/src/lib/components/button/button.component.scss',
    'globals/_link-button.scss'
  ])('%s presses through the token, with no scale of its own', file => {
    const scss = readFileSync(join(SRC, file), 'utf8');

    expect(scss).toContain('scale(press-scale())');
    expect(scss).not.toMatch(/scale\(\s*0?\.\d+/);
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

describe('overlay geometry tokens (R13)', () => {
  const root = block(':root {\n  --ims-dur-fast');

  it('lifts a dialog 12px, starts it at 0.94 and slides a sheet 100% in the normal case', () => {
    expect(root).toContain('--ims-move-md: 12px');
    expect(root).toContain('--ims-scale-pop: 0.94');
    expect(root).toContain('--ims-slide: 100%');
  });

  it.each([
    ['prefers-reduced-motion', OS_SETTING],
    ['data-motion=off', SWITCH_TOKENS]
  ])('moves, grows and slides nothing under %s, so only the opacity changes', (_name, tokens) => {
    expect(tokens).toContain('--ims-move-md: 0px');
    expect(tokens).toContain('--ims-scale-pop: 1');
    expect(tokens).toContain('--ims-slide: 0%');
  });
});

describe('overlay components animate through tokens only (R13)', () => {
  const UI = join(SRC, '../../ui/src/lib/components');
  const HOST = join(SRC, '../../../core/notification/src/lib/components/toast-host.component.scss');
  const DIALOG = join(UI, 'dialog/dialog.component.scss');
  const FILES = [
    DIALOG,
    join(UI, 'toast/toast.component.scss'),
    join(UI, 'banner/banner.component.scss'),
    join(UI, 'card/card.component.scss'),
    join(UI, 'empty-state/empty-state.component.scss'),
    HOST
  ];

  it.each(FILES)('%s has no literal duration, curve, distance or scale', file => {
    const scss = readFileSync(file, 'utf8');

    expect(scss).not.toMatch(/\d(ms|s)\b/);
    expect(scss).not.toContain('cubic-bezier');
    expect(scss).not.toMatch(/translate[XYZ]?\(\s*-?[\d.]/);
    expect(scss).not.toMatch(/scale\(\s*[\d.]/);
    expect(scss).not.toMatch(/transition:\s*all|will-change/);
  });

  const dialog = compile(DIALOG, { loadPaths: [SRC] }).css;

  it('the dialog enters from @starting-style with a token pop and a themed scrim', () => {
    expect(dialog).toMatch(/\.ui-dialog\[open\] \{[^}]*transition: opacity var\(--ims-dur-base\)/);
    expect(dialog).toContain(
      'transform: translateY(var(--ims-move-md)) scale(var(--ims-scale-pop))'
    );
    expect(dialog).toContain('@starting-style');
    expect(dialog).toContain('background: var(--ims-scrim)');
  });

  it('the phone sheet starts one sheet-height below and has the 16px top radius', () => {
    const phone = dialog.slice(dialog.indexOf('@media (max-width: 599px)'));

    expect(phone).toContain('transform: translateY(var(--ims-slide))');
    expect(phone).toContain('border-radius: 16px 16px 0 0');
  });

  it('the dialog has no exit transition (the panel leaves the DOM with the close)', () => {
    expect(dialog).not.toMatch(/allow-discrete|overlay /);
  });

  it('the toast host fades only: no transform in either keyframe', () => {
    const host = readFileSync(HOST, 'utf8');
    const keyframes = host.slice(host.indexOf('@keyframes ims-toast-in'));

    expect(keyframes).toContain('opacity: 0');
    expect(keyframes).not.toContain('transform');
  });

  it.each([
    ['dialog title', DIALOG, '.ui-dialog-title'],
    ['banner content', join(UI, 'banner/banner.component.scss'), '.ui-banner-content'],
    ['card heading', join(UI, 'card/card.component.scss'), '.ui-card-heading'],
    [
      'empty-state message',
      join(UI, 'empty-state/empty-state.component.scss'),
      '.ui-empty-state-message'
    ]
  ])('%s breaks a long unbroken value anywhere (L00)', (_name, file, selector) => {
    const css = compile(file, { loadPaths: [SRC] }).css;
    const rule = css.slice(css.indexOf(`${selector} {`));

    expect(rule.slice(0, rule.indexOf('}'))).toContain('overflow-wrap: anywhere');
  });
});
