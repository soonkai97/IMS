import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import {
  LARGE,
  NEEDS_APPROVAL,
  NO_APPROVAL,
  SKU_PREFIX,
  SLOW,
  SUPPLIER_PREFIX,
  approvedOrder,
  cancelOpenOrders,
  getOrder,
  command,
  createOrder,
  purchasingIsOn,
  receive,
  retireReceipt,
  setPurchasing,
  type ApiOrder
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
  sku: `${SKU_PREFIX}${token}`,
  receiptId: undefined as PublicId | undefined,
  partOrder: undefined as { id: PublicId; receiptNo: string; receiptId: PublicId } | undefined
};
const admin = () => apiAs('admin');
const moduleSwitch = (page: Page) => page.getByRole('switch', { name: 'Purchasing' });
const status = (page: Page) => page.locator('app-page-header app-status-chip').first();
const row = (page: Page, no: string) => page.getByRole('row').filter({ hasText: no });

const bar = (page: Page, name: string) => page.getByRole('button', { name, exact: true });
/** The buttons of the action bar, as a person reads them. */
const barButtons = async (page: Page): Promise<string[]> =>
  (await page.locator('.po-detail-bar').getByRole('button').allInnerTexts()).map(text =>
    text.replace(/\s+/g, ' ').trim()
  );

