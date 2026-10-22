import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import {
  SKU_PREFIX,
  SLOW,
  SUPPLIER_PREFIX,
  cancelOpenOrders,
  command,
  createOrder,
  purchasingIsOn,
  setPurchasing
} from '../src/purchasing';
import { expect, test } from '../src/fixtures';
import { goTo, loadPage } from '../src/pages/shell';

// Reorder on the real API (PL2.10): what is below its safety stock in a warehouse, net of the open orders (a DRAFT counts), the editable
// quantity, the supplier chosen once and the entry link into the order editor. Purchasing is switched on from Settings with a reason and
// put back as found; every product is a throw-away one and every order is cancelled again. One scenario after the other (serial).
test.describe.configure({ mode: 'serial' });

const REASON_ON = 'E2E: reorder';
const token = unique();
const data = {
  foundOn: false,
  supplier: { id: '' as PublicId, name: `${SUPPLIER_PREFIX}RO ${token}` },
  product: { id: '' as PublicId, sku: `${SKU_PREFIX}RO-${token}` },
  orderIds: [] as PublicId[]
};
const admin = () => apiAs('admin');
const moduleSwitch = (page: Page) => page.getByRole('switch', { name: 'Purchasing' });
const search = (page: Page, text: string) =>
  page.getByRole('searchbox', { name: 'Search products' }).fill(text);
const row = (page: Page) => page.getByRole('row').filter({ hasText: data.product.sku });
const qty = (page: Page) =>
  page.getByRole('spinbutton', { name: `Quantity to order of ${data.product.sku}` });
const supplierBox = (page: Page) => page.getByRole('combobox', { name: /^Supplier/ });

