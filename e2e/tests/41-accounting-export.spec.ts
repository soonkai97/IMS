import { readFileSync } from 'node:fs';
import AxeBuilder from '@axe-core/playwright';
import { apiAs, unique, type PublicId } from '../src/api';
import {
  PARTNER_PREFIX,
  SKU_PREFIX,
  accountingIsOn,
  retireThrowAways,
  setAccounting,
  today
} from '../src/accounting';
import { expect, test } from '../src/fixtures';
import { goTo, loadPage } from '../src/pages/shell';
import { readFirstSheet } from '../src/xlsx';

// The accounting export screen on the real API (PL1.8b): Reports > Accounting export for a person who holds `accounting.export` while the
// module is on. The module is switched on through the API (the switch itself is `40-accounting`), a throw-away product with codes, a
// throw-away partner with tax numbers and one posted receipt are made through the API, and the file that the screen downloads is read.
// One scenario after the other (serial).
//
// The installation is left as it was found: the module state is read first and put back last (also after a failure), the receipt is
// reversed (stock ends where it started; the posted documents stay in the history, as every posting does), and the product and the
// partner are deactivated with their tax values cleared.
test.describe.configure({ mode: 'serial' });

const HS_CODE = '0901.21';
const SST_CODE = 'ST-01';
const TIN = 'C2026E2E02';
const SST_REG_NO = 'W10-1808-32000002';
const EXPORT_COLUMNS = [
  'Date',
  'Document no.',
  'Type',
  'Purpose',
  'Reversal of',
  'Warehouse',
  'Partner',
  'Partner TIN',
  'Partner SST reg. no.',
  'SKU',
  'Product',
  'HS code',
  'SST code',
  'Unit',
  'Quantity',
  'Unit cost',
  'Amount'
];
const CAP_SENTENCE =
  'Too many rows: one accounting file holds at most 50000 rows. Narrow the date range (for example one month at a time) or pick one warehouse.';

const token = unique();
const data = {
  sku: `${SKU_PREFIX}X${token}`,
  partner: `${PARTNER_PREFIX}X${token}`,
  receiptId: undefined as PublicId | undefined,
  receiptNo: undefined as string | undefined,
  foundOn: false
};

const admin = () => apiAs('admin');
const DOWNLOAD = 'Download Excel file';

