import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import { SLOW } from '../src/purchasing';
import {
  CUSTOMER_PREFIX,
  NEEDS_APPROVAL,
  NO_APPROVAL,
  SKU_PREFIX,
  cancelOpenSalesOrders,
  createSalesOrder,
  getSalesOrder,
  salesCommand,
  salesIsOn,
  setSales,
  type ApiSalesOrder
} from '../src/sales';
import { expect, openSession, test } from '../src/fixtures';
import { goTo, loadPage } from '../src/pages/shell';

// Sales orders on the real API (PL3.8): the menu, the route guards, the list (filters, "Waiting for my approval"), the detail (lines
// with delivered and remaining, the buttons the API lists), the approve flow, and the rule that a holder of so.read only sees no
// selling price anywhere. The module is read first and put back last (also after a failure). Every order is a throw-away one that
// is cancelled again. One scenario after the other (serial).
test.describe.configure({ mode: 'serial' });

/** A money figure on this app is written with two decimals ("6,000.00"); quantities never are. */
const MONEY = /\d[\d,]*\.\d{2}\b/;
const REASON_ON = 'E2E: sales orders';
const token = unique();
const data = {
  foundOn: false,
  customer: { id: '' as PublicId, name: `${CUSTOMER_PREFIX}${token}` },
  otherCustomer: { id: '' as PublicId, name: `${CUSTOMER_PREFIX}B${token}` },
  productId: '' as PublicId,
  sku: `${SKU_PREFIX}${token}`
};
const admin = () => apiAs('admin');
const moduleSwitch = (page: Page) => page.getByRole('switch', { name: 'Sales' });
const status = (page: Page) => page.locator('app-page-header app-status-chip').first();
const row = (page: Page, no: string) => page.getByRole('row').filter({ hasText: no });
const bar = (page: Page, name: string) => page.getByRole('button', { name, exact: true });

/** The buttons of the action bar, as a person reads them. */
const barButtons = async (page: Page): Promise<string[]> =>
  (await page.locator('.so-detail-bar').getByRole('button').allInnerTexts()).map(text =>
    text.replace(/\s+/g, ' ').trim()
  );

/** What the API lists, in the order of the bar. Edit and Deliver are left out on purpose: their screens are PL3.9 and PL3.10. */
const offered: [string, string][] = [
  ['cancel', 'Cancel order'],
  ['reject', 'Reject'],
  ['close', 'Close short'],
  ['recall', 'Back to draft'],
  ['submit', 'Submit'],
  ['approve', 'Approve']
];
async function expectedButtons(
  order: ApiSalesOrder,
  role: 'admin' | 'clerk' | 'supervisor' | 'owner' | 'staff'
): Promise<string[]> {
  const { allowedActions } = await getSalesOrder(order.id, role);
  return offered.filter(([action]) => allowedActions.includes(action)).map(([, label]) => label);
}

