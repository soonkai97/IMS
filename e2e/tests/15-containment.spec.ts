import type { Locator, Page } from '@playwright/test';
import { apiAs, unique } from '../src/api';
import { expect, openSession, test } from '../src/fixtures';
import { LONG_VARIANTS, longValue, type LongVariant } from '../src/long-values';
import { DocumentEditor } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';

// L00 (N15): content never causes horizontal page scroll, whatever the length or script of a value the API accepts
// (names 300 characters, remarks 500). Only a deliberate scroll region may scroll sideways, and it is focusable and named.
const WIDTHS = [320, 360, 768, 1024, 1280, 1440];
const NAME = 300;
const REMARK = 500;

/** Waits until nothing on the page is loading (a refreshing table is aria-busy). */
async function settled(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(
    () => document.querySelector('[aria-busy="true"], .ui-table-progress') === null
  );
}

/** The page is not wider than the window, and every element that scrolls sideways is a focusable, named scroll region. */
async function expectContained(page: Page, label: string): Promise<void> {
  await settled(page);
  const result = await page.evaluate(() => {
    const doc = document.documentElement;
    const strays: string[] = [];
    for (const element of Array.from(document.querySelectorAll<HTMLElement>('body *'))) {
      const style = getComputedStyle(element);
      const scrolls = style.overflowX === 'auto' || style.overflowX === 'scroll';
      if (!scrolls || element.scrollWidth <= element.clientWidth || element.closest('app-tabs')) {
        continue;
      }
      const named =
        element.hasAttribute('data-scroll-region') &&
        element.tabIndex >= 0 &&
        (element.getAttribute('aria-label') ?? '').trim() !== '';
      if (!named) {
        strays.push(element.tagName.toLowerCase() + '.' + element.className);
      }
    }
    // When the page is too wide, name the widest elements so the failure says where.
    const wide = Array.from(document.querySelectorAll<HTMLElement>('body *'))
      .filter(element => element.getBoundingClientRect().right > doc.clientWidth + 1)
      .slice(0, 6)
      .map(element => `${element.tagName.toLowerCase()}.${element.className}`);
    return {
      page: doc.scrollWidth - doc.clientWidth,
      body: document.body.scrollWidth - doc.clientWidth,
      strays,
      wide
    };
  });
  expect(
    result.page,
    `${label}: page is wider than the window (${result.wide.join(' | ')})`
  ).toBeLessThanOrEqual(0);
  expect(result.body, `${label}: body is wider than the window`).toBeLessThanOrEqual(0);
  expect(result.strays, `${label}: sideways scrollers that are not named scroll regions`).toEqual(
    []
  );
  await expectCellsWhole(page, label);
}

/**
 * On a table (a box of 45rem and wider, A02: 1024 px windows and up) a number, date, ordinary code, status or action cell
 * is never broken across lines or clipped, and a chip is never cut: those values size their column, they are not squeezed
 * by it (L00, reviewer findings 1 and 2). A card (a narrower box) may wrap every value, so only rows that really are
 * table rows are measured; `expectMode` proves which rows those are.
 */
