import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import { announcedBy } from '../src/feedback';
import {
  NO_APPROVAL,
  SKU_PREFIX,
  SLOW,
  SUPPLIER_PREFIX,
  cancelOpenOrders,
  command,
  createOrder,
  getOrder,
  purchasingIsOn,
  receiptFromOrder,
  receive,
  retireReceipt,
  setPurchasing,
  type ApiOrder
} from '../src/purchasing';
import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';

// Receive against a purchase order (PL2.9), on the real API: the Receive button, the receipt of an order in the receipt editor, partial
// and last receipts, the closing of the order, the refusal of an over-receipt at posting, and what a person without cost.view sees.
// The module is read first and put back last (also after a failure). Orders and receipts are throw-away ones: receipts are retired by
// their state (posted ones reversed, open ones cancelled), then the orders are cancelled. One scenario after the other (serial).
test.describe.configure({ mode: 'serial' });

const REASON = 'E2E: receive against an order';
const MONEY = /\d[\d,]*\.\d{2}\b/;
const token = unique();
const data = {
  foundOn: false,
  supplier: { id: '' as PublicId, name: `${SUPPLIER_PREFIX}R${token}` },
  productId: '' as PublicId,
  sku: `${SKU_PREFIX}R${token}`,
  /** Every receipt a scenario made, so the clean-up finds it even when the scenario failed half way. */
  receipts: [] as PublicId[]
};
const admin = () => apiAs('admin');
const bar = (page: Page, name: string) => page.getByRole('button', { name, exact: true });
const status = (page: Page) => page.locator('app-page-header app-status-chip').first();
const noSideways = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);

/** An Approved order of 10 pieces at RM 5.00 (below every approval rule: submitting releases it). */
async function approved(): Promise<ApiOrder> {
  const order = await createOrder(data.supplier.id, data.productId, NO_APPROVAL);
  return command('clerk', order.id, 'submit');
}