test.describe('Sales orders, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await salesIsOn();
    // The first scenario switches the module on from the screen: start from off (refused with 409 while an order is open).
    await cancelOpenSalesOrders().catch(() => undefined);
    await setSales(false, 'E2E: start from off');
    for (const customer of [data.customer, data.otherCustomer]) {
      customer.id = (await admin().createPartner('Customer', { name: customer.name })).id;
    }
    data.productId = (await admin().createProduct({ sku: data.sku })).id;
  });

  test.afterAll(async () => {
    try {
      if (await salesIsOn()) {
        await cancelOpenSalesOrders();
      }
    } finally {
      await setSales(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given sales is off, then there is no menu entry and the address is the not-found page; the Admin turns it on with a reason and the entry appears', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Sales orders');
    await loadPage(page, '/sales-orders');
    await expect(page).toHaveURL(/\/not-found$/);
    expect((await admin().raw('GET', '/api/sales-orders')).status).toBe(404);

    await goTo(page, '/settings/modules');
    await moduleSwitch(page).click();
    const dialog = page.getByRole('dialog', { name: 'Turn on Sales?' });
    await dialog.getByRole('textbox').fill(REASON_ON);
    await dialog.getByRole('button', { name: 'Turn on' }).click();
    await expect(dialog).toBeHidden({ timeout: SLOW });
    await expect(moduleSwitch(page)).toHaveAttribute('aria-checked', 'true', {
      timeout: SLOW
    });

    expect(await salesIsOn()).toBe(true);
    expect(await shell.menu()).toContain('Sales orders');
    await shell.nav.getByRole('link', { name: 'Sales orders' }).click();
    await expect(page).toHaveURL(/\/sales-orders$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Sales orders' })).toBeVisible();
  });

  test('given the module is on, then every role that holds so.read has the entry in its menu', async ({
    browser
  }) => {
    for (const role of ['clerk', 'supervisor', 'staff', 'owner'] as const) {
      const session = await openSession(browser, role);
      try {
        expect(await session.shell.menu()).toContain('Sales orders');
      } finally {
        await session.context.close();
      }
    }
  });

  test('given orders of two customers, then the list filters by status, customer, number and "Waiting for my approval", and an approver finds the Submitted one marked', async ({
    page,
    signIn,
    shell
  }) => {
    const waiting = await createSalesOrder(data.customer.id, data.productId, NEEDS_APPROVAL);
    await salesCommand('clerk', waiting.id, 'submit');
    const draft = await createSalesOrder(
      data.otherCustomer.id,
      data.productId,
      NEEDS_APPROVAL,
      'E2E draft'
    );
    const released = await createSalesOrder(data.customer.id, data.productId, NO_APPROVAL);
    await salesCommand('clerk', released.id, 'submit');

    await signIn('supervisor');
    await shell.nav.getByRole('link', { name: 'Sales orders' }).click();
    for (const order of [waiting, draft, released]) {
      await expect(row(page, order.no)).toBeVisible();
    }
    await expect(row(page, waiting.no)).toContainText(data.customer.name);
    await expect(row(page, waiting.no)).toContainText('Main warehouse');
    await expect(row(page, waiting.no)).toContainText('6,000.00');
    await expect(row(page, waiting.no)).toContainText('Waiting for you');
    await expect(row(page, draft.no)).toContainText('Draft');
    await expect(row(page, released.no)).toContainText('Approved');

    await page.getByRole('combobox', { name: 'Status' }).selectOption({ label: 'Submitted' });
    await expect(row(page, waiting.no)).toBeVisible();
    await expect(row(page, draft.no)).toHaveCount(0);
    await expect(row(page, released.no)).toHaveCount(0);

    await page.getByRole('combobox', { name: 'Status' }).selectOption({ label: 'Any' });
    await page
      .getByRole('combobox', { name: 'Customer' })
      .selectOption({ label: data.otherCustomer.name });
    await expect(row(page, draft.no)).toBeVisible();
    await expect(row(page, waiting.no)).toHaveCount(0);

    await page.getByRole('combobox', { name: 'Customer' }).selectOption({ label: 'All' });
    await page.getByRole('checkbox', { name: 'Waiting for my approval' }).check();
    await expect(row(page, waiting.no)).toBeVisible();
    await expect(row(page, draft.no)).toHaveCount(0);
    await expect(row(page, released.no)).toHaveCount(0);
    await page.getByRole('checkbox', { name: 'Waiting for my approval' }).uncheck();

    await page.getByRole('searchbox', { name: 'Search sales orders' }).fill(released.no);
    await expect(row(page, released.no)).toBeVisible();
    await expect(row(page, waiting.no)).toHaveCount(0);

    await page.getByRole('searchbox', { name: 'Search sales orders' }).fill('');
    await page.getByRole('textbox', { name: /^From/ }).fill('01/01/2099');
    await expect(page.getByText('No sales orders match these filters.')).toBeVisible();

    // The submitter is not the reviewer: the Clerk sees the order without the mark.
    const clerk = await openSession(page.context().browser()!, 'clerk');
    try {
      await goTo(clerk.page, '/sales-orders');
      await expect(row(clerk.page, waiting.no)).toBeVisible();
      await expect(row(clerk.page, waiting.no)).not.toContainText('Waiting for you');
    } finally {
      await clerk.context.close();
    }
    for (const order of [waiting, draft, released]) {
      await salesCommand('supervisor', order.id, 'cancel', {
        reason: 'E2E: throw-away order'
      }).catch(() =>
        salesCommand('clerk', order.id, 'cancel', {
          reason: 'E2E: throw-away order'
        })
      );
    }
  });

  test('given a submitted order, then the detail shows header, customer, warehouse, remark, lines with ordered, delivered and remaining, total and history', async ({
    page,
    signIn,
    shell
  }) => {
    const order = await createSalesOrder(
      data.customer.id,
      data.productId,
      NEEDS_APPROVAL,
      'Deliver to bay 3'
    );
    await salesCommand('clerk', order.id, 'submit');

    await signIn('supervisor');
    await shell.nav.getByRole('link', { name: 'Sales orders' }).click();
    await row(page, order.no).getByRole('link', { name: order.no }).click();
    await expect(page).toHaveURL(new RegExp(`/sales-orders/${order.id}$`));
    await expect(page.getByRole('heading', { level: 1, name: order.no })).toBeVisible();
    await expect(status(page)).toHaveText('Submitted');
    const main = page.getByRole('main');
    await expect(main).toContainText(data.customer.name);
    await expect(main).toContainText('Main warehouse');
    await expect(main).toContainText('Deliver to bay 3');
    const line = page.getByRole('row').filter({ hasText: data.sku });
    await expect(line).toContainText('10 PCS');
    // The cells in order: SKU, name, ordered, delivered, remaining.
    await expect(line.getByRole('cell').nth(3)).toHaveText('0 PCS');
    await expect(line.getByRole('cell').nth(4)).toHaveText('10 PCS');
    await expect(line).toContainText('600.00');
    await expect(main).toContainText('6,000.00');
    await expect(page.getByRole('heading', { name: 'Why this needs approval' })).toBeVisible();
    await expect(page.getByRole('listitem').filter({ hasText: 'Submitted' })).toBeVisible();
    expect(await barButtons(page)).toEqual(await expectedButtons(order, 'supervisor'));

    await salesCommand('clerk', order.id, 'cancel');
  });

  test('given a Draft, when it is submitted, rejected with a reason, submitted again, approved and cancelled, then each step shows its result and the buttons follow the API', async ({
    page,
    signIn,
    shell
  }) => {
    const order = await createSalesOrder(data.customer.id, data.productId, NEEDS_APPROVAL);
    const clerk = await openSession(page.context().browser()!, 'clerk');
    try {
      await goTo(clerk.page, `/sales-orders/${order.id}`);
      await expect(status(clerk.page)).toHaveText('Draft');
      expect(await barButtons(clerk.page)).toEqual(await expectedButtons(order, 'clerk'));
      await clerk.page.getByRole('button', { name: 'Submit', exact: true }).click();
      await expect(status(clerk.page)).toHaveText('Submitted');
      expect(await barButtons(clerk.page)).toEqual(await expectedButtons(order, 'clerk'));

      await signIn('supervisor');
      await goTo(page, `/sales-orders/${order.id}`);
      await bar(page, 'Reject').click();
      const dialog = shell.dialog(`Reject ${order.no}?`);
      await dialog.getByRole('textbox').fill('abc');
      await dialog.getByRole('button', { name: 'Reject', exact: true }).click();
      await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
      expect((await getSalesOrder(order.id)).status).toBe('Submitted');
      await dialog.getByRole('textbox').fill('The price is too low');
      await dialog.getByRole('button', { name: 'Reject', exact: true }).click();
      await expect(status(page)).toHaveText('Draft');
      await expect(
        page.locator('app-banner').filter({ hasText: 'The price is too low' })
      ).toBeVisible();
      await expect(bar(page, 'Approve')).toHaveCount(0);

      await salesCommand('clerk', order.id, 'submit');
      await page.reload();
      await bar(page, 'Approve').click();
      await shell
        .dialog(`Approve ${order.no}?`)
        .getByRole('button', { name: 'Approve', exact: true })
        .click();
      await expect(status(page)).toHaveText('Approved');
      await expect(shell.toast('Approved.')).toHaveCount(1);
      expect(await barButtons(page)).toEqual(await expectedButtons(order, 'supervisor'));

      await bar(page, 'Cancel order').click();
      const cancel = shell.dialog(`Cancel ${order.no}?`);
      await cancel.getByRole('textbox').fill('abc');
      await cancel.getByRole('button', { name: 'Cancel order', exact: true }).click();
      await expect(cancel.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
      await cancel.getByRole('textbox').fill('Customer withdrew the order');
      await cancel.getByRole('button', { name: 'Cancel order', exact: true }).click();
      await expect(status(page)).toHaveText('Cancelled');
      await expect(
        page.locator('app-banner').filter({ hasText: 'Customer withdrew the order' })
      ).toBeVisible();
      await expect(page.locator('.so-detail-bar')).toHaveCount(0);
    } finally {
      await clerk.context.close();
    }
  });

  test('given Warehouse staff (so.read only), then no money shows on the list or the detail, and the API leaves the keys out', async ({
    page,
    signIn
  }) => {
    const order = await createSalesOrder(data.customer.id, data.productId, NEEDS_APPROVAL);
    const asStaff = await getSalesOrder(order.id, 'staff');
    expect(asStaff).not.toHaveProperty('totalAmount');
    expect(asStaff.lines[0]).not.toHaveProperty('unitPrice');
    expect(asStaff.lines[0]).not.toHaveProperty('amount');

    await signIn('staff');
    await goTo(page, '/sales-orders');
    await expect(row(page, order.no)).toBeVisible();
    await expect(page.getByRole('columnheader', { name: /^Amount/ })).toHaveCount(0);
    await expect(page.getByRole('main')).not.toContainText(MONEY);
    await goTo(page, `/sales-orders/${order.id}`);
    await expect(page.getByRole('heading', { level: 1, name: order.no })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: data.sku })).toContainText('10 PCS');
    await expect(page.getByRole('main')).not.toContainText(MONEY);
    await expect(page.getByRole('main')).not.toContainText('RM');
    await expect(page.getByRole('columnheader', { name: /price|amount/i })).toHaveCount(0);
    expect(await barButtons(page)).toEqual([]);
    await salesCommand('clerk', order.id, 'cancel');
  });

  test.describe('layout and accessibility', () => {
    let waiting: ApiSalesOrder;
    test.beforeAll(async () => {
      waiting = await createSalesOrder(data.customer.id, data.productId, NEEDS_APPROVAL);
      await salesCommand('clerk', waiting.id, 'submit');
    });

    test.describe('360 px phone', () => {
      test.use({ viewport: { width: 360, height: 740 } });

      test('given a phone, then the list is cards and neither page scrolls sideways', async ({
        page,
        signIn
      }) => {
        await signIn('supervisor');
        await goTo(page, '/sales-orders');
        const card = page.locator('app-table .ui-table-row').filter({ hasText: waiting.no });
        await expect(card).toBeVisible();
        expect((await card.boundingBox())?.height).toBeLessThan(200);
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        ).toBeLessThanOrEqual(0);
        await goTo(page, `/sales-orders/${waiting.id}`);
        await expect(status(page)).toHaveText('Submitted');
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        ).toBeLessThanOrEqual(0);
        await expect(bar(page, 'Approve')).toBeInViewport();
      });
    });

    for (const scheme of ['light', 'dark'] as const) {
      for (const width of [360, 1280]) {
        test.describe(`axe, ${scheme}, ${width} px`, () => {
          test.use({ colorScheme: scheme, viewport: { width, height: 740 } });

          test(`given the list and the detail, then axe finds no serious or critical violation (${scheme}, ${width})`, async ({
            page,
            signIn
          }) => {
            await signIn('supervisor');
            for (const path of ['/sales-orders', `/sales-orders/${waiting.id}`]) {
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
    await cancelOpenSalesOrders();
    await setSales(false, 'E2E: sales orders over');
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Sales orders');
    await loadPage(page, '/sales-orders');
    await expect(page).toHaveURL(/\/not-found$/);
    expect((await admin().raw('GET', '/api/sales-orders')).status).toBe(404);
  });
});