/** Opens Reorder from the order list and looks for the throw-away product (a shared database holds many rows below their level). */
async function openReorder(page: Page): Promise<void> {
  await goTo(page, '/purchase-orders');
  await page.getByRole('link', { name: 'Reorder' }).click();
  await expect(page).toHaveURL(/\/purchase-orders\/reorder$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Reorder' })).toBeVisible();
  await search(page, data.product.sku);
}

test.describe('Reorder, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await purchasingIsOn();
    await cancelOpenOrders().catch(() => undefined);
    await setPurchasing(false, 'E2E: start from off');
    data.supplier.id = (await admin().createPartner('Supplier', { name: data.supplier.name })).id;
    // 10 safety, 3 on hand at cost 2.00: suggested 2 x 10 - 3 - 0 = 17, worth 34.00.
    const product = await admin().createProduct({ safetyStock: 10, sku: data.product.sku });
    data.product.id = product.id;
    await admin().post('receipts', [{ productId: product.id, qty: 3, unitCost: 2 }]);
  });

  test.afterAll(async () => {
    try {
      if (await purchasingIsOn()) {
        await cancelOpenOrders();
      }
    } finally {
      await setPurchasing(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given purchasing is off, then the address is the not-found page; the Admin turns the module on with a reason and the order list offers Reorder', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await loadPage(page, '/purchase-orders/reorder');
    await expect(page).toHaveURL(/\/not-found$/);

    await goTo(page, '/settings/modules');
    await moduleSwitch(page).click();
    const dialog = page.getByRole('dialog', { name: 'Turn on Purchasing?' });
    await dialog.getByRole('textbox').fill(REASON_ON);
    await dialog.getByRole('button', { name: 'Turn on' }).click();
    await expect(dialog).toBeHidden({ timeout: SLOW });
    await expect(moduleSwitch(page)).toHaveAttribute('aria-checked', 'true', { timeout: SLOW });
    expect(await purchasingIsOn()).toBe(true);

    await goTo(page, '/purchase-orders');
    await expect(page.getByRole('link', { name: 'Reorder' })).toBeVisible();
  });

  test('given a product below its safety stock, then the Clerk sees the row with the right numbers and a value, and the editable quantity starts at the suggestion', async ({
    page,
    signIn
  }) => {
    await signIn('clerk');
    await openReorder(page);

    await expect(row(page)).toBeVisible({ timeout: SLOW });
    await expect(row(page)).toContainText('Widget');
    const text = (await row(page).innerText()).replace(/\s+/g, ' ');
    // On hand 3, safety 10, on order 0, value 34.00 (17 at an average cost of 2.00).
    expect(text).toMatch(/\b3\b.*\b10\b.*\b0\b/);
    expect(text).toContain('34.00');
    await expect(qty(page)).toHaveValue('17');
    // The value follows the quantity in the box, at the same average cost.
    await qty(page).fill('5');
    await expect(row(page)).toContainText('10.00');
    await qty(page).fill('0');
    await expect(page.getByText('Enter a quantity above 0, with up to 4 decimals.')).toBeVisible();
    await qty(page).fill('17');
  });

  test('given nothing chosen, Create purchase order says what is missing; with a selected row, an edited quantity and a supplier it opens the editor with the lines prefilled; saving a draft takes the row off the list (a draft counts as on order)', async ({
    page,
    signIn
  }) => {
    await signIn('clerk');
    await openReorder(page);
    await expect(row(page)).toBeVisible({ timeout: SLOW });
    await page.getByRole('button', { name: 'Create purchase order' }).click();
    await expect(page.getByText('Choose a supplier.')).toBeVisible();
    await expect(page.getByText('Select at least one product to order.')).toBeVisible();
    await expect(page.getByText('Only this page is selected.')).toBeVisible();
    await expect(page).toHaveURL(/\/purchase-orders\/reorder$/);
    await qty(page).fill('5');
    await page.getByRole('checkbox', { name: `Select ${data.product.sku}` }).check();
    await expect(page.getByText('1 product selected')).toBeVisible();
    await supplierBox(page).selectOption({ label: data.supplier.name });
    await page.getByRole('button', { name: 'Create purchase order' }).click();

    await expect(page).toHaveURL(/\/purchase-orders\/new\?/);
    await expect(supplierBox(page).locator('option:checked')).toHaveText(data.supplier.name);
    await expect(
      page.getByRole('spinbutton', { name: `Quantity of ${data.product.sku}` })
    ).toHaveValue('5');
    await expect(
      page.getByRole('spinbutton', { name: `Unit price of ${data.product.sku}` })
    ).toHaveValue('');
    await page.getByRole('button', { name: 'Save draft' }).click();
    await expect(page).toHaveURL(/\/purchase-orders\/[0-9a-f-]{36}$/);
    const orderId = page.url().split('/').pop()!;
    data.orderIds.push(orderId);

    // The draft of 5 is on order: the row stays with the 12 still short. A second draft for the 12 covers it and the row leaves.
    await openReorder(page);
    await expect(qty(page)).toHaveValue('12', { timeout: SLOW });
    const second = await createOrder(data.supplier.id, data.product.id, { qty: 12, unitPrice: 5 });
    data.orderIds.push(second.id);
    await openReorder(page);
    await expect(page.getByText('No product to order matches this search.')).toBeVisible({
      timeout: SLOW
    });
    await expect(row(page)).toHaveCount(0);

    // Both orders cancelled: the shortage is back as it was.
    for (const id of data.orderIds) {
      await command('clerk', id, 'cancel');
    }
    await openReorder(page);
    await expect(qty(page)).toHaveValue('17', { timeout: SLOW });
  });

  test('given a person without cost.view and po.manage, then the row has no value, no selection and no way to create', async ({
    page,
    signIn
  }) => {
    await signIn('staff');
    await openReorder(page);

    await expect(row(page)).toBeVisible({ timeout: SLOW });
    await expect(page.getByRole('columnheader', { name: /^Value/ })).toHaveCount(0);
    await expect(row(page)).not.toContainText('34.00');
    await expect(page.getByRole('checkbox')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Create purchase order' })).toHaveCount(0);
    await expect(page.getByRole('spinbutton')).toHaveCount(0);
    await expect(row(page)).toContainText('17');
  });

  test.describe('360 px phone', () => {
    test.use({ viewport: { width: 360, height: 740 } });

    test('given a phone, then the rows are cards and the page does not scroll sideways', async ({
      page,
      signIn
    }) => {
      await signIn('clerk');
      await openReorder(page);
      await expect(row(page)).toBeVisible({ timeout: SLOW });
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      ).toBeLessThanOrEqual(0);
      // A card (title, name and unit, the figures, the box), not a screen high.
      expect((await row(page).boundingBox())?.height).toBeLessThan(320);
    });
  });

  test.describe('320 px with the text at 200%', () => {
    test.use({ viewport: { width: 320, height: 640 } });

    for (const [language, label] of [
      ['English', 'English'],
      ['Bahasa Melayu', 'Malay'],
      ['中文', 'Chinese']
    ] as const) {
      test(`given ${label}, then Reorder does not scroll sideways or clip`, async ({
        page,
        signIn,
        shell
      }) => {
        await signIn('clerk');
        if (language !== 'English') {
          await shell.setLanguage(language);
        }
        await goTo(page, '/purchase-orders/reorder');
        await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
        await page.getByRole('searchbox').fill(data.product.sku);
        await expect(page.getByRole('row').filter({ hasText: data.product.sku })).toBeVisible({
          timeout: SLOW
        });
        await page.waitForLoadState('networkidle');
        await page.waitForFunction(() => document.getAnimations().length === 0);
        await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        ).toBeLessThanOrEqual(0);
        const outside = await page
          .getByRole('main')
          .locator('input, select, button')
          .evaluateAll(items =>
            items
              .filter(item => item.getBoundingClientRect().right > 320)
              .map(
                item =>
                  `${item.tagName} ${item.getAttribute('aria-label') ?? item.id} ${item.getBoundingClientRect().right}`
              )
          );
        expect(outside).toEqual([]);
        await page.evaluate(() => (document.documentElement.style.fontSize = ''));
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

        test(`given Reorder with a row selected and one with a bad quantity, then axe finds no serious or critical violation (${scheme}, ${width})`, async ({
          page,
          signIn
        }) => {
          await signIn('clerk');
          await openReorder(page);
          await expect(row(page)).toBeVisible({ timeout: SLOW });
          await page.getByRole('checkbox', { name: `Select ${data.product.sku}` }).check();
          await qty(page).fill('0');
          await page.getByRole('button', { name: 'Create purchase order' }).click();
          await page.waitForLoadState('networkidle');
          await page.waitForFunction(() => document.getAnimations().length === 0);
          const { violations } = await new AxeBuilder({ page })
            .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
            .analyze();
          const serious = violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
          expect(
            serious.map(v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(' | ')}`)
          ).toEqual([]);
        });
      });
    }
  }

  test('given the module is switched off again, then the entry goes and the API answers 404', async ({
    page,
    signIn
  }) => {
    await cancelOpenOrders();
    await setPurchasing(false, 'E2E: reorder over');
    await signIn('admin');
    await loadPage(page, '/purchase-orders/reorder');
    await expect(page).toHaveURL(/\/not-found$/);
    expect((await admin().raw('GET', '/api/purchase-orders/suggestions')).status).toBe(404);
  });
});