test.describe('Receive against a purchase order, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await purchasingIsOn();
    await setPurchasing(true, REASON);
    data.supplier.id = (await admin().createPartner('Supplier', { name: data.supplier.name })).id;
    data.productId = (await admin().createProduct({ sku: data.sku })).id;
  });

  test.afterAll(async () => {
    try {
      // A receipt in progress stops an order being cancelled: retire the receipts first, then cancel what is still open.
      for (const id of data.receipts) {
        await retireReceipt(id).catch(() => undefined);
      }
      if (await purchasingIsOn()) {
        await cancelOpenOrders();
      }
    } finally {
      await setPurchasing(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given an Approved order, then Receive opens its draft receipt, a partial receipt posts and the order shows what is left, and the second receipt closes it', async ({
    page,
    signIn
  }) => {
    const order = await approved();
    const editor = new DocumentEditor(page);
    await signIn('supervisor');
    await goTo(page, `/purchase-orders/${order.id}`);
    // The button is the API's own offer, not a role.
    expect((await getOrder(order.id, 'supervisor')).allowedActions).toContain('receive');
    await bar(page, 'Receive').click();

    await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
    data.receipts.push(await editor.savedId());
    await expect(editor.status).toHaveText('Draft');
    await expect(page.getByText(`This receipt is for purchase order ${order.no}`)).toBeVisible();
    await expect(page.getByRole('link', { name: 'Open the order.' })).toHaveAttribute(
      'href',
      `/purchase-orders/${order.id}`
    );
    // The supplier and the warehouse are the order's; the lines are the order's too.
    await expect(editor.warehouse).toHaveCount(0);
    await expect(editor.partner).toHaveCount(0);
    await expect(page.getByText(data.supplier.name)).toBeVisible();
    await expect(editor.addItemButton).toHaveCount(0);
    await expect(
      page.getByText('Order line 1: ordered 10, received 0, remaining 10 PCS')
    ).toBeVisible();
    await expect(editor.qty(data.sku)).toHaveValue('10');

    // Above what is left: said in words, and nothing can be sent.
    await editor.setQty(data.sku, 11);
    await expect(
      page.getByText('Only 10 PCS of this order line is left to receive.')
    ).toBeVisible();
    await expect(editor.action('Submit & post')).toBeDisabled();

    await editor.setQty(data.sku, 6);
    await editor.submitAndPost();
    await expect(editor.status).toHaveText('Posted', { timeout: SLOW });
    await expect(page.getByText(`Order ${order.no} still has 1 line to receive.`)).toBeVisible();

    // The order shows 6 received and 4 left, still Approved, with the receipt in its list.
    await page.getByRole('link', { name: 'Open the order.' }).click();
    await expect(status(page)).toHaveText('Approved');
    const row = page.locator('tbody tr').first();
    await expect(row).toContainText('6 PCS');
    await expect(row).toContainText('4 PCS');
    await expect(page.getByRole('heading', { name: 'Receipts (1)' })).toBeVisible();

    // The rest: the draft starts at what is left, and the last quantity closes the order.
    await bar(page, 'Receive').click();
    await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
    data.receipts.push(await editor.savedId());
    await expect(editor.qty(data.sku)).toHaveValue('4');
    await editor.submitAndPost();
    await expect(editor.status).toHaveText('Posted', { timeout: SLOW });
    await expect(
      page.getByText(`Order ${order.no} is now fully received and closed.`)
    ).toBeVisible();

    await page.getByRole('link', { name: 'Open the order.' }).click();
    await expect(status(page)).toHaveText('Closed');
    await expect(page.getByText('Fully received')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Receipts (2)' })).toBeVisible();
    await expect(bar(page, 'Receive')).toHaveCount(0);
    expect((await getOrder(order.id, 'supervisor')).allowedActions).not.toContain('receive');
  });

  test('given a receipt that was submitted when the order still had room, when another receipt takes the room, then posting says which line and how many remain, in one alert, and lowering the quantity posts', async ({
    page,
    signIn
  }) => {
    const order = await approved();
    const submitted = await receiptFromOrder(order);
    data.receipts.push(submitted.id);
    await apiAs('supervisor').command(submitted.id, 'submit');
    // 7 of the 10 arrive on another receipt first.
    await receive(order, 7, id => data.receipts.push(id));

    const editor = new DocumentEditor(page);
    await signIn('supervisor');
    await editor.open('receipts', submitted.id);
    await expect(editor.status).toHaveText('Submitted');
    await editor.action('Post').click();
    await editor.confirm(/^Post /, 'Post');

    const figures = `Line 1 (${data.sku}): ordered 10, received 7, remaining 3, this receipt 10.`;
    await expect(page.getByText(figures)).toBeVisible({ timeout: SLOW });
    expect(await announcedBy(page, figures)).toBe(1);
    await expect(
      page.getByRole('alert').filter({ hasText: 'more than the order has left' })
    ).toHaveCount(1);
    // Nothing else says it: no toast, no "changed since you opened it".
    await expect(page.getByText('changed since you opened it')).toHaveCount(0);
    await expect(editor.status).toHaveText('Submitted');

    // Edit items (back to Draft), lower the quantity, send it again.
    await page.getByRole('button', { name: 'Edit items' }).click();
    await editor.confirm(/back to draft\?$/, 'Back to draft');
    await expect(editor.status).toHaveText('Draft');
    await editor.setQty(data.sku, 3);
    await editor.submitAndPost();
    await expect(editor.status).toHaveText('Posted', { timeout: SLOW });
    expect((await getOrder(order.id, 'supervisor')).status).toBe('Closed');
  });

  test('given the order is no longer open for receiving and the API answers with its general sentence, then it is one alert and not a stale copy', async ({
    page,
    signIn
  }) => {
    const sentence = 'The purchase order of this receipt is no longer open for receiving.';
    const order = await approved();
    const draft = await receiptFromOrder(order);
    data.receipts.push(draft.id);
    await apiAs('supervisor').command(draft.id, 'submit');

    const editor = new DocumentEditor(page);
    await signIn('supervisor');
    await editor.open('receipts', draft.id);
    // What a caller without po.read gets from the API at posting: a sentence with no figures and no code.
    await page.route(/\/api\/documents\/[0-9a-f-]{36}\/post$/, route =>
      route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Conflict', status: 409, detail: sentence })
      })
    );
    await editor.action('Post').click();
    await editor.confirm(/^Post /, 'Post');

    await expect(page.getByText(sentence)).toBeVisible();
    expect(await announcedBy(page, sentence)).toBe(1);
    await expect(page.getByText('changed since you opened it')).toHaveCount(0);
    await expect(bar(page, 'Reload')).toHaveCount(0);
    await expect(editor.action('Post')).toBeEnabled();
  });

  test('given Warehouse staff, then they receive by quantity: no price anywhere on the receipt, and the stock is valued at the order price', async ({
    page,
    signIn
  }) => {
    const order = await approved();
    expect((await getOrder(order.id, 'staff')).allowedActions).toContain('receive');
    const editor = new DocumentEditor(page);
    await signIn('staff');
    await goTo(page, `/purchase-orders/${order.id}`);
    await bar(page, 'Receive').click();
    await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
    const id = await editor.savedId();
    data.receipts.push(id);

    await expect(editor.qty(data.sku)).toHaveValue('10');
    await expect(editor.unitCost(data.sku)).toHaveCount(0);
    expect(await page.getByRole('main').innerText()).not.toMatch(MONEY);
    await editor.setQty(data.sku, 5);
    await editor.action('Save draft').click();
    await expect(page.getByText('Draft saved.')).toBeVisible();

    const saved = await admin().call<{ lines: { qty: number; unitCost: number }[] }>(
      'GET',
      `/api/documents/${id}`
    );
    expect(saved.lines.map(line => [line.qty, line.unitCost])).toEqual([[5, 5]]);
  });

  test.describe('layout and accessibility', () => {
    let order: ApiOrder;
    let draft: { id: PublicId; no: string };
    test.beforeAll(async () => {
      order = await approved();
      draft = await receiptFromOrder(order);
      data.receipts.push(draft.id);
    });

    const pages = () => [`/purchase-orders/${order.id}`, `/receipts/${draft.id}`];
    /** Each page is read only once its own content is there (the heading of the page before it is still on screen while the next one loads). */
    const ready = (page: Page, path: string) =>
      expect(
        page.getByRole('heading', {
          level: 1,
          name: path.startsWith('/receipts/') ? draft.no : order.no
        })
      ).toBeVisible();

    test.describe('360 px phone', () => {
      test.use({ viewport: { width: 360, height: 740 } });

      test('given a phone, then the order and its draft receipt do not scroll sideways and the action buttons are in view', async ({
        page,
        signIn
      }) => {
        await signIn('supervisor');
        await goTo(page, pages()[0]);
        await expect(bar(page, 'Receive')).toBeInViewport();
        expect(await noSideways(page)).toBeLessThanOrEqual(0);
        await goTo(page, pages()[1]);
        await expect(
          page.getByText('Order line 1: ordered 10, received 0, remaining 10 PCS')
        ).toBeVisible();
        await expect(new DocumentEditor(page).action('Submit & post')).toBeInViewport();
        expect(await noSideways(page)).toBeLessThanOrEqual(0);
      });
    });

    test.describe('320 px with the text at 200%', () => {
      test.use({ viewport: { width: 320, height: 640 } });

      for (const [language, label] of [
        ['English', 'English'],
        ['Bahasa Melayu', 'Malay'],
        ['中文', 'Chinese']
      ] as const) {
        test(`given ${label}, then the order and the receipt of an order do not scroll sideways or clip`, async ({
          page,
          signIn,
          shell
        }) => {
          await signIn('supervisor');
          if (language !== 'English') {
            await shell.setLanguage(language);
          }
          for (const path of pages()) {
            await goTo(page, path);
            await ready(page, path);
            await page.waitForLoadState('networkidle');
            await page.waitForFunction(() => document.getAnimations().length === 0);
            await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
            expect(await noSideways(page)).toBeLessThanOrEqual(0);
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

          test(`given the order and its draft receipt, then axe finds no serious or critical violation (${scheme}, ${width})`, async ({
            page,
            signIn
          }) => {
            await signIn('supervisor');
            for (const path of pages()) {
              await goTo(page, path);
              await ready(page, path);
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
});