async function expectCellsWhole(page: Page, label: string): Promise<void> {
  const broken = await page.evaluate(() => {
    const problems: string[] = [];
    const selector = ['number', 'date', 'code', 'status', 'action']
      .flatMap(type => [`app-table th.--${type}`, `app-table td.--${type}`])
      .join(',');
    for (const cell of Array.from(document.querySelectorAll<HTMLElement>(selector))) {
      if (cell.getClientRects().length === 0) {
        continue;
      }
      // A table in card mode (its rows are grids or flex boxes, its header is hidden): its values may wrap.
      const bodyRow = cell.closest('table')?.querySelector('tbody tr');
      if (bodyRow && getComputedStyle(bodyRow).display !== 'table-row') {
        continue;
      }
      const isChipCell = cell.classList.contains('--status');
      // An ordinary code stays whole; a 64-character SKU wraps inside its bounded box (it must not push the other columns away).
      if (cell.classList.contains('--code') && (cell.textContent ?? '').trim().length > 24) {
        continue;
      }
      const range = document.createRange();
      const parts = isChipCell
        ? Array.from(cell.querySelectorAll<HTMLElement>('.ui-status-chip-label'))
        : [cell];
      for (const part of parts) {
        // The line boxes of the text itself (not of an icon or a button around it).
        const lines = new Set<number>();
        const walker = document.createTreeWalker(part, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
          // A chip beside a number (the Reversal badge under a document number) has its own line, and its own check below.
          if (
            (node.textContent ?? '').trim() === '' ||
            node.parentElement?.closest('.ui-status-chip')
          ) {
            continue;
          }
          range.selectNodeContents(node);
          for (const rect of Array.from(range.getClientRects())) {
            if (rect.width > 0) {
              lines.add(Math.round(rect.top));
            }
          }
        }
        const clipped = part.scrollWidth - part.clientWidth > 1;
        // A header may wrap at a space, never inside a word: at most one line per word.
        const words = (part.textContent ?? '').trim().split(/\s+/).length;
        const allowed = cell.tagName === 'TH' ? words : 1;
        if (lines.size > allowed || clipped) {
          problems.push(
            `${cell.tagName.toLowerCase()}.${cell.className.replace(/\s+/g, '.')} "${(part.textContent ?? '').trim().slice(0, 30)}" lines=${lines.size} clipped=${clipped}`
          );
        }
      }
    }
    // No chip in a table is cut, wherever it sits.
    for (const label of Array.from(
      document.querySelectorAll<HTMLElement>('app-table .ui-status-chip-label')
    )) {
      if (label.getClientRects().length > 0 && label.scrollWidth - label.clientWidth > 1) {
        problems.push(`chip "${(label.textContent ?? '').trim()}" clipped`);
      }
    }
    return problems;
  });
  expect(broken, `${label}: number, date, code or chip cells broken or cut`).toEqual([]);
}

/** A dialog or toast stays inside the window and does not scroll sideways itself. */
async function expectFits(locator: Locator, label: string): Promise<void> {
  const box = await locator.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return {
      left: rect.left,
      right: rect.right,
      overflow: element.scrollWidth - element.clientWidth,
      window: window.innerWidth
    };
  });
  expect(box.left, `${label}: left edge`).toBeGreaterThanOrEqual(-0.5);
  expect(box.right, `${label}: right edge`).toBeLessThanOrEqual(box.window + 0.5);
  expect(box.overflow, `${label}: scrolls sideways inside itself`).toBeLessThanOrEqual(0);
}

