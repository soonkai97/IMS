import { join } from 'node:path';
import { compile } from 'sass';

// A02: the table decides table or cards by the width of its own box, and keeps its header and first column in view. jsdom
// lays nothing out, so the contract is proven on the compiled stylesheet (the e2e test 15-containment proves it in a browser).
const STYLES = join(__dirname, '../../../../../styles/src');
const css = compile(join(__dirname, 'table.component.scss'), { loadPaths: [STYLES] }).css;

/** The body of the first `@container` block that starts with `query`, with nested braces balanced. */
function container(query: string): string {
  const from = css.indexOf(query);
  expect(from).toBeGreaterThan(-1);
  let depth = 0;
  for (let index = css.indexOf('{', from); index < css.length; index++) {
    if (css[index] === '{') {
      depth++;
    } else if (css[index] === '}') {
      depth--;
    }
    if (depth === 0) {
      return css.slice(from, index + 1);
    }
  }
  throw new Error(`Unbalanced block after ${query}`);
}

const WIDE = container('@container ims-table (min-width: 45rem)');
const NARROW = container('@container ims-table (width < 45rem)');

describe('app-table styles (A02)', () => {
  it('is its own size container, so it answers to its box and not to the window', () => {
    expect(css).toMatch(/:host\s*{[^}]*container: ims-table ?\/ ?inline-size/);
  });

  it('is a list of cards in a narrow box and a table in a wide one, and no rule asks the window', () => {
    expect(NARROW).toContain('.ui-table-row');
    expect(NARROW).toContain('content: attr(data-label)');
    expect(WIDE).toContain('.ui-table-header');
    expect(WIDE).not.toContain('attr(data-label)');
    // Only a short window (400% zoom) may use a media query: the sticky header cannot take a screen that small.
    const queries = Array.from(css.matchAll(/@media[^{]*/g)).map(match => match[0].trim());
    expect(queries.every(query => query.includes('height'))).toBe(true);
  });

  it('keeps the header in view: it sticks under the top bar, or to the top of its own scroll box', () => {
    expect(WIDE).toMatch(
      /\.ui-table-header\s*{[^}]*position: sticky;[^}]*top: var\(--ims-table-head-top\)/
    );
    expect(WIDE).toMatch(/\.ui-table-scroll\s*{[^}]*--ims-table-head-top: calc\(3\.5rem/);
    expect(WIDE).toMatch(/\[data-scroll-region\]\s*{[^}]*--ims-table-head-top: 0/);
  });

  it('only scrolls inside its box when the columns need more room (the page is what scrolls otherwise)', () => {
    expect(WIDE).toMatch(/\.ui-table-scroll\s*{[^}]*overflow: clip/);
    expect(WIDE).toMatch(/\.ui-table-scroll\[data-scroll-region\]\s*{[^}]*overflow: auto/);
    expect(WIDE).toMatch(/\[data-scroll-region\]\s*{[^}]*max-height: calc\(100dvh/);
  });

  it('pins the first column only while the box scrolls sideways, capped so it cannot cover the rest', () => {
    const pinned = /\.ui-table-scroll\[data-scroll-region\] \.ui-table-cell:first-child[^{]*{[^}]*/;
    expect(WIDE).toMatch(pinned);
    const rule = WIDE.match(pinned)?.[0] ?? '';
    expect(rule).toContain('position: sticky');
    expect(rule).toContain('max-width: 16rem');
    // Outside a scrolling box nothing is pinned.
    expect(WIDE.replace(pinned, '')).not.toMatch(/:first-child[^{]*{[^}]*position: sticky/);
  });

  it('keeps the L00 rules: whole numbers and codes, clamped names, a bounded code box', () => {
    expect(WIDE).toContain('white-space: nowrap');
    expect(WIDE).toMatch(/\.ui-table-code\s*{[^}]*max-width: 14rem/);
    expect(WIDE).toMatch(/min-width: 6rem/);
  });
});
