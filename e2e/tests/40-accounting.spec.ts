import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import {
  PARTNER_PREFIX,
  SKU_PREFIX,
  SLOW,
  accountingIsOn,
  retireThrowAways,
  setAccounting,
  type PartnerRow,
  type ProductRow
} from '../src/accounting';
import { expect, test } from '../src/fixtures';
import { goTo, loadPage } from '../src/pages/shell';

// The accounting module on the real API, the master-data side (PL1.8a): an Administrator switches it ON with a reason from Settings >
// Modules, the product form and the partner dialog show the tax fields and keep them across a reload and an unrelated edit (the form
// sends both fields of a pair on every save, plan risk 11), the product import page names the two optional columns, and the module is
// switched OFF again from the screen and the fields go. The export is `41-accounting-export`. One scenario after the other (serial):
// each step uses what the one before left.
//
// The installation is left as it was found. The module state is read first and put back last (also when a step failed), and the
// product and the partner are throw-away rows of this run (their tax values are cleared and they are deactivated).
test.describe.configure({ mode: 'serial' });

const HS_CODE = '0901.21';
const SST_CODE = 'ST-01';
const TIN = 'C2026E2E01';
const SST_REG_NO = 'W10-1808-32000001';
const REASON_ON = 'E2E: accounting pilot start';
const REASON_OFF = 'E2E: accounting pilot over';

const token = unique();
const data = {
  sku: `${SKU_PREFIX}${token}`,
  name: `E2E Tea ${token}`,
  partner: `${PARTNER_PREFIX}${token}`,
  productId: undefined as PublicId | undefined,
  /** Whether the module was on when the run began (it is switched off for the first scenario and put back at the end). */
  foundOn: false
};

const admin = () => apiAs('admin');
const moduleSwitch = (page: Page) => page.getByRole('switch', { name: 'Accounting' });