test.describe('L00 Content containment', () => {
  test.setTimeout(300_000);

  test('given 300-character names and 500-character remarks in four shapes, then no screen scrolls sideways and no number or chip is broken from 320 to 1440', async ({
    page,
    signIn,
    shell
  }) => {
    const admin = apiAs('admin');
    const mark = `L${unique()}`;
    const variantsOf = (suffix: string): [LongVariant, string][] =>
      LONG_VARIANTS.map(variant => [variant, `${mark}${suffix}`]);

    // One partner, product, user and posted receipt per shape, plus a draft that uses all the products.
    const products = [];
    const partners = [];
    for (const [variant, prefix] of variantsOf('')) {
      const token = unique();
      partners.push(
        await admin.createPartner('Supplier', {
          name: longValue(variant, NAME, `${prefix}-${variant}-`),
          contact: longValue(variant, NAME, 'c-')
        })
      );
      products.push(
        await admin.createProduct({
          sku: `L${token}${'S'.repeat(64)}`.slice(0, 64),
          name: longValue(variant, NAME, `${prefix}-${variant}-`)
        })
      );
      await admin.createUser({ displayName: longValue(variant, 200, `${prefix}-${variant}-`) });
    }
    // A line worth 1.2 billion, so the number columns meet a wide amount.
    const big = await admin.createProduct({ name: `${mark} big` });
    await admin.post('receipts', [{ productId: big.id, qty: 1000, unitCost: 1234567.89 }]);
    const lines = products.map(product => ({ productId: product.id, qty: 3, unitCost: 1 }));
    const posted = await admin.post('receipts', lines);
    const draft = await admin.createDocument('receipts', lines, {
      partnerId: partners[0]!.id,
      remark: longValue('latin', REMARK, `${mark}-draft-`)
    });
    for (const [index, [variant]] of variantsOf('').entries()) {
      await admin.createDocument('receipts', [lines[index]!], {
        partnerId: partners[index]!.id,
        remark: longValue(variant, REMARK, `${mark}-${variant}-`)
      });
    }

    await signIn('admin');
    const search = async (name: string | RegExp, term: string): Promise<void> => {
      const box = page.getByRole('searchbox', { name });
      if ((await box.count()) > 0) {
        await box.first().fill(term);
      }
    };

    for (const width of WIDTHS) {
      await page.setViewportSize({ width, height: 800 });
      const at = (screen: string): string => `${screen} at ${width}px`;

      await goTo(page, '/partners');
      await expect(page.getByRole('heading', { level: 1, name: 'Partners' })).toBeVisible();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Partners'));
      await expectMode(page, at('Partners'));

      // L00.3: a cut name is readable in full without a pointer: a real button, reachable by keyboard.
      const more = page.getByRole('button', { name: 'Show more' }).first();
      await expect(more).toBeVisible();
      await more.focus();
      await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Show less' }).first()).toHaveAttribute(
        'aria-expanded',
        'true'
      );
      await expectContained(page, at('Partners, name expanded'));

      // A dialog that holds the long values.
      await page.getByRole('button', { name: 'Edit' }).first().click();
      const dialog = shell.dialog('Edit partner');
      await expect(dialog).toBeVisible();
      await expectFits(dialog, at('Edit partner dialog'));
      await expectContained(page, at('Edit partner dialog'));
      await dialog.getByRole('button', { name: 'Cancel' }).click();
      await expect(dialog).toBeHidden();

      await goTo(page, '/products');
      await expect(page.getByRole('heading', { level: 1, name: 'Products' })).toBeVisible();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Products'));
      await expectMode(page, at('Products'));

      await goTo(page, '/users');
      await expect(page.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Users'));
      await expectMode(page, at('Users'));

      await goTo(page, '/receipts');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await page.getByRole('tab', { name: 'All' }).click();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Receipts list'));
      await expectMode(page, at('Receipts list'));

      await goTo(page, '/stock');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Stock'));
      await expectMode(page, at('Stock'));

      // The movements report has the widest table (13 columns). Its box is narrower than 45rem below a 1024 px window, so it
      // is cards: nothing scrolls and the table box is no region and no tab stop. From 1024 px it scrolls inside its own
      // named, focusable region when it must.
      await goTo(page, '/reports');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await expect(page.locator('.ui-table-scroll')).toBeVisible();
      await expectContained(page, at('Reports'));
      await expectMode(page, at('Reports'));
      if (width < 1024) {
        await expectFits(page.locator('.ui-table-scroll'), at('Reports cards'));
        await expect(page.locator('.ui-table-scroll')).not.toHaveAttribute('tabindex', /.*/);
      }

      const editor = new DocumentEditor(page);
      await editor.open('receipts', draft.id);
      await expectContained(page, at('Receipt editor (draft)'));

      await editor.open('receipts', posted.id);
      await expectContained(page, at('Receipt (posted)'));

      await editor.openNew('receipts');
      const sheet = await editor.openAddItem();
      await sheet.getByRole('searchbox', { name: 'Find a product' }).fill(mark);
      await expect(sheet.getByText(mark).first()).toBeVisible();
      await expectFits(sheet, at('Add item sheet'));
      await expectContained(page, at('Add item sheet'));
      await sheet.getByRole('button', { name: 'Done' }).click();
      await expect(sheet).toBeHidden();
    }
  });

  test('given the server answers with a 500-character message, then the toast stays inside the window at every width', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    const message = longValue('latin', REMARK, 'Server said ');
    await page.route(
      url => url.pathname === '/api/partners',
      route =>
        route.request().method() === 'POST'
          ? route.fulfill({
              status: 500,
              contentType: 'application/problem+json',
              body: JSON.stringify({ title: 'Failure', status: 500, detail: message })
            })
          : route.fallback()
    );
    for (const width of WIDTHS) {
      await page.setViewportSize({ width, height: 800 });
      await goTo(page, '/partners');
      await page.getByRole('button', { name: 'New partner' }).click();
      const dialog = page.getByRole('dialog', { name: 'New partner' });
      await dialog.getByRole('textbox', { name: /^Name/ }).fill(`L00 toast ${width}`);
      await dialog.getByRole('button', { name: 'Save' }).click();
      const toast = page.locator('app-toast').filter({ hasText: 'Server said' });
      await expect(toast).toBeVisible();
      await expectFits(toast, `Toast at ${width}px`);
      await expectContained(page, `Toast at ${width}px`);
      await dialog.getByRole('button', { name: 'Cancel' }).click();
      await toast.getByRole('button').last().click();
      await expect(toast).toBeHidden();
    }
  });
});