/** What each role is offered, from the API itself, in the order of the bar: the screen must show exactly this and nothing else. */
const offered: [string, string][] = [
  ['cancel', 'Cancel order'],
  ['reject', 'Reject'],
  ['close', 'Close short'],
  ['recall', 'Back to draft'],
  ['submit', 'Submit'],
  ['approve', 'Approve']
];
async function expectedButtons(
  order: ApiOrder,
  role: 'admin' | 'clerk' | 'supervisor' | 'owner' | 'staff'
): Promise<string[]> {
  const { allowedActions } = await getOrder(order.id, role);
  return offered.filter(([action]) => allowedActions.includes(action)).map(([, label]) => label);
}

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
      if (data.receiptId) {
        await retireReceipt(data.receiptId);
      }
      if (await purchasingIsOn()) {
        await cancelOpenOrders();
      }
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

  test('given a submitted order, then the detail shows header, status, supplier, warehouse, remark, lines, total and history, and the list row opens it', async ({
    page,
    signIn,
    shell
  }) => {
    const order = await createOrder(
      data.supplier.id,
      data.productId,
      NEEDS_APPROVAL,
      'Deliver to bay 3'
    );
    await command('clerk', order.id, 'submit');

    await signIn('supervisor');
    await shell.nav.getByRole('link', { name: 'Purchase orders' }).click();
    await row(page, order.no).getByRole('link', { name: order.no }).click();
    await expect(page).toHaveURL(new RegExp(`/purchase-orders/${order.id}$`));
    await expect(page.getByRole('heading', { level: 1, name: order.no })).toBeVisible();
    await expect(status(page)).toHaveText('Submitted');
    const main = page.getByRole('main');
    await expect(main).toContainText(data.supplier.name);
    await expect(main).toContainText('Main warehouse');
    await expect(main).toContainText('Deliver to bay 3');
    const line = page.getByRole('row').filter({ hasText: data.sku });
    await expect(line).toContainText('10 PCS');
    // The cells in order: SKU, name, ordered, received, remaining.
    await expect(line.getByRole('cell').nth(3)).toHaveText('0 PCS');
    await expect(page.locator('app-banner')).toHaveCount(0);
    await expect(line).toContainText('600.00');
    await expect(main).toContainText('6,000.00');
    await expect(page.getByRole('heading', { name: 'History' })).toBeVisible();
    await expect(page.getByRole('listitem').filter({ hasText: 'Submitted' })).toBeVisible();

    await command('clerk', order.id, 'cancel');
  });

  test('given a Standard-tier and a Large-tier order, then Approve is offered by the API to the Supervisor for the first and to the Owner for the second, and the bar shows exactly what it offers', async ({
    browser
  }) => {
    const standard = await createOrder(data.supplier.id, data.productId, NEEDS_APPROVAL);
    const large = await createOrder(data.supplier.id, data.productId, LARGE);
    for (const order of [standard, large]) {
      await command('clerk', order.id, 'submit');
    }
    const cases = [
      ['supervisor', standard, true],
      ['owner', standard, false],
      ['supervisor', large, false],
      ['owner', large, true],
      ['clerk', standard, false],
      ['staff', standard, false]
    ] as const;
    for (const [role, order, approves] of cases) {
      const session = await openSession(browser, role);
      try {
        await goTo(session.page, `/purchase-orders/${order.id}`);
        await expect(session.page.getByRole('heading', { level: 1, name: order.no })).toBeVisible();
        const buttons = await barButtons(session.page);
        expect(buttons, `${role} on ${order.no}`).toEqual(await expectedButtons(order, role));
        expect(buttons.includes('Approve'), `${role} on ${order.no}`).toBe(approves);
      } finally {
        await session.context.close();
      }
    }
    for (const order of [standard, large]) {
      await command('clerk', order.id, 'cancel');
    }
  });

  test('given a Supervisor who submitted an order, then Approve is not offered to that Supervisor (self-approval) and the bar shows what the API offers', async ({
    page,
    signIn
  }) => {
    const order = await createOrder(
      data.supplier.id,
      data.productId,
      NEEDS_APPROVAL,
      'mine',
      'supervisor'
    );
    await command('supervisor', order.id, 'submit');

    await signIn('supervisor');
    await goTo(page, `/purchase-orders/${order.id}`);
    await expect(page.getByRole('heading', { level: 1, name: order.no })).toBeVisible();
    expect(await barButtons(page)).toEqual(await expectedButtons(order, 'supervisor'));
    expect(await barButtons(page)).not.toContain('Approve');
    await command('supervisor', order.id, 'cancel');
  });

  test('given a Draft, when it is submitted, rejected with a reason, submitted again, approved and cancelled, then each step shows its result and the buttons follow the API', async ({
    page,
    signIn,
    shell
  }) => {
    const order = await createOrder(data.supplier.id, data.productId, NEEDS_APPROVAL);
    const clerk = await openSession(page.context().browser()!, 'clerk');
    await goTo(clerk.page, `/purchase-orders/${order.id}`);
    await expect(status(clerk.page)).toHaveText('Draft');
    expect(await barButtons(clerk.page)).toEqual(await expectedButtons(order, 'clerk'));

    const submit = clerk.page.getByRole('button', { name: 'Submit', exact: true });
    await submit.dblclick();
    await expect(clerk.shell.toast('Submitted for approval.')).toHaveCount(1);
    await expect(status(clerk.page)).toHaveText('Submitted');

    await signIn('supervisor');
    await goTo(page, `/purchase-orders/${order.id}`);
    await bar(page, 'Reject').click();
    const dialog = shell.dialog(`Reject ${order.no}?`);
    await dialog.getByRole('textbox').fill('abc');
    await dialog.getByRole('button', { name: 'Reject', exact: true }).click();
    await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
    expect((await getOrder(order.id)).status).toBe('Submitted');
    await dialog.getByRole('textbox').fill('The price is too high');
    await dialog.getByRole('button', { name: 'Reject', exact: true }).click();
    await expect(status(page)).toHaveText('Draft');
    await expect(
      page.locator('app-banner').filter({ hasText: 'The price is too high' })
    ).toBeVisible();
    await expect(bar(page, 'Approve')).toHaveCount(0);

    await command('clerk', order.id, 'submit');
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
    await cancel.getByRole('textbox').fill('Supplier went out of business');
    await cancel.getByRole('button', { name: 'Cancel order', exact: true }).click();
    await expect(status(page)).toHaveText('Cancelled');
    await expect(
      page.locator('app-banner').filter({ hasText: 'Supplier went out of business' })
    ).toBeVisible();
    await expect(page.locator('.po-detail-bar')).toHaveCount(0);
    await clerk.context.close();
  });

  test('given an order changed by someone else, when a button is used, then one banner says so, the buttons are locked, and Reload shows the new state', async ({
    page,
    signIn
  }) => {
    const order = await createOrder(data.supplier.id, data.productId, NEEDS_APPROVAL);
    await command('clerk', order.id, 'submit');
    await signIn('clerk');
    await goTo(page, `/purchase-orders/${order.id}`);
    await expect(status(page)).toHaveText('Submitted');

    await command('supervisor', order.id, 'approve');
    await bar(page, 'Back to draft').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Back to draft' }).click();

    const banner = page
      .locator('app-banner')
      .filter({ hasText: `${order.no} changed since you opened it` });
    await expect(banner).toBeVisible();
    await expect(page.locator('app-toast')).toHaveCount(0);
    await expect(bar(page, 'Cancel order')).toBeDisabled();
    await banner.getByRole('button', { name: 'Reload' }).click();
    await expect(status(page)).toHaveText('Approved');
    await expect(banner).toHaveCount(0);
    await command('supervisor', order.id, 'cancel', { reason: 'E2E: throw-away order' });
  });

  test('given an order that is part received, then ordered, received and remaining show per line with the receipt linked, and Close short with a reason closes it', async ({
    page,
    signIn,
    shell
  }) => {
    const order = await approvedOrder(data.supplier.id, data.productId);
    const receipt = await receive(order, 6, id => (data.receiptId = id));
    data.partOrder = { id: order.id, receiptNo: receipt.no, receiptId: receipt.id };

    await signIn('supervisor');
    await goTo(page, `/purchase-orders/${order.id}`);
    const line = page.getByRole('row').filter({ hasText: data.sku });
    // The cells in order: SKU, name, ordered, received, remaining.
    await expect(line.getByRole('cell').nth(2)).toHaveText('10 PCS');
    await expect(line.getByRole('cell').nth(3)).toHaveText('6 PCS');
    await expect(line.getByRole('cell').nth(4)).toHaveText('4 PCS');
    await expect(page.getByRole('link', { name: receipt.no })).toHaveAttribute(
      'href',
      `/receipts/${receipt.id}`
    );
    // Close short is offered only because something was received.
    expect(await barButtons(page)).toEqual(await expectedButtons(order, 'supervisor'));
    expect(await barButtons(page)).toContain('Close short');

    await bar(page, 'Close short').click();
    const dialog = shell.dialog(`Close ${order.no} short?`);
    await dialog.getByRole('textbox').fill('abc');
    await dialog.getByRole('button', { name: 'Close short', exact: true }).click();
    await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
    await dialog.getByRole('textbox').fill('Supplier cannot send the rest');
    await dialog.getByRole('button', { name: 'Close short', exact: true }).click();

    await expect(status(page)).toHaveText('Closed');
    await expect(page.locator('.po-detail-bar')).toHaveCount(0);
    await expect(page.locator('app-banner').filter({ hasText: 'Closed short' })).toContainText(
      'Supplier cannot send the rest'
    );
  });

  test('given a user without cost.view (Warehouse staff), then the part-received order shows quantities and no price, amount or total', async ({
    page,
    signIn
  }) => {
    const { id, receiptNo, receiptId } = data.partOrder!;
    await signIn('staff');
    await goTo(page, `/purchase-orders/${id}`);
    const line = page.getByRole('row').filter({ hasText: data.sku });
    await expect(line.getByRole('cell').nth(3)).toHaveText('6 PCS');
    await expect(page.locator('.po-detail-bar')).toHaveCount(0);

    expect(await page.getByRole('columnheader').allInnerTexts()).not.toEqual(
      expect.arrayContaining([expect.stringMatching(/Unit price|Amount/)])
    );
    await expect(page.locator('dt', { hasText: 'Amount' })).toHaveCount(0);
    expect(await page.getByRole('main').innerText()).not.toMatch(/d+.d{2}/);
    // Warehouse staff hold stock.read, so the receipt is a link to the receipt screen.
    await expect(page.getByRole('link', { name: receiptNo })).toHaveAttribute(
      'href',
      `/receipts/${receiptId}`
    );
  });

  test('given an id that names no order, then the page is "Page not found" and a bookmark with a number asks the API for nothing', async ({
    page,
    signIn
  }) => {
    const calls: string[] = [];
    page.on('request', request => {
      if (request.url().includes('/api/purchase-orders/')) {
        calls.push(request.url());
      }
    });
    await signIn('admin');
    await goTo(page, '/purchase-orders/31');
    await expect(
      page.getByRole('alert').getByText("This page or document doesn't exist")
    ).toBeVisible();
    expect(calls).toEqual([]);

    await goTo(page, '/purchase-orders/00000000-0000-4000-8000-000000000000');
    await expect(
      page.getByRole('alert').getByText("This page or document doesn't exist")
    ).toBeVisible();
  });

  test.describe('layout and accessibility', () => {
    let order: { id: PublicId; no: string };
    test.beforeAll(async () => {
      order = await createOrder(data.supplier.id, data.productId, NEEDS_APPROVAL);
      await command('clerk', order.id, 'submit');
    });

    test.describe('360 px phone', () => {
      test.use({ viewport: { width: 360, height: 740 } });

      test('given a phone, then the list is cards and neither page scrolls sideways', async ({
        page,
        signIn
      }) => {
        await signIn('supervisor');
        await goTo(page, '/purchase-orders');
        // A card (number and status, then the date, supplier, warehouse and amount), not a row of a wide table: far under a screen high.
        const card = page.locator('app-table .ui-table-row').filter({ hasText: order.no });
        await expect(card).toBeVisible();
        expect((await card.boundingBox())?.height).toBeLessThan(200);
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        ).toBeLessThanOrEqual(0);
        await goTo(page, `/purchase-orders/${order.id}`);
        await expect(status(page)).toHaveText('Submitted');
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        ).toBeLessThanOrEqual(0);
        await expect(bar(page, 'Approve')).toBeInViewport();
      });
    });

    test.describe('320 px with the text at 200%', () => {
      test.use({ viewport: { width: 320, height: 640 } });

      for (const [language, label] of [
        ['English', 'English'],
        ['Bahasa Melayu', 'Malay'],
        ['中文', 'Chinese']
      ] as const) {
        test(`given ${label}, then the list and the detail do not scroll sideways or clip`, async ({
          page,
          signIn,
          shell
        }) => {
          await signIn('supervisor');
          if (language !== 'English') {
            await shell.setLanguage(language);
          }
          for (const path of ['/purchase-orders', `/purchase-orders/${order.id}`]) {
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

          test(`given the list and the detail, then axe finds no serious or critical violation (${scheme}, ${width})`, async ({
            page,
            signIn
          }) => {
            await signIn('supervisor');
            for (const path of ['/purchase-orders', `/purchase-orders/${order.id}`]) {
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