test.describe('Accounting module, forms and import, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await accountingIsOn();
    // The first scenario turns it on from the screen, so it must start off.
    await setAccounting(false, 'E2E: start from off');
  });

  test.afterAll(async () => {
    // Clearing the tax values needs the module on (off, a save leaves them alone); then it goes back to how it was found.
    try {
      await setAccounting(true, 'E2E: clean up');
      await retireThrowAways();
    } finally {
      await setAccounting(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given the Admin turns accounting on with a reason, then the switch says On, one toast says so and the switch keeps the focus', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await goTo(page, '/settings/modules');
    await expect(moduleSwitch(page)).toHaveAttribute('aria-checked', 'false');
    await expect(moduleSwitch(page)).not.toHaveAttribute('aria-disabled', 'true');

    await moduleSwitch(page).click();
    const dialog = page.getByRole('dialog', { name: 'Turn on Accounting?' });
    await dialog.getByRole('textbox').fill('abc');
    await dialog.getByRole('button', { name: 'Turn on' }).click();
    await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
    expect(await accountingIsOn()).toBe(false);

    await dialog.getByRole('textbox').fill(REASON_ON);
    await dialog.getByRole('button', { name: 'Turn on' }).click();

    // The first flip after the API started can take 15 s (a cold query for the roles that regain their grants); the switch is busy meanwhile.
    await expect(dialog).toBeHidden({ timeout: SLOW });
    await expect(moduleSwitch(page)).toHaveAttribute('aria-checked', 'true', { timeout: SLOW });
    await expect(shell.toast('Accounting is now on.')).toHaveCount(1);
    expect(await accountingIsOn()).toBe(true);
    await expect(moduleSwitch(page)).toBeFocused();
  });

  test('given the module is on, then a product is saved with its codes, they survive a reload, and an edit of the name alone keeps them', async ({
    page,
    signIn
  }) => {
    const unit = (await admin().call<{ code: string; name: string }[]>('GET', '/api/units')).find(
      item => item.code === 'PCS'
    );
    await signIn('admin');
    await goTo(page, '/products/new');
    await expect(page.getByLabel('HS code')).toBeVisible();
    await expect(page.getByLabel('SST code')).toBeVisible();
    await expect(page.getByText('Type it as text, for example 0901.21.')).toBeVisible();

    await page.getByLabel('SKU').fill(data.sku);
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill(data.name);
    await page.getByLabel('Base unit').selectOption({ label: `${unit?.code} - ${unit?.name}` });
    await page.getByLabel('HS code').fill('0901-21');
    await page.getByRole('button', { name: 'Save' }).first().click();
    await expect(
      page.getByText('HS code may contain only digits and dots, up to 16 characters.')
    ).toBeVisible();
    await expect(page.getByLabel('HS code')).toBeFocused();

    await page.getByLabel('HS code').fill(` ${HS_CODE} `);
    await page.getByLabel('SST code').fill(SST_CODE);
    await page.getByRole('button', { name: 'Save' }).first().click();
    await expect(page).toHaveURL(/\/products\/[0-9a-f-]{36}$/);
    data.productId = page.url().split('/').pop();

    // The server stored the trimmed value, and a reload shows what the server holds.
    const stored = await admin().call<ProductRow>('GET', `/api/products/${data.productId}`);
    expect([stored.hsCode, stored.sstCode]).toEqual([HS_CODE, SST_CODE]);
    await loadPage(page, `/products/${data.productId}`);
    await expect(page.getByLabel('HS code')).toHaveValue(HS_CODE);
    await expect(page.getByLabel('SST code')).toHaveValue(SST_CODE);

    // Only the name changes. The form still sends both codes, so they are not cleared.
    await page
      .getByRole('textbox', { name: 'Name', exact: true })
      .fill(`${data.name} renamed`);
    await page.getByRole('button', { name: 'Save' }).first().click();
    await expect(page.locator('app-toast').filter({ hasText: 'saved' })).toHaveCount(1);
    const after = await admin().call<ProductRow>('GET', `/api/products/${data.productId}`);
    expect(after.name).toBe(`${data.name} renamed`);
    expect([after.hsCode, after.sstCode]).toEqual([HS_CODE, SST_CODE]);
    await loadPage(page, `/products/${data.productId}`);
    await expect(page.getByLabel('HS code')).toHaveValue(HS_CODE);

    // Clearing a code on purpose still works: an emptied field is sent empty and the server stores none.
    await page.getByLabel('SST code').fill('');
    await page.getByRole('button', { name: 'Save' }).first().click();
    await expect(page.locator('app-toast').filter({ hasText: 'saved' })).toHaveCount(1);
    const cleared = await admin().call<ProductRow>('GET', `/api/products/${data.productId}`);
    expect([cleared.hsCode, cleared.sstCode]).toEqual([HS_CODE, undefined]);
  });

  test('given the module is on, then a partner is saved with its tax numbers, they survive a reload, and an edit of the contact alone keeps them', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await goTo(page, '/partners');
    await page.getByRole('button', { name: 'New partner' }).click();
    const dialog = page.getByRole('dialog', { name: 'New partner' });
    await expect(dialog.getByLabel('TIN')).toBeVisible();
    await expect(dialog.getByLabel('SST registration no.')).toBeVisible();

    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(data.partner);
    await dialog.getByLabel('TIN').fill('C-2026');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(
      dialog.getByText('TIN may contain only letters and digits, up to 20 characters.')
    ).toBeVisible();
    await expect(dialog.getByLabel('TIN')).toBeFocused();

    await dialog.getByLabel('TIN').fill(TIN);
    await dialog.getByLabel('SST registration no.').fill(SST_REG_NO);
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(dialog).toBeHidden();

    const find = async (): Promise<PartnerRow | undefined> =>
      (
        await admin().call<{ items: PartnerRow[] }>(
          'GET',
          `/api/partners?search=${encodeURIComponent(data.partner)}`
        )
      ).items.find(item => item.name === data.partner);
    const stored = await find();
    expect([stored?.tin, stored?.sstRegNo]).toEqual([TIN, SST_REG_NO]);

    // Reload, open the edit form: the numbers are there; change only the contact; they must not be cleared.
    await loadPage(page, '/partners');
    await page.getByRole('searchbox').fill(data.partner);
    await page.getByRole('button', { name: new RegExp(`^Edit ${data.partner}`) }).click();
    const edit = page.getByRole('dialog', { name: 'Edit partner' });
    await expect(edit.getByLabel('TIN')).toHaveValue(TIN);
    await expect(edit.getByLabel('SST registration no.')).toHaveValue(SST_REG_NO);
    await edit.getByLabel('Contact').fill('e2e@example.test');
    await edit.getByRole('button', { name: 'Save' }).click();
    await expect(edit).toBeHidden();

    const after = await find();
    expect(after?.contact).toBe('e2e@example.test');
    expect([after?.tin, after?.sstRegNo]).toEqual([TIN, SST_REG_NO]);
  });

  test('given the module is on, then the product import page names the two optional columns', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await goTo(page, '/products/import');

    await expect(page.getByText(/Optional columns for accounting: HsCode/)).toBeVisible();
    await expect(page.getByText(/Format both columns as Text/)).toBeVisible();
  });

  test.describe('layout and accessibility with the module on', () => {
    test.describe('320 px with the text at 200%', () => {
      test.use({ viewport: { width: 320, height: 640 } });

      for (const [language, label] of [
        ['English', 'English'],
        ['Bahasa Melayu', 'Malay'],
        ['中文', 'Chinese']
      ] as const) {
        test(`given ${label}, then the product form does not scroll sideways or clip`, async ({
          page,
          signIn,
          shell
        }) => {
          await signIn('admin');
          if (language !== 'English') {
            await shell.setLanguage(language);
          }
          await goTo(page, `/products/${data.productId}`);
          await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
          await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
          const sideways = await page.evaluate(
            () => document.documentElement.scrollWidth - window.innerWidth
          );
          expect(sideways).toBeLessThanOrEqual(0);
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
          if (language !== 'English') {
            await shell.setLanguage('English');
          }
        });
      }
    });

    for (const scheme of ['light', 'dark'] as const) {
      test.describe(`axe, ${scheme}`, () => {
        test.use({ colorScheme: scheme, viewport: { width: 360, height: 740 } });

        test(`given the product form and the partner form, then axe finds no serious or critical violation (${scheme})`, async ({
          page,
          signIn
        }) => {
          await signIn('admin');
          const scan = async (name: string) => {
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
                v => `${name} ${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(' | ')}`
              )
            ).toEqual([]);
          };

          await goTo(page, `/products/${data.productId}`);
          await expect(page.getByLabel('HS code')).toBeVisible();
          await scan('product');

          await goTo(page, '/partners');
          await page.getByRole('searchbox').fill(data.partner);
          await page.getByRole('button', { name: new RegExp(`^Edit ${data.partner}`) }).click();
          await expect(page.getByRole('dialog').getByLabel('TIN')).toBeVisible();
          await scan('partner');
        });
      });
    }
  });

  test('given the Admin turns accounting off with a reason, then the tax fields go from both forms and the stored values are not shown', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await goTo(page, '/settings/modules');
    await expect(moduleSwitch(page)).toHaveAttribute('aria-checked', 'true');

    await moduleSwitch(page).click();
    const dialog = page.getByRole('dialog', { name: 'Turn off Accounting?' });
    await dialog.getByRole('textbox').fill(REASON_OFF);
    await dialog.getByRole('button', { name: 'Turn off' }).click();

    await expect(dialog).toBeHidden();
    await expect(moduleSwitch(page)).toHaveAttribute('aria-checked', 'false');
    await expect(shell.toast('Accounting is now off.')).toHaveCount(1);
    expect(await accountingIsOn()).toBe(false);

    await goTo(page, `/products/${data.productId}`);
    await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toBeVisible();
    await expect(page.getByLabel('HS code')).toHaveCount(0);
    await expect(page.getByLabel('SST code')).toHaveCount(0);

    await goTo(page, '/partners');
    await page.getByRole('button', { name: 'New partner' }).click();
    await expect(
      page.getByRole('dialog').getByRole('textbox', { name: 'Name', exact: true })
    ).toBeVisible();
    await expect(page.getByRole('dialog').getByLabel('TIN')).toHaveCount(0);
    await page.keyboard.press('Escape');

    await goTo(page, '/products/import');
    await expect(page.getByText(/Optional columns for accounting/)).toHaveCount(0);

    // The API says the same: while the module is off the stored codes are left out of an answer, and not lost.
    const stored = await admin().call<ProductRow>('GET', `/api/products/${data.productId}`);
    expect(stored.hsCode).toBeUndefined();
    expect(stored.sstCode).toBeUndefined();
  });
});
