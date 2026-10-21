import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import {
  NEEDS_APPROVAL,
  NO_APPROVAL,
  SKU_PREFIX,
  SLOW,
  SUPPLIER_PREFIX,
  cancelOpenOrders,
  command,
  createOrder,
  purchasingIsOn,
  setPurchasing
} from '../src/purchasing';
import { expect, openSession, test } from '../src/fixtures';
import { goTo, loadPage } from '../src/pages/shell';

// Purchase orders on the real API (PL2.7a): the menu, the route guards, the list (filters, "Waiting for you", empty states) and its layout.
// The module is read first and put back last (also after a failure). Every order is a throw-away one that is cancelled again. One
// scenario after the other (serial).
test.describe.configure({ mode: 'serial' });

const REASON_ON = 'E2E: purchase orders';
const token = unique();
const data = {
  foundOn: false,
  supplier: { id: '' as PublicId, name: `${SUPPLIER_PREFIX}${token}` },
  otherSupplier: { id: '' as PublicId, name: `${SUPPLIER_PREFIX}B${token}` },
  productId: '' as PublicId,
  sku: `${SKU_PREFIX}${token}`
};
const admin = () => apiAs('admin');
const moduleSwitch = (page: Page) => page.getByRole('switch', { name: 'Purchasing' });
const row = (page: Page, no: string) => page.getByRole('row').filter({ hasText: no });

