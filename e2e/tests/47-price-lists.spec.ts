import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import {
  CUSTOMER_PREFIX,
  PRICE_LIST_PREFIX,
  SKU_PREFIX,
  createPriceList,
  defaultPriceListExists,
  deletePriceLists,
  getPriceList,
  renamePriceList,
  salesIsOn,
  setSales,
  type ApiPriceList
} from '../src/sales';
import { expect, openSession, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { goTo, loadPage } from '../src/pages/shell';

// Price lists on the real API (PL3.13), serial: the menu and the route guards (module, then pricelist.manage), the list of lists,
// the editor (name, customer, prices from the shared picker, one price per product and unit), the whole list saved with its version,
// a stale copy, delete with a confirm, and the layout at 360 and 1280 px in light and dark. The module is read first and put back
// last; every list is deleted again.
test.describe.configure({ mode: 'serial' });

const token = unique();
const data = {
  foundOn: false,
  customer: { id: '' as PublicId, name: `${CUSTOMER_PREFIX}P${token}` },
  productId: '' as PublicId,
  productName: '',
  sku: `${SKU_PREFIX}P${token}`,
  seed: undefined as ApiPriceList | undefined
};
const admin = () => apiAs('admin');
const nameOf = (suffix: string): string => `${PRICE_LIST_PREFIX}${token} ${suffix}`;
const button = (page: Page, name: string) => page.getByRole('button', { name, exact: true });
const row = (page: Page, text: string) => page.getByRole('row').filter({ hasText: text });
const noSideways = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);