test.describe('Accounting export, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await accountingIsOn();
    await setAccounting(true, 'E2E: accounting export');
    const units = await admin().call<{ id: PublicId; code: string }[]>('GET', '/api/units');
    const product = await admin().call<{ id: PublicId }>('POST', '/api/products', {
      sku: data.sku,
      name: `E2E Export Tea ${token}`,
      categoryId: null,
      baseUomId: (units.find(unit => unit.code === 'PCS') ?? units[0])?.id,
      safetyStock: 0,
      isActive: true,
      barcodes: [],
      conversions: [],
      hsCode: HS_CODE,
      sstCode: SST_CODE
    });
    const partner = await admin().call<{ id: PublicId }>('POST', '/api/partners', {
      type: 'Supplier',
      name: data.partner,
      contact: null,
      isActive: true,
      tin: TIN,
      sstRegNo: SST_REG_NO
    });
    const receipt = await admin().post(
      'receipts',
      [{ productId: product.id, qty: 10, unitCost: 5 }],
      {
        partnerId: partner.id
      }
    );
    data.receiptId = receipt.id;
    data.receiptNo = receipt.no;
  });

  test.afterAll(async () => {
    try {
      await setAccounting(true, 'E2E: clean up');
      if (data.receiptId) {
        await admin().raw('POST', `/api/documents/${data.receiptId}/reverse`, {
          reason: 'E2E: throw-away receipt'
        });
      }
      await retireThrowAways();
    } finally {
      await setAccounting(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given a posted receipt, then the Accounting export entry opens the screen and the download is one file whose row carries the product and partner codes', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Accounting export' }).click();
    await expect(page).toHaveURL(/\/reports\/accounting$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Accounting export' })).toBeVisible();
    // Reports is not the current item here; Accounting export is.
    await expect(shell.nav.getByRole('link', { name: 'Accounting export' })).toHaveAttribute(
      'aria-current',
      'page'
    );
    await expect(shell.nav.getByRole('link', { name: 'Reports' })).not.toHaveAttribute(
      'aria-current',
      'page'
    );
    await expect(page.getByText('The layout is fixed')).toBeVisible();
    await expect(page.getByText('Amounts are at cost.')).toBeVisible();
    await expect(page.getByText('Reversals appear as negative rows')).toBeVisible();

    // An empty period sends nothing and says which date is missing; the focus goes to the first one.
    await page.getByRole('button', { name: DOWNLOAD }).click();
    await expect(page.getByText('Enter the first date of the period.')).toBeVisible();
    await expect(page.getByText('Enter the last date of the period.')).toBeVisible();
    await expect(page.getByRole('textbox', { name: /^From/ })).toBeFocused();

    await page.getByRole('textbox', { name: /^From/ }).fill(today());
    await page.getByRole('textbox', { name: /^To/ }).fill(today());
    // Double click: one request, one file.
    let requests = 0;
    page.on('request', request => {
      if (request.url().includes('/api/accounting/export')) {
        requests++;
      }
    });
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: DOWNLOAD }).dblclick()
    ]);

    expect(download.suggestedFilename()).toMatch(
      /^ims-accounting-\d{4}-\d{2}-\d{2}-\d{4}-\d{2}-\d{2}\.xlsx$/
    );
    await expect(page.getByText(`Downloaded ${download.suggestedFilename()}.`)).toBeVisible();
    expect(requests).toBe(1);
    const sheet = readFirstSheet(readFileSync(await download.path()));
    expect(sheet.name).toBe('Accounting');
    expect(sheet.rows[0]).toEqual(EXPORT_COLUMNS);
    const row = sheet.rows.find(cells => cells[EXPORT_COLUMNS.indexOf('SKU')] === data.sku);
    expect(row, 'a row for the SKU of this run').toBeDefined();
    const cell = (name: string) => (row as string[])[EXPORT_COLUMNS.indexOf(name)];
    expect(cell('Document no.')).toBe(data.receiptNo);
    expect(cell('Partner')).toBe(data.partner);
    expect(cell('Partner TIN')).toBe(TIN);
    expect(cell('Partner SST reg. no.')).toBe(SST_REG_NO);
    expect(cell('HS code')).toBe(HS_CODE);
    expect(cell('SST code')).toBe(SST_CODE);
    expect(Number(cell('Quantity'))).toBe(10);
    expect(Number(cell('Amount'))).toBe(50);
  });

  test('given the API refuses a period with too many rows, then the sentence is shown once in an alert, no file is saved and the button is usable again', async ({
    page,
    signIn
  }) => {
    await page.route('**/api/accounting/export*', route =>
      route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          title: 'Bad Request',
          status: 400,
          detail: CAP_SENTENCE
        })
      })
    );
    await signIn('admin');
    await goTo(page, '/reports/accounting');
    await page.getByRole('textbox', { name: /^From/ }).fill('01/01/2026');
    await page.getByRole('textbox', { name: /^To/ }).fill('31/12/2026');
    let downloads = 0;
    page.on('download', () => downloads++);

    await page.getByRole('button', { name: DOWNLOAD }).click();

    await expect(page.getByRole('alert').filter({ hasText: 'Narrow the date range' })).toHaveCount(
      1
    );
    await expect(page.locator('app-toast')).toHaveCount(0);
    await expect(page.getByRole('button', { name: DOWNLOAD })).toBeEnabled();
    await expect(page.getByRole('button', { name: DOWNLOAD })).not.toHaveAttribute(
      'aria-busy',
      'true'
    );
    expect(downloads).toBe(0);
  });

  test('given a period whose first date is after the last, then it says so under To and sends nothing', async ({
    page,
    signIn
  }) => {
    let requests = 0;
    page.on('request', request => {
      if (request.url().includes('/api/accounting/export')) {
        requests++;
      }
    });
    await signIn('admin');
    await goTo(page, '/reports/accounting');

    await page.getByRole('textbox', { name: /^From/ }).fill('31/10/2026');
    await page.getByRole('textbox', { name: /^To/ }).fill('01/10/2026');
    await page.getByRole('button', { name: DOWNLOAD }).click();

    await expect(page.getByText('From must be on or before To.')).toBeVisible();
    expect(requests).toBe(0);
  });

  for (const role of ['supervisor', 'staff'] as const) {
    test(`given the module is on, then a ${role} (no accounting.export) has no Accounting export in the menu and a typed address is the forbidden page`, async ({
      page,
      signIn,
      shell
    }) => {
      await signIn(role);
      expect(await shell.menu()).not.toContain('Accounting export');

      await loadPage(page, '/reports/accounting');

      await expect(page).toHaveURL(/\/forbidden$/);
    });
  }

  test('given an Owner (holds accounting.export), then Accounting export is in the menu', async ({
    signIn,
    shell
  }) => {
    await signIn('owner');

    expect(await shell.menu()).toContain('Accounting export');
  });

  test.describe('layout and accessibility', () => {
    test.describe('320 px with the text at 200%', () => {
      test.use({ viewport: { width: 320, height: 640 } });

      for (const [language, label] of [
        ['English', 'English'],
        ['Bahasa Melayu', 'Malay'],
        ['中文', 'Chinese']
      ] as const) {
        test(`given ${label}, then the export screen does not scroll sideways or clip`, async ({
          page,
          signIn,
          shell
        }) => {
          await signIn('admin');
          if (language !== 'English') {
            await shell.setLanguage(language);
          }
          await goTo(page, '/reports/accounting');
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
        test.use({
          colorScheme: scheme,
          viewport: { width: 360, height: 740 }
        });

        test(`given the export screen, with and without an error showing, then axe finds no serious or critical violation (${scheme})`, async ({
          page,
          signIn
        }) => {
          await page.route('**/api/accounting/export*', route =>
            route.fulfill({
              status: 400,
              contentType: 'application/problem+json',
              body: JSON.stringify({
                title: 'Bad Request',
                status: 400,
                detail: CAP_SENTENCE
              })
            })
          );
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

          await goTo(page, '/reports/accounting');
          await expect(page.getByRole('button', { name: DOWNLOAD })).toBeVisible();
          await scan('export');

          await page.getByRole('textbox', { name: /^From/ }).fill('01/01/2026');
          await page.getByRole('textbox', { name: /^To/ }).fill('31/12/2026');
          await page.getByRole('button', { name: DOWNLOAD }).click();
          await expect(
            page.getByRole('alert').filter({ hasText: 'Narrow the date range' })
          ).toBeVisible();
          await scan('export with error');
        });
      });
    }
  });

  test('given the module is switched off, then the menu entry goes, the address is the not-found page and the API answers 404', async ({
    page,
    signIn,
    shell
  }) => {
    await setAccounting(false, 'E2E: accounting export over');
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Accounting export');

    await loadPage(page, '/reports/accounting');
    await expect(page).toHaveURL(/\/not-found$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible();

    expect(
      (await admin().raw('GET', '/api/accounting/export?From=2026-10-01&To=2026-10-31')).status
    ).toBe(404);
  });
});