// A02: the table is a table or a list of cards by the width of its OWN box, its header and first column stay in view, and a
// row action says which row it acts on (ER-10).

/** Whether the first body row of the first table is a table row (a table) or a grid or flex box (a card). */
async function tableMode(page: Page): Promise<'table' | 'cards'> {
  return page.evaluate(() => {
    const row = document.querySelector('app-table tbody tr');
    if (!row) {
      throw new Error('No table row on the page');
    }
    return getComputedStyle(row).display === 'table-row' ? 'table' : 'cards';
  });
}

async function expectMode(page: Page, label: string): Promise<void> {
  const expected = page.viewportSize()!.width >= 1024 ? 'table' : 'cards';
  expect(await tableMode(page), `${label}: table or cards`).toBe(expected);
}

/** The geometry that sticky behaviour is judged by: the header cell, the first body cell and the box they sit in. */
function geometry(page: Page): Promise<{
  region: { top: number; scrollsSideways: boolean; scrollLeft: number; scrollTop: number };
  headTop: number;
  barBottom: number;
  firstLeft: number;
  firstBackground: string;
  secondLeft: number;
}> {
  return page.evaluate(() => {
    const region = document.querySelector<HTMLElement>('.ui-table-scroll')!;
    const first = region.querySelector<HTMLElement>('tbody td')!;
    const second = region.querySelectorAll<HTMLElement>('tbody td')[1]!;
    return {
      region: {
        top: region.getBoundingClientRect().top,
        scrollsSideways: region.hasAttribute('data-scroll-region'),
        scrollLeft: region.scrollLeft,
        scrollTop: region.scrollTop
      },
      headTop: region.querySelector<HTMLElement>('th')!.getBoundingClientRect().top,
      barBottom: document.querySelector('.shell-bar')!.getBoundingClientRect().bottom,
      firstLeft: first.getBoundingClientRect().left,
      firstBackground: getComputedStyle(first).backgroundColor,
      secondLeft: second.getBoundingClientRect().left
    };
  });
}

/** The accessible names of the actions in the last column of the first table. */
function rowActions(page: Page): Promise<{ name: string; text: string }[]> {
  return page
    .locator('app-table tbody td:last-child')
    .locator('button, a')
    .evaluateAll(elements =>
      elements.map(element => ({
        name: (element.getAttribute('aria-label') ?? element.textContent ?? '').trim(),
        text: (element.textContent ?? '').trim()
      }))
    );
}