test.describe('Price lists, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await salesIsOn();
    await setSales(false, 'E2E: start from off');
    data.customer.id = (await admin().createPartner('Customer', { name: data.customer.name })).id;
    const product = await admin().createProduct({ sku: data.sku });
    data.productId = product.id;
    data.productName = product.name;
  });

  test.afterAll(async () => {
    try {
      if (await salesIsOn()) {
        await deletePriceLists();
      }
    } finally {
      await setSales(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given sales is off, then there is no menu entry and the address is the not-found page; with it on, the roles that hold pricelist.manage see the entry and the others get the forbidden page', async ({
    page,
    signIn,
    shell,
    browser
  }) => {
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Price lists');
    await loadPage(page, '/price-lists');
    await expect(page).toHaveURL(/\/not-found$/);
    expect((await admin().raw('GET', '/api/price-lists')).status).toBe(404);

    await setSales(true, 'E2E: price lists');
    // The module list is read when the app starts: a new load shows the switch.
    await loadPage(page, '/');
    await expect(shell.nav.getByRole('link', { name: 'Price lists' })).toBeVisible();
    await shell.nav.getByRole('link', { name: 'Price lists' }).click();
    await expect(page).toHaveURL(/\/price-lists$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Price lists' })).toBeVisible();

    for (const [role, sees] of [
      ['owner', true],
      ['supervisor', true],
      ['clerk', false],
      ['staff', false]
    ] as const) {
      const session = await openSession(browser, role);
      try {
        expect((await session.shell.menu()).includes('Price lists'), role).toBe(sees);
        if (!sees) {
          await loadPage(session.page, '/price-lists');
          await expect(session.page).toHaveURL(/\/forbidden$/);
        }
      } finally {
        await session.context.close();
      }
    }
  });

  test('given a Supervisor, when a list is made in the editor, then the words say what is missing, the prices are checked in words, and the saved list shows in the list of lists', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('supervisor');
    await shell.nav.getByRole('link', { name: 'Price lists' }).click();
    await page.getByRole('link', { name: 'New price list' }).click();
    await expect(page).toHaveURL(/\/price-lists\/new$/);
    const editor = new DocumentEditor(page);

    await button(page, 'Save').click();
    await expect(page.getByRole('textbox', { name: /^Name/ })).toBeFocused();

    const name = nameOf('Acme');
    await page.getByRole('textbox', { name: /^Name/ }).fill(name);
    await page
      .getByRole('combobox', { name: /^Customer/ })
      .selectOption({ label: data.customer.name });
    await editor.addItem({ sku: data.sku, name: data.productName });
    const price = page.getByRole('spinbutton', { name: `Price of ${data.sku}` });
    await price.fill('1.234');
    await button(page, 'Save').click();
    await expect(page.locator('[aria-invalid="true"]').first()).toBeFocused();
    await price.fill('5.5');
    await button(page, 'Save').click();

    await expect(page).toHaveURL(/\/price-lists$/);
    await expect(shell.toast('Price list saved.')).toHaveCount(1);
    await page.getByRole('searchbox', { name: 'Search price lists' }).fill(name);
    await expect(row(page, name)).toContainText(data.customer.name);
    await expect(row(page, name)).toContainText('1 price');
    data.seed = (
      await admin().call<{ items: ApiPriceList[] }>(
        'GET',
        `/api/price-lists?Search=${encodeURIComponent(name)}`
      )
    ).items[0];
    const saved = await getPriceList(data.seed!.id);
    expect(saved.customerId).toBe(data.customer.id);
    expect(saved.items).toMatchObject([{ productId: data.productId, price: 5.5 }]);
  });

  test('given a saved list, when only the name is changed, then the whole list is saved with the opened version; the same product twice is said in words and nothing is sent', async ({
    page,
    signIn
  }) => {
    const list = await getPriceList(data.seed!.id);
    await signIn('supervisor');
    await goTo(page, `/price-lists/${list.id}`);
    const editor = new DocumentEditor(page);
    await expect(page.getByRole('spinbutton', { name: `Price of ${data.sku}` })).toHaveValue('5.5');

    // The picker does not add a product that is already priced in its unit: it says so instead.
    const sheet = await editor.openAddItem();
    await sheet.getByRole('searchbox', { name: 'Find a product' }).fill(data.sku);
    await sheet.getByRole('button', { name: `${data.sku}, ${data.productName}` }).click();
    await expect(sheet.getByText(`${data.sku} already has a price in every unit`)).toBeVisible();
    await sheet.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByRole('spinbutton', { name: `Price of ${data.sku}` })).toHaveCount(1);

    const renamed = nameOf('Acme 2');
    await page.getByRole('textbox', { name: /^Name/ }).fill(renamed);
    await button(page, 'Save').click();
    await expect(page).toHaveURL(/\/price-lists$/);
    const after = await getPriceList(list.id);
    expect(after.name).toBe(renamed);
    expect(after.customerId).toBe(data.customer.id);
    expect(after.items).toMatchObject([{ productId: data.productId, price: 5.5 }]);
  });

  test('given a list changed behind a screen, when the screen saves, then one Reload banner says so, Save and Delete are locked, and Reload shows the new state', async ({
    page,
    signIn
  }) => {
    const list = await getPriceList(data.seed!.id);
    await signIn('supervisor');
    await goTo(page, `/price-lists/${list.id}`);
    await expect(page.getByRole('textbox', { name: /^Name/ })).toHaveValue(nameOf('Acme 2'));

    await renamePriceList(list, nameOf('Acme 3'));
    await page.getByRole('textbox', { name: /^Name/ }).fill(nameOf('Acme 4'));
    await button(page, 'Save').click();

    const banner = page.locator('app-banner').filter({ hasText: 'changed since you opened it' });
    await expect(banner).toBeVisible();
    await expect(button(page, 'Save')).toBeDisabled();
    await expect(button(page, 'Delete list')).toBeDisabled();
    await banner.getByRole('button', { name: 'Reload' }).click();
    await expect(page.getByRole('textbox', { name: /^Name/ })).toHaveValue(nameOf('Acme 3'));
    await expect(banner).toHaveCount(0);
  });

  test('given a default list, then a second default is refused in the API sentence that takes the focus, and a default list has no customer', async ({
    page,
    signIn
  }) => {
    // A default list exists before the screen asks for a second one, so the refusal always runs (one of ours when the database had none).
    if (!(await defaultPriceListExists())) {
      await createPriceList({ name: nameOf('First default'), isDefault: true });
    }
    await signIn('supervisor');
    await goTo(page, '/price-lists/new');
    await page.getByRole('textbox', { name: /^Name/ }).fill(nameOf('Default'));
    await page
      .getByRole('combobox', { name: /^Customer/ })
      .selectOption({ label: data.customer.name });
    await page.getByRole('checkbox', { name: /^Default list/ }).check();
    await expect(page.getByRole('combobox', { name: /^Customer/ })).toBeDisabled();
    await button(page, 'Save').click();

    const alert = page.locator('app-banner[role="alert"]');
    await expect(alert).toHaveCount(1);
    await expect(alert).toBeFocused();
    await expect(page).toHaveURL(/\/price-lists\/new$/);
  });

  test('given a list, when it is deleted after a confirm, then it is gone from the list and the API', async ({
    page,
    signIn,
    shell
  }) => {
    const list = await createPriceList({ name: nameOf('Delete me') });
    await signIn('supervisor');
    await goTo(page, `/price-lists/${list.id}`);

    await button(page, 'Delete list').click();
    const dialog = shell.dialog(`Delete ${nameOf('Delete me')}?`);
    await dialog.getByRole('button', { name: 'Keep list' }).click();
    await expect(page).toHaveURL(new RegExp(`/price-lists/${list.id}$`));

    await button(page, 'Delete list').click();
    await shell
      .dialog(`Delete ${nameOf('Delete me')}?`)
      .getByRole('button', { name: 'Delete list', exact: true })
      .click();
    await expect(page).toHaveURL(/\/price-lists$/);
    expect((await admin().raw('GET', `/api/price-lists/${list.id}`)).status).toBe(404);
  });

  test.describe('layout and accessibility', () => {
    let list: ApiPriceList;
    test.beforeAll(async () => {
      list = await createPriceList({
        name: nameOf('Layout'),
        items: [{ productId: data.productId, price: 7.25 }]
      });
    });

    test.describe('360 px phone', () => {
      test.use({ viewport: { width: 360, height: 740 } });

      test('given a phone, then the list is cards and neither page scrolls sideways', async ({
        page,
        signIn
      }) => {
        await signIn('supervisor');
        await goTo(page, '/price-lists');
        await page.getByRole('searchbox', { name: 'Search price lists' }).fill(nameOf('Layout'));
        const card = page.locator('app-table .ui-table-row').filter({ hasText: nameOf('Layout') });
        await expect(card).toBeVisible();
        expect((await card.boundingBox())?.height).toBeLessThan(200);
        expect(await noSideways(page)).toBeLessThanOrEqual(0);
        await goTo(page, `/price-lists/${list.id}`);
        await expect(page.getByRole('spinbutton', { name: `Price of ${data.sku}` })).toBeVisible();
        expect(await noSideways(page)).toBeLessThanOrEqual(0);
        await expect(button(page, 'Save')).toBeInViewport();
      });
    });

    for (const scheme of ['light', 'dark'] as const) {
      for (const width of [360, 1280]) {
        test.describe(`axe, ${scheme}, ${width} px`, () => {
          test.use({ colorScheme: scheme, viewport: { width, height: 740 } });

          test(`given the list and the editor, then axe finds no serious or critical violation (${scheme}, ${width})`, async ({
            page,
            signIn
          }) => {
            await signIn('supervisor');
            for (const path of ['/price-lists', `/price-lists/${list.id}`, '/price-lists/new']) {
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
    await deletePriceLists();
    await setSales(false, 'E2E: price lists over');
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Price lists');
    await loadPage(page, '/price-lists');
    await expect(page).toHaveURL(/\/not-found$/);
    expect((await admin().raw('GET', '/api/price-lists')).status).toBe(404);
  });
});