test.describe('Purchase orders, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await purchasingIsOn();
    // The first scenario switches the module on from the screen: start from off (refused with 409 while an order is open).
    await cancelOpenOrders().catch(() => undefined);
    await setPurchasing(false, 'E2E: start from off');
    for (const supplier of [data.supplier, data.otherSupplier]) {
      supplier.id = (await admin().createPartner('Supplier', { name: supplier.name })).id;
    }
    data.productId = (await admin().createProduct({ sku: data.sku })).id;
  });

  test.afterAll(async () => {
    try {
      await setPurchasing(true, 'E2E: clean up');
      await cancelOpenOrders();
    } finally {
      await setPurchasing(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given purchasing is off, then there is no menu entry and the address is the not-found page; the Admin turns it on with a reason and the entry appears', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Purchase orders');
    await loadPage(page, '/purchase-orders');
    await expect(page).toHaveURL(/\/not-found$/);

    await goTo(page, '/settings/modules');
    await moduleSwitch(page).click();
    const dialog = page.getByRole('dialog', { name: 'Turn on Purchasing?' });
    await dialog.getByRole('textbox').fill('abc');
    await dialog.getByRole('button', { name: 'Turn on' }).click();
    await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
    expect(await purchasingIsOn()).toBe(false);

    await dialog.getByRole('textbox').fill(REASON_ON);
    await dialog.getByRole('button', { name: 'Turn on' }).click();
    await expect(dialog).toBeHidden({ timeout: SLOW });
    await expect(moduleSwitch(page)).toHaveAttribute('aria-checked', 'true', { timeout: SLOW });

    expect(await purchasingIsOn()).toBe(true);
    expect(await shell.menu()).toContain('Purchase orders');
    await shell.nav.getByRole('link', { name: 'Purchase orders' }).click();
    await expect(page).toHaveURL(/\/purchase-orders$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Purchase orders' })).toBeVisible();
  });

  test('given the module is on, then every role that holds po.read has the entry in its menu', async ({
    browser
  }) => {
    for (const role of ['clerk', 'supervisor', 'staff', 'owner'] as const) {
      const session = await openSession(browser, role);
      try {
        expect(await session.shell.menu()).toContain('Purchase orders');
      } finally {
        await session.context.close();
      }
    }
  });

  test('given orders of two suppliers, then the list filters by status, supplier and number, an approver finds the Submitted one marked "Waiting for you", and an empty result is not an empty list', async ({
    page,
    signIn,
    shell
  }) => {
    const waiting = await createOrder(data.supplier.id, data.productId, NEEDS_APPROVAL);
    await command('clerk', waiting.id, 'submit');
    const draft = await createOrder(
      data.otherSupplier.id,
      data.productId,
      NEEDS_APPROVAL,
      'E2E draft'
    );
    const released = await createOrder(data.supplier.id, data.productId, NO_APPROVAL);
    await command('clerk', released.id, 'submit');

    await signIn('supervisor');
    await shell.nav.getByRole('link', { name: 'Purchase orders' }).click();
    // The three orders are the newest, so they open the list.
    for (const order of [waiting, draft, released]) {
      await expect(row(page, order.no)).toBeVisible();
    }
    await expect(row(page, waiting.no)).toContainText(data.supplier.name);
    await expect(row(page, waiting.no)).toContainText('Main warehouse');
    await expect(row(page, waiting.no)).toContainText('6,000.00');
    await expect(row(page, draft.no)).toContainText('Draft');
    await expect(row(page, released.no)).toContainText('Approved');

    await page.getByRole('combobox', { name: 'Status' }).selectOption({ label: 'Submitted' });
    await expect(row(page, waiting.no)).toContainText('Waiting for you');
    await expect(row(page, draft.no)).toHaveCount(0);
    await expect(row(page, released.no)).toHaveCount(0);

    await page.getByRole('combobox', { name: 'Status' }).selectOption({ label: 'Any' });
    await page
      .getByRole('combobox', { name: 'Supplier' })
      .selectOption({ label: data.otherSupplier.name });
    await expect(row(page, draft.no)).toBeVisible();
    await expect(row(page, waiting.no)).toHaveCount(0);

    await page.getByRole('combobox', { name: 'Supplier' }).selectOption({ label: 'All' });
    await page.getByRole('searchbox', { name: 'Search purchase orders' }).fill(released.no);
    await expect(row(page, released.no)).toBeVisible();
    await expect(row(page, waiting.no)).toHaveCount(0);

    await page.getByRole('searchbox', { name: 'Search purchase orders' }).fill('');
    await page.getByRole('textbox', { name: /^From/ }).fill('01/01/2099');
    await expect(page.getByText('No purchase orders match these filters.')).toBeVisible();
    await page.getByRole('button', { name: 'Clear filters' }).click();
    await expect(row(page, waiting.no)).toBeVisible();

    // The submitter is not the reviewer: the Clerk sees the order without the mark.
    const clerk = await openSession(page.context().browser()!, 'clerk');
    await goTo(clerk.page, '/purchase-orders');
    await expect(row(clerk.page, waiting.no)).toBeVisible();
    await expect(row(clerk.page, waiting.no)).not.toContainText('Waiting for you');
    await clerk.context.close();
    await command('clerk', waiting.id, 'cancel', { reason: 'E2E: throw-away order' });
    await command('clerk', draft.id, 'cancel');
    await command('supervisor', released.id, 'cancel', { reason: 'E2E: throw-away order' });
  });

  test.describe('layout and accessibility', () => {
    let order: { id: PublicId; no: string };
    test.beforeAll(async () => {
      order = await createOrder(data.supplier.id, data.productId, NEEDS_APPROVAL);
      await command('clerk', order.id, 'submit');
    });

    test.describe('360 px phone', () => {
      test.use({ viewport: { width: 360, height: 740 } });

      test('given a phone, then the list is cards and does not scroll sideways', async ({
        page,
        signIn
      }) => {
        await signIn('supervisor');
        await goTo(page, '/purchase-orders');
        await expect(page.getByRole('link', { name: order.no })).toBeVisible();
        // A card (number and status, then the date, supplier, warehouse and amount), not a row of a wide table: far under a screen high.
        const card = page.locator('app-table .ui-table-row').filter({ hasText: order.no });
        await expect(card).toBeVisible();
        expect((await card.boundingBox())?.height).toBeLessThan(200);
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        ).toBeLessThanOrEqual(0);
      });
    });

    test.describe('320 px with the text at 200%', () => {
      test.use({ viewport: { width: 320, height: 640 } });

      for (const [language, label] of [
        ['English', 'English'],
        ['Bahasa Melayu', 'Malay'],
        ['中文', 'Chinese']
      ] as const) {
        test(`given ${label}, then the list does not scroll sideways or clip`, async ({
          page,
          signIn,
          shell
        }) => {
          await signIn('supervisor');
          if (language !== 'English') {
            await shell.setLanguage(language);
          }
          for (const path of ['/purchase-orders']) {
            await goTo(page, path);
            await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
            await page.waitForLoadState('networkidle');
            await page.waitForFunction(() => document.getAnimations().length === 0);
            await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
            expect(
              await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
            ).toBeLessThanOrEqual(0);
            const edges = await page
              .getByRole('main')
              .locator('input, select, button')
              .evaluateAll(items =>
                items
                  .filter(item => item.getBoundingClientRect().width > 0)
                  .map(item => item.getBoundingClientRect().right)
              );
            for (const right of edges) {
              expect(right).toBeLessThanOrEqual(320);
            }
            await page.evaluate(() => (document.documentElement.style.fontSize = ''));
          }
          if (language !== 'English') {
            await shell.setLanguage('English');
          }
        });
      }
    });

    for (const scheme of ['light', 'dark'] as const) {
      for (const width of [360, 1280]) {
        test.describe(`axe, ${scheme}, ${width} px`, () => {
          test.use({ colorScheme: scheme, viewport: { width, height: 740 } });

          test(`given the list, then axe finds no serious or critical violation (${scheme}, ${width})`, async ({
            page,
            signIn
          }) => {
            await signIn('supervisor');
            for (const path of ['/purchase-orders']) {
              await goTo(page, path);
              await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
              await page.waitForLoadState('networkidle');
              await page.waitForFunction(() => document.getAnimations().length === 0);
              const { violations } = await new AxeBuilder({ page })
                .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
                .analyze();
              const serious = violations.filter(
                v => v.impact === 'serious' || v.impact === 'critical'
              );
              expect(
                serious.map(
                  v => `${path} ${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(' | ')}`
                )
              ).toEqual([]);
            }
          });
        });
      }
    }
  });

  test('given the module is switched off again, then the entry goes and the API answers 404', async ({
    page,
    signIn,
    shell
  }) => {
    await cancelOpenOrders();
    await setPurchasing(false, 'E2E: purchase orders over');
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Purchase orders');
    await loadPage(page, '/purchase-orders');
    await expect(page).toHaveURL(/\/not-found$/);
    expect((await admin().raw('GET', '/api/purchase-orders')).status).toBe(404);
  });
});
