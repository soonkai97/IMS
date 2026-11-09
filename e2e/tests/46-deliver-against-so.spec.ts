import type { Page } from '@playwright/test';
import { apiAs, unique, type PublicId } from '../src/api';
import {
  CUSTOMER_PREFIX,
  NO_APPROVAL,
  SKU_PREFIX,
  cancelOpenSalesOrders,
  createSalesOrder,
  getSalesOrder,
  salesCommand,
  salesIsOn,
  saveDelivery,
  setSales,
  startDelivery,
  submitDelivery,
  type ApiSalesOrder
} from '../src/sales';
import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';

// Deliver against a sales order on the real API (PL3.10), serial: order 10, deliver 6 and post (the order shows 6 of 10 and stays
// Approved), deliver 4 and post (Closed), a third delivery is refused, reversing the second reopens the quantity but not the order;
// a quantity above what is left is said in words; a post that would over-deliver is one alert naming the line; Warehouse staff
// see quantities and no price. The module is read first and put back last. Every delivery is reversed or cancelled again.
test.describe.configure({ mode: 'serial' });

/** A money figure on this app is written with two decimals ("5.00"); quantities never are. */
const MONEY = /\d[\d,]*\.\d{2}\b/;
const token = unique();
const data = {
  foundOn: false,
  customer: { id: '' as PublicId, name: `${CUSTOMER_PREFIX}D${token}` },
  productId: '' as PublicId,
  productName: '',
  sku: `${SKU_PREFIX}D${token}`,
  receiptId: '' as PublicId,
  /** Every delivery a scenario made, so the clean-up finds it even when the scenario failed half way. */
  deliveries: [] as PublicId[]
};
const admin = () => apiAs('admin');
const bar = (page: Page, name: string) => page.getByRole('button', { name, exact: true });
const status = (page: Page) => page.locator('app-page-header app-status-chip').first();

/** An Approved order of 10 pieces at RM 5.00 (below every approval rule: submitting releases it). */
async function approvedOrder(): Promise<ApiSalesOrder> {
  const order = await createSalesOrder(data.customer.id, data.productId, NO_APPROVAL);
  const submitted = await salesCommand('clerk', order.id, 'submit');
  return submitted.status === 'Approved'
    ? submitted
    : salesCommand('supervisor', order.id, 'approve');
}

/** The Deliver button of the order page: opens the draft delivery in the document editor. */
async function startFromPage(page: Page, order: Pick<ApiSalesOrder, 'id'>): Promise<PublicId> {
  await goTo(page, `/sales-orders/${order.id}`);
  await bar(page, 'Deliver').click();
  await expect(page).toHaveURL(/\/issues\/[0-9a-f-]{36}$/);
  const id = page.url().split('/').pop()!;
  data.deliveries.push(id);
  return id;
}