test.describe('A02 Table: sticky header and first column, cards by container width, row actions named', () => {
  test.setTimeout(240_000);

  test('given a wide window, when the table is given a narrow box, then it is cards, and a table again when the box is wide', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await page.setViewportSize({ width: 1280, height: 800 });
    await goTo(page, '/partners');
    await expect(page.locator('app-table tbody tr').first()).toBeVisible();
    expect(await tableMode(page), 'a 1280px window gives the table room').toBe('table');

    // The window does not change; only the table's own box does (a narrow column, a dialog, a side panel).
    await page.addStyleTag({ content: 'app-table { max-width: 30rem; }' });
    await expect.poll(() => tableMode(page)).toBe('cards');
    await expectContained(page, 'Partners in a narrow box');

    await page.addStyleTag({ content: 'app-table { max-width: 60rem; }' });
    await expect.poll(() => tableMode(page)).toBe('table');
    await expectContained(page, 'Partners in a wide box');

    // The other way round: a 360px phone is cards whatever the table holds.
    await page.setViewportSize({ width: 360, height: 740 });
    await expect.poll(() => tableMode(page)).toBe('cards');
  });

  test('given a long list, when the page scrolls, then the header stays under the top bar, and a focused action is never hidden under it', async ({
    page,
    signIn
  }) => {
    const admin = apiAs('admin');
    const mark = `S${unique()}`;
    for (let index = 0; index < 24; index++) {
      await admin.createPartner('Supplier', {
        name: `${mark} partner ${String(index).padStart(2, '0')}`
      });
    }
    await signIn('admin');
    await page.setViewportSize({ width: 1280, height: 800 });
    await goTo(page, '/partners');
    await page.getByRole('searchbox', { name: /^Search/ }).fill(mark);
    await expect(page.getByText(`${mark} partner 23`)).toBeVisible();
    await settled(page);

    const before = await geometry(page);
    expect(before.region.scrollsSideways, 'five columns fit in 1008px: the page scrolls').toBe(
      false
    );
    await page.evaluate(() => window.scrollTo(0, 700));
    const after = await geometry(page);
    expect(after.headTop, 'header sits right under the top bar').toBeGreaterThanOrEqual(
      after.barBottom - 1
    );
    expect(after.headTop).toBeLessThanOrEqual(after.barBottom + 1);
    const covered = await page.evaluate(() => {
      const head = document.querySelector('th')!.getBoundingClientRect();
      const hit = document.elementFromPoint(head.left + 20, head.top + head.height / 2);
      return !hit || !hit.closest('th');
    });
    expect(covered, 'nothing covers the stuck header').toBe(false);

    // Keyboard, forward and then back (going back is what scrolls a focused action up under a sticky header): each Edit
    // button ends up fully below the stuck header and on screen.
    const walk = async (key: 'Tab' | 'Shift+Tab'): Promise<number> => {
      let seen = 0;
      for (let press = 0; press < 40 && seen < 24; press++) {
        await page.keyboard.press(key);
        const hidden = await page.evaluate(() => {
          const element = document.activeElement as HTMLElement;
          if (!element.closest('app-table tbody')) {
            return null;
          }
          const head = document.querySelector('th')!.getBoundingClientRect();
          const rect = element.getBoundingClientRect();
          return rect.top < head.bottom - 0.5 || rect.bottom > window.innerHeight + 0.5;
        });
        if (hidden !== null) {
          seen++;
          expect(hidden, `${key}: focused action ${seen} is under the header or off screen`).toBe(
            false
          );
        }
      }
      return seen;
    };
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.getByRole('searchbox', { name: /^Search/ }).focus();
    expect(await walk('Tab'), 'the walk forward reached the row actions').toBeGreaterThanOrEqual(10);
    expect(await walk('Shift+Tab'), 'the walk back reached the row actions').toBeGreaterThanOrEqual(
      10
    );
    await expectContained(page, 'Partners, sticky header');
  });

  test('given a table too wide for its box, when it scrolls both ways, then the header and the first column stay in view', async ({
    page,
    signIn
  }) => {
    const admin = apiAs('admin');
    const mark = `W${unique()}`;
    const lines = [];
    for (let index = 0; index < 12; index++) {
      const product = await admin.createProduct({ name: `${mark} product ${index}` });
      lines.push({ productId: product.id, qty: 2, unitCost: 3 });
    }
    await admin.post('receipts', lines);

    await signIn('admin');
    // Short enough for the scroll box to be shorter than its 12 rows, tall enough that sticky is not switched off (500px).
    await page.setViewportSize({ width: 1024, height: 600 });
    await goTo(page, '/reports');
    await expect(page.locator('.ui-table-scroll[data-scroll-region]')).toBeVisible();
    await settled(page);
    await expectMode(page, 'Reports');

    const region = page.locator('.ui-table-scroll');
    await expect(region).toHaveAttribute('role', 'region');
    await expect(region).toHaveAttribute('tabindex', '0');
    const start = await geometry(page);
    await region.evaluate(element => element.scrollTo({ left: 400, top: 120 }));
    const moved = await geometry(page);

    expect(moved.region.scrollLeft, 'it scrolled sideways').toBeGreaterThan(100);
    expect(moved.region.scrollTop, 'it scrolled down inside its own box').toBeGreaterThan(50);
    expect(
      Math.abs(moved.headTop - moved.region.top),
      'header stays at the top of the box'
    ).toBeLessThanOrEqual(2);
    expect(
      Math.abs(moved.firstLeft - start.firstLeft),
      'first column stays where it was'
    ).toBeLessThanOrEqual(1);
    expect(moved.secondLeft, 'the other columns moved').toBeLessThan(start.secondLeft - 100);
    expect(
      moved.firstBackground,
      'the pinned cell is opaque: the columns scroll under it'
    ).not.toMatch(/rgba\(0, 0, 0, 0\)|transparent/);
    await expectContained(page, 'Reports, scrolled');
  });

  test('given the screens with a button on every row, then no two row actions on a screen share a name and each name holds the visible label', async ({
    browser,
    page,
    signIn
  }) => {
    const admin = apiAs('admin');
    await admin.createProduct({ safetyStock: 5 });
    await admin.createProduct({ safetyStock: 5 });
    await signIn('admin');

    const screens: [string, string, number][] = [
      ['Users', '/users', 2],
      ['Partners', '/partners', 2],
      ['Settings approval rules', '/settings', 1],
      ['Audit log', '/audit-log', 2]
    ];
    for (const [label, path, atLeast] of screens) {
      await goTo(page, path);
      await expect(page).toHaveURL(new RegExp(`${path}$`));
      await expect
        .poll(async () => (await rowActions(page)).length, `${label}: row actions found`)
        .toBeGreaterThanOrEqual(atLeast);
      await settled(page);
      const actions = await rowActions(page);
      const names = actions.map(action => action.name);
      expect(names.length, `${label}: row actions found`).toBeGreaterThanOrEqual(atLeast);
      expect(new Set(names).size, `${label}: names repeat: ${names.join(' | ')}`).toBe(
        names.length
      );
      for (const action of actions) {
        expect(
          action.name.toLowerCase(),
          `${label}: the name of "${action.text}" holds its visible label (WCAG 2.5.3)`
        ).toContain(action.text.toLowerCase());
      }
    }

    // Low stock offers New receipt to the roles that create documents.
    const clerk = await openSession(browser, 'clerk');
    try {
      await goTo(clerk.page, '/low-stock');
      await expect
        .poll(async () => (await rowActions(clerk.page)).length, 'Low stock: row actions found')
        .toBeGreaterThanOrEqual(2);
      await settled(clerk.page);
      const names = (await rowActions(clerk.page)).map(action => action.name);
      expect(names.length, 'Low stock: row actions found').toBeGreaterThanOrEqual(2);
      expect(new Set(names).size, `Low stock: names repeat: ${names.join(' | ')}`).toBe(
        names.length
      );
      expect(names.every(name => name.startsWith('New receipt for '))).toBe(true);
    } finally {
      await clerk.context.close();
    }
  });
});
