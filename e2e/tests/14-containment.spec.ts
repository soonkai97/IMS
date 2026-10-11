import type { Locator, Page } from '@playwright/test';
import { apiAs, unique } from '../src/api';
import { expect, test } from '../src/fixtures';
import { LONG_VARIANTS, longValue, type LongVariant } from '../src/long-values';
import { DocumentEditor } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';

// L00 (N15): content never causes horizontal page scroll, whatever the length or script of a value the API accepts
// (names 300 characters, remarks 500). Only a deliberate scroll region may scroll sideways, and it is focusable and named.
const WIDTHS = [320, 360, 768, 1440];
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
    return {
      page: doc.scrollWidth - doc.clientWidth,
      body: document.body.scrollWidth - doc.clientWidth,
      strays
    };
  });
  expect(result.page, `${label}: page is wider than the window`).toBeLessThanOrEqual(0);
  expect(result.body, `${label}: body is wider than the window`).toBeLessThanOrEqual(0);
  expect(result.strays, `${label}: sideways scrollers that are not named scroll regions`).toEqual(
    []
  );
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

  test('given 300-character names and 500-character remarks in four shapes, then no screen scrolls sideways at 320, 360, 768 and 1440', async ({
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

      await goTo(page, '/users');
      await expect(page.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Users'));

      await goTo(page, '/receipts');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await page.getByRole('tab', { name: 'All' }).click();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Receipts list'));

      await goTo(page, '/stock');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await search(/^Search/, mark);
      await expect(page.getByText(mark).first()).toBeVisible();
      await expectContained(page, at('Stock'));

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