test.describe('Deliver against a sales order, real API', () => {
  test.beforeAll(async () => {
    data.foundOn = await salesIsOn();
    await setSales(true, 'E2E: deliver against a sales order');
    data.customer.id = (await admin().createPartner('Customer', { name: data.customer.name })).id;
    const product = await admin().createProduct({ sku: data.sku });
    data.productId = product.id;
    data.productName = product.name;
    data.receiptId = (
      await admin().post('receipts', [{ productId: product.id, qty: 40, unitCost: 1 }])
    ).id;
  });

  test.afterAll(async () => {
    try {
      // Newest first: a delivery that went out is reversed before the receipt that brought the stock in.
      for (const id of [...data.deliveries].reverse()) {
        const { status: state } = await admin().getDocument(id);
        if (state === 'Posted') {
          await admin()
            .reverse(id, 'E2E: throw-away delivery')
            .catch(() => undefined);
        } else if (['Draft', 'Submitted', 'Approved'].includes(state)) {
          await apiAs('supervisor').raw('POST', `/api/documents/${id}/cancel`, {});
        }
      }
      if (data.receiptId) {
        await admin()
          .reverse(data.receiptId, 'E2E: throw-away receipt')
          .catch(() => undefined);
      }
      if (await salesIsOn()) {
        await cancelOpenSalesOrders();
      }
    } finally {
      await setSales(data.foundOn, 'E2E: put back as found');
    }
  });

  test('given an Approved order of 10, when 6 are delivered and posted from the Deliver button, then the order shows 6 of 10 and stays Approved; the rest closes it; a third delivery is refused', async ({
    page,
    signIn
  }) => {
    const order = await approvedOrder();
    await signIn('supervisor');
    const editor = new DocumentEditor(page);

    const first = await startFromPage(page, order);
    await expect(page.getByRole('main')).toContainText(`sales order ${order.no}`);
    await expect(page.getByRole('main')).toContainText(data.customer.name);
    await expect(page.getByRole('main')).toContainText(
      'Order line 1: ordered 10, delivered 0, remaining 10'
    );
    await expect(page.getByRole('button', { name: 'Add item' })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: /^(Customer|Warehouse)/ })).toHaveCount(0);

    // More than is left is said in words and stops the submit.
    await editor.setQty(data.sku, 11);
    await expect(
      page.getByText('Only 10 PCS of this order line is left to deliver.')
    ).toBeVisible();
    await editor.setQty(data.sku, 6);
    await expect(page.getByText('Only 10 PCS')).toHaveCount(0);
    await editor.submitAndPost();
    await expect(status(page)).toHaveText('Posted');
    await expect(page.getByRole('main')).toContainText('still has 1 line to deliver');

    await goTo(page, `/sales-orders/${order.id}`);
    await expect(status(page)).toHaveText('Approved');
    const line = page.getByRole('row').filter({ hasText: data.sku });
    // The cells in order: SKU, name, ordered, delivered, remaining.
    await expect(line.getByRole('cell').nth(3)).toHaveText('6 PCS');
    await expect(line.getByRole('cell').nth(4)).toHaveText('4 PCS');
    await expect(page.getByRole('link', { name: /^ISS-/ })).toHaveCount(1);

    // The next delivery starts at what is left; posting it closes the order.
    const second = await startFromPage(page, order);
    await expect(editor.qty(data.sku)).toHaveValue('4');
    await editor.submitAndPost();
    await expect(status(page)).toHaveText('Posted');
    await expect(page.getByRole('main')).toContainText('is fully delivered and closed');
    await goTo(page, `/sales-orders/${order.id}`);
    await expect(status(page)).toHaveText('Closed');
    await expect(page.getByText('Fully delivered')).toBeVisible();
    await expect(bar(page, 'Deliver')).toHaveCount(0);

    // A third delivery is refused by the API in words.
    const third = await apiAs('supervisor').raw('POST', `/api/sales-orders/${order.id}/delivery`);
    expect(third.status).toBe(409);

    // Reversing the second gives the quantity back; the order is not reopened.
    await admin().reverse(second, 'E2E: reverse the second delivery');
    const after = await getSalesOrder(order.id);
    expect(after.status).toBe('Closed');
    expect(after.lines[0]?.deliveredQty).toBe(6);
    expect(after.lines[0]?.remainingQty).toBe(4);
    expect(first).not.toBe(second);
  });

  test('given two drafts of 6 against 10 and the first posted, then posting the second is refused in one alert naming the line and how many remain, and the draft stays', async ({
    page,
    signIn
  }) => {
    const order = await approvedOrder();
    const a = await startDelivery(order);
    const b = await startDelivery(order);
    data.deliveries.push(a.id, b.id);
    const savedA = await saveDelivery(a, 6);
    const savedB = await saveDelivery(b, 6);
    // B is submitted while 10 remain (the check at submit is advice); A then goes out.
    const readyB = await submitDelivery(savedB);
    const readyA = await submitDelivery(savedA);
    await apiAs('supervisor').command(a.id, 'post', {}, readyA.concurrencyToken);

    await signIn('supervisor');
    await goTo(page, `/issues/${b.id}`);
    await page.getByRole('button', { name: 'Post', exact: true }).click();
    await new DocumentEditor(page).confirm(/^Post/, 'Post');

    const alert = page.locator('app-banner[role="alert"]');
    await expect(alert).toHaveCount(1);
    await expect(alert).toContainText(
      `Line 1 (${data.sku}): ordered 10 PCS, delivered 6 PCS, remaining 4 PCS, this delivery 6 PCS.`
    );
    await expect(page.locator('app-toast')).toHaveCount(0);
    expect((await admin().getDocument(b.id)).status).not.toBe('Posted');
    expect(readyB.id).toBe(b.id);
  });

  test('given Warehouse staff, when they deliver against an order, then they see quantities and no price anywhere, and the order line figures are there', async ({
    page,
    signIn
  }) => {
    const order = await approvedOrder();
    await signIn('staff');
    const id = await startFromPage(page, order);

    await expect(page.getByRole('main')).toContainText(
      'Order line 1: ordered 10, delivered 0, remaining 10'
    );
    await expect(page.getByRole('main')).not.toContainText(MONEY);
    await expect(page.getByText(/Unit cost|Unit price/)).toHaveCount(0);
    await goTo(page, `/sales-orders/${order.id}`);
    await expect(page.getByRole('main')).not.toContainText(MONEY);
    await apiAs('supervisor').raw('POST', `/api/documents/${id}/cancel`, {});
  });
});
