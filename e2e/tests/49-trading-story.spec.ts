import type { Page } from '@playwright/test';
import { apiAs, unique, type ApiDocument, type PublicId } from '../src/api';
import {
  SUPPLIER_PREFIX,
  SLOW,
  approvedOrder,
  purchasingIsOn,
  receive,
  setPurchasing
} from '../src/purchasing';
import {
  CUSTOMER_PREFIX,
  PRICE_LIST_PREFIX,
  SKU_PREFIX,
  cancelOpenSalesOrders,
  deletePriceLists,
  getSalesOrder,
  salesIsOn,
  setSales
} from '../src/sales';
import { readFirstSheet } from '../src/xlsx';
import { expect, openSession, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { StockPages } from '../src/pages/screens';
import { goTo } from '../src/pages/shell';

// The trading story (PL3.19), one continuous business story on the real API, serial; every scenario is one step of it and uses what
// the step before made. Admin switches on Sales and Returns; a Supervisor makes a price list for a customer; a Clerk drafts a sales
// order (the price comes from the list, one price is changed: the audit says so); a Supervisor approves it; 6 of 10 are delivered (the
// order stays Approved), then 4 (the order closes by itself); the customer copy prints for the Closed order; a Clerk returns part of
// the first delivery, a Supervisor approves it (never its submitter) and posts it, the stock goes back at the ORIGINAL cost although
// the average has moved; a Supervisor returns part of a purchase-order receipt to the supplier; reversing the sale is refused while
// the return is Posted, reversing the return lets it through. At the end: the movements export has the Return rows, Warehouse staff
// see no price or cost on any screen of the story, and the audit log screen shows the key events.
// The three module states are read first and put back last; a price list is deleted again; every document is reversed or cancelled
// again (the product, the customer, the supplier, the Closed order and the Closed purchase order stay, as in the other specs).
test.describe.configure({ mode: 'serial' });

/** A money figure on this app is written with two decimals ("6,500.00"); quantities never are. */
const MONEY = /\d[\d,]*\.\d{2}\b/;
const token = unique();
const data = {
  found: { sales: false, purchasing: false, returns: false },
  customer: { id: '' as PublicId, name: `${CUSTOMER_PREFIX}T${token}` },
  supplier: { id: '' as PublicId, name: `${SUPPLIER_PREFIX}T${token}` },
  productId: '' as PublicId,
  productName: '',
  sku: `${SKU_PREFIX}T${token}`,
  priceList: `${PRICE_LIST_PREFIX}${token} story`,
  priceListId: '' as PublicId,
  /** The sales order made in the editor. */
  order: { id: '' as PublicId, no: '' },
  /** The two deliveries against it (6, then 4), the later receipt that moves the average, the two returns and the PO receipt. */
  firstDelivery: { id: '' as PublicId, no: '' },
  secondDelivery: { id: '' as PublicId, no: '' },
  poReceipt: undefined as ApiDocument | undefined,
  dearReceiptId: '' as PublicId,
  customerReturn: { id: '' as PublicId, no: '' },
  supplierReturn: { id: '' as PublicId, no: '' },
  /** Every document made here, oldest first, so the clean-up finds it even when a scenario failed half way. */
  made: [] as PublicId[]
};
const admin = () => apiAs('admin');
const status = (page: Page) => page.locator('app-page-header app-status-chip').first();
const bar = (page: Page, name: string) => page.getByRole('button', { name, exact: true });
const button = bar;
const moduleSwitch = (page: Page, name: string) => page.getByRole('switch', { name });
const lastSegment = (page: Page) => new URL(page.url()).pathname.split('/').pop()!;

async function returnsIsOn(): Promise<boolean> {
  const list = await admin().call<{ code: string; enabled: boolean }[]>('GET', '/api/modules');
  return list.find(item => item.code === 'returns')?.enabled === true;
}
async function setReturns(enabled: boolean, reason: string): Promise<void> {
  if ((await returnsIsOn()) !== enabled) {
    await admin().call('PUT', '/api/modules/returns', { enabled, reason });
  }
}

/** Puts a throw-away document out of the way by its state: a posted one is reversed, an unfinished one cancelled. */
async function retire(id: PublicId): Promise<void> {
  const { status: state } = await admin().getDocument(id);
  if (state === 'Posted') {
    await admin()
      .reverse(id, 'E2E: throw-away document')
      .catch(() => undefined);
  } else if (['Draft', 'Submitted', 'Approved'].includes(state)) {
    for (const role of ['supervisor', 'admin'] as const) {
      if ((await apiAs(role).raw('POST', `/api/documents/${id}/cancel`, {})).status < 300) {
        break;
      }
    }
  }
}

/** The audit log screen filtered to one action: the rows that name `text`, which is waited for (the list before the filter shows other actions). */
async function auditRows(page: Page, action: string, text: string) {
  await page.getByRole('combobox', { name: 'Action' }).selectOption({ label: action });
  return page.getByRole('row').filter({ hasText: action }).filter({ hasText: text });
}

test.describe('Trading story, real API', () => {
  test.beforeAll(async () => {
    data.found = {
      sales: await salesIsOn(),
      purchasing: await purchasingIsOn(),
      returns: await returnsIsOn()
    };
    // The story starts from off (a switch-off is refused while an order of ours is open). Returns first: it needs the other two.
    await cancelOpenSalesOrders().catch(() => undefined);
    await setReturns(false, 'E2E: start from off');
    await setSales(false, 'E2E: start from off');
    await setPurchasing(true, 'E2E: trading story');
    data.customer.id = (await admin().createPartner('Customer', { name: data.customer.name })).id;
    data.supplier.id = (await admin().createPartner('Supplier', { name: data.supplier.name })).id;
    const product = await admin().createProduct({ sku: data.sku });
    data.productId = product.id;
    data.productName = product.name;
    // 30 pieces bought on a purchase order at 3.00 and received against it: what the story sells and returns.
    const order = await approvedOrder(data.supplier.id, product.id, {
      qty: 30,
      unitPrice: 3
    });
    data.poReceipt = await receive(order, 30, id => data.made.push(id));
  });

  test.afterAll(async () => {
    try {
      // Newest first: a return goes before the document it returns, and the receipt that brought the stock in last.
      for (const id of [...data.made].reverse()) {
        await retire(id);
      }
      await deletePriceLists().catch(() => undefined);
      if (await salesIsOn()) {
        await cancelOpenSalesOrders();
      }
    } finally {
      await setReturns(data.found.returns, 'E2E: put back as found');
      await setSales(data.found.sales, 'E2E: put back as found');
      await setPurchasing(data.found.purchasing, 'E2E: put back as found');
    }
  });

  test('given Sales and Returns are off, when the Admin turns them on with a reason, then the menu gets Sales orders and the API answers', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    expect(await shell.menu()).not.toContain('Sales orders');
    expect((await admin().raw('GET', '/api/sales-orders')).status).toBe(404);

    await goTo(page, '/settings/modules');
    for (const name of ['Sales', 'Returns']) {
      await moduleSwitch(page, name).click();
      const dialog = page.getByRole('dialog', { name: `Turn on ${name}?` });
      await dialog.getByRole('textbox').fill(`E2E: trading story, ${name}`);
      await dialog.getByRole('button', { name: 'Turn on' }).click();
      await expect(dialog).toBeHidden({ timeout: SLOW });
      await expect(moduleSwitch(page, name)).toHaveAttribute('aria-checked', 'true', {
        timeout: SLOW
      });
    }
    expect(await salesIsOn()).toBe(true);
    expect(await returnsIsOn()).toBe(true);
    expect(await shell.menu()).toContain('Sales orders');
    expect((await admin().raw('GET', '/api/sales-orders')).status).toBe(200);
  });

  test('given a customer, when a Supervisor makes a price list for them, then the list is saved with the price and shows in the list of lists', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('supervisor');
    await shell.nav.getByRole('link', { name: 'Price lists' }).click();
    await page.getByRole('link', { name: 'New price list' }).click();
    await expect(page).toHaveURL(/\/price-lists\/new$/);
    await page.getByRole('textbox', { name: /^Name/ }).fill(data.priceList);
    await page
      .getByRole('combobox', { name: /^Customer/ })
      .selectOption({ label: data.customer.name });
    await new DocumentEditor(page).addItem({
      sku: data.sku,
      name: data.productName
    });
    await page.getByRole('spinbutton', { name: `Price of ${data.sku}` }).fill('600');
    await button(page, 'Save').click();

    await expect(page).toHaveURL(/\/price-lists$/);
    await expect(shell.toast('Price list saved.')).toHaveCount(1);
    await page.getByRole('searchbox', { name: 'Search price lists' }).fill(data.priceList);
    const row = page.getByRole('row').filter({ hasText: data.priceList });
    await expect(row).toContainText(data.customer.name);
    await expect(row).toContainText('1 price');
    // The audit log names the object by its id, so the id is kept for the last step.
    data.priceListId = (
      await admin().call<{ items: { id: PublicId }[] }>(
        'GET',
        `/api/price-lists?Search=${encodeURIComponent(data.priceList)}`
      )
    ).items[0]!.id;
  });

  test('given the price list, when a Clerk drafts a sales order, then the price is filled from the list, a changed price is kept and the order waits for a Supervisor', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('clerk');
    await goTo(page, '/sales-orders/new');
    const editor = new DocumentEditor(page);
    await editor.partner.selectOption({ label: data.customer.name });
    await editor.addItem({ sku: data.sku, name: data.productName });
    const price = page.getByRole('spinbutton', {
      name: `Unit price of ${data.sku}`
    });
    await expect(price).toHaveValue(/^600(\.00?)?$/);
    await expect(page.locator('.so-editor-listprice')).toContainText(
      /List price 600(\.00)?, from the customer's list\./
    );

    // The one change of price: the list says 600, the Clerk sells at 650 (6,500.00 for 10, above the Standard rule of an approver).
    await page.getByRole('spinbutton', { name: `Quantity of ${data.sku}` }).fill('10');
    await price.fill('650');
    await expect(page.locator('.so-editor-listprice')).toContainText('This price is different.');
    await expect(page.locator('.so-editor-totals')).toContainText('6,500.00');
    await button(page, 'Save draft').click();
    await expect(page).toHaveURL(/\/sales-orders\/[0-9a-f-]{36}$/);
    data.order.id = lastSegment(page);
    data.order.no = (await getSalesOrder(data.order.id, 'clerk')).no;
    expect((await getSalesOrder(data.order.id, 'clerk')).lines[0]?.unitPrice).toBe(650);

    await bar(page, 'Submit').click();
    await expect(shell.toast('Submitted for approval.')).toHaveCount(1);
    await expect(status(page)).toHaveText('Submitted');
  });

  test('given a Submitted order, when a Supervisor approves it and delivers 6 of 10, then the order shows 6 of 10 and stays Approved', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('supervisor');
    await goTo(page, `/sales-orders/${data.order.id}`);
    await bar(page, 'Approve').click();
    await shell
      .dialog(`Approve ${data.order.no}?`)
      .getByRole('button', { name: 'Approve', exact: true })
      .click();
    await expect(status(page)).toHaveText('Approved');

    await bar(page, 'Deliver').click();
    await expect(page).toHaveURL(/\/issues\/[0-9a-f-]{36}$/);
    data.firstDelivery.id = lastSegment(page);
    data.made.push(data.firstDelivery.id);
    const editor = new DocumentEditor(page);
    await expect(page.getByRole('main')).toContainText(
      'Order line 1: ordered 10, delivered 0, remaining 10'
    );
    await editor.setQty(data.sku, 6);
    await editor.submitAndPost();
    await expect(status(page)).toHaveText('Posted');
    data.firstDelivery.no = (await admin().getDocument(data.firstDelivery.id)).no;

    await goTo(page, `/sales-orders/${data.order.id}`);
    await expect(status(page)).toHaveText('Approved');
    const line = page.getByRole('row').filter({ hasText: data.sku });
    // The cells in order: SKU, name, ordered, delivered, remaining.
    await expect(line.getByRole('cell').nth(2)).toHaveText('10 PCS');
    await expect(line.getByRole('cell').nth(3)).toHaveText('6 PCS');
    await expect(line.getByRole('cell').nth(4)).toHaveText('4 PCS');
    expect(await admin().onHand(data.productId)).toBe(24);
  });

  test('given 6 delivered, when the other 4 are delivered, then the order closes by itself and its customer copy prints', async ({
    page,
    signIn
  }) => {
    await signIn('supervisor');
    await goTo(page, `/sales-orders/${data.order.id}`);
    await bar(page, 'Deliver').click();
    await expect(page).toHaveURL(/\/issues\/[0-9a-f-]{36}$/);
    data.secondDelivery.id = lastSegment(page);
    data.made.push(data.secondDelivery.id);
    const editor = new DocumentEditor(page);
    // The next delivery starts at what is left.
    await expect(editor.qty(data.sku)).toHaveValue('4');
    await editor.submitAndPost();
    await expect(status(page)).toHaveText('Posted');
    await expect(page.getByRole('main')).toContainText('is fully delivered and closed');
    data.secondDelivery.no = (await admin().getDocument(data.secondDelivery.id)).no;

    await goTo(page, `/sales-orders/${data.order.id}`);
    await expect(status(page)).toHaveText('Closed');
    await expect(page.getByText('Fully delivered')).toBeVisible();
    await expect(bar(page, 'Deliver')).toHaveCount(0);
    expect(await admin().onHand(data.productId)).toBe(20);

    // The customer copy of the Closed order, opened by the Clerk who made it: prices, customer and no navigation when printed.
    const clerk = await openSession(page.context().browser()!, 'clerk');
    try {
      await goTo(clerk.page, `/sales-orders/${data.order.id}`);
      await clerk.page.getByRole('link', { name: 'Print' }).click();
      await expect(clerk.page).toHaveURL(new RegExp(`/sales-orders/${data.order.id}/print$`));
      await expect(
        clerk.page.getByRole('heading', {
          level: 1,
          name: `Sales order ${data.order.no}`
        })
      ).toBeVisible();
      await expect(clerk.page.locator('.so-print')).toContainText(data.customer.name);
      await expect(clerk.page.locator('.so-print')).toContainText('650.00');
      await expect(clerk.page.locator('.so-print')).toContainText('6,500.00');
      await clerk.page.emulateMedia({ media: 'print' });
      await expect(clerk.shell.nav).toHaveCount(0);
      await clerk.page.emulateMedia({ media: 'screen' });
    } finally {
      await clerk.context.close();
    }
  });

  test('given the stock was bought in again dearer, when a Clerk returns 2 of the first delivery and a Supervisor approves and posts it, then the 2 go back at the ORIGINAL cost and the Clerk saw no cost', async ({
    page,
    signIn,
    shell
  }) => {
    // The average moves: 10 more at 5.00 on top of 20 at 3.00 makes it 3.67, so a return at the average would be wrong.
    data.dearReceiptId = (
      await admin().post('receipts', [{ productId: data.productId, qty: 10, unitCost: 5 }])
    ).id;
    data.made.push(data.dearReceiptId);

    await signIn('clerk');
    await goTo(page, `/issues/${data.firstDelivery.id}`);
    await bar(page, 'Return').click();
    await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
    data.customerReturn.id = lastSegment(page);
    data.made.push(data.customerReturn.id);
    const editor = new DocumentEditor(page);
    await expect(page.getByRole('main')).toContainText(
      `This is a return of ${data.firstDelivery.no}.`
    );
    await expect(page.getByRole('main')).toContainText('already returned 0 PCS, returnable 6 PCS');
    await expect(page.getByRole('button', { name: 'Add item' })).toHaveCount(0);
    // The Clerk holds cost.view and still sees no cost on a return: it is the original's.
    await expect(page.getByRole('spinbutton', { name: /Unit cost|Cost of/ })).toHaveCount(0);
    await expect(page.getByRole('textbox', { name: /Unit cost/ })).toHaveCount(0);

    await editor.setQty(data.sku, 7);
    await expect(page.getByText('Only 6 PCS of this line can still be returned.')).toBeVisible();
    await editor.setQty(data.sku, 2);
    await editor.action('Save draft').click();
    await expect(shell.toast('Draft saved.')).toHaveCount(1);
    await bar(page, 'Submit').click();
    await expect(status(page)).toHaveText('Submitted');
    data.customerReturn.no = (await admin().getDocument(data.customerReturn.id)).no;

    // A different person approves it: the Supervisor, in a second browser, then posts it.
    const supervisor = await openSession(page.context().browser()!, 'supervisor');
    try {
      await goTo(supervisor.page, `/receipts/${data.customerReturn.id}`);
      await expect(status(supervisor.page)).toHaveText('Submitted');
      await supervisor.page.getByRole('button', { name: 'Approve', exact: true }).click();
      await new DocumentEditor(supervisor.page).confirm(/^Approve/, 'Approve');
      await expect(status(supervisor.page)).toHaveText('Approved');
      await supervisor.page.getByRole('button', { name: 'Post', exact: true }).click();
      await new DocumentEditor(supervisor.page).confirm(/^Post/, 'Post');
      await expect(status(supervisor.page)).toHaveText('Posted');

      // Stock: 20 + 10 + 2 pieces; the ledger row of the return carries the cost of the sale (3.00), not the average (3.67).
      expect(await admin().onHand(data.productId)).toBe(32);
      await goTo(supervisor.page, '/reports');
      const returnRow = supervisor.page
        .getByRole('row')
        .filter({ hasText: data.customerReturn.no });
      await expect(returnRow).toHaveCount(1);
      await expect(returnRow).toContainText('+2');
      await expect(returnRow).toContainText('3.00');
      await expect(returnRow).not.toContainText('3.67');

      // The original shows what came back.
      await goTo(supervisor.page, `/issues/${data.firstDelivery.id}`);
      await expect(supervisor.page.getByRole('main')).toContainText(
        'Returned 2 PCS, returnable 4 PCS'
      );
    } finally {
      await supervisor.context.close();
    }
  });

  test('given a received purchase order, when a Supervisor returns 5 to the supplier and it is approved and posted, then the stock falls by 5', async ({
    page,
    signIn
  }) => {
    await signIn('supervisor');
    await goTo(page, `/receipts/${data.poReceipt!.id}`);
    await bar(page, 'Return').click();
    await expect(page).toHaveURL(/\/issues\/[0-9a-f-]{36}$/);
    data.supplierReturn.id = lastSegment(page);
    data.made.push(data.supplierReturn.id);
    const editor = new DocumentEditor(page);
    await expect(page.getByRole('main')).toContainText(
      `This is a return of ${data.poReceipt!.no}.`
    );
    await expect(page.getByRole('main')).toContainText('Purpose: Return');
    await expect(page.getByRole('main')).toContainText(data.supplier.name);
    await editor.setQty(data.sku, 5);
    await editor.action('Save draft').click();
    await expect(page.getByText('Draft saved.')).toBeVisible();
    data.supplierReturn.no = (await admin().getDocument(data.supplierReturn.id)).no;

    // The submitter cannot approve it; the Admin does, the Supervisor posts.
    await editor.submitAndPost();
    await expect(status(page)).toHaveText('Submitted');
    await expect(bar(page, 'Approve')).toHaveAttribute('aria-disabled', 'true');
    const reviewer = await openSession(page.context().browser()!, 'admin');
    try {
      await goTo(reviewer.page, `/issues/${data.supplierReturn.id}`);
      await reviewer.page.getByRole('button', { name: 'Approve', exact: true }).click();
      await new DocumentEditor(reviewer.page).confirm(/^Approve/, 'Approve');
      await expect(status(reviewer.page)).toHaveText('Approved');
    } finally {
      await reviewer.context.close();
    }
    await page.reload();
    await editor.action('Post').click();
    await editor.confirm(/^Post/, 'Post');
    await expect(status(page)).toHaveText('Posted');
    expect(await admin().onHand(data.productId)).toBe(27);

    // The receipt shows what went back.
    await goTo(page, `/receipts/${data.poReceipt!.id}`);
    await expect(page.getByRole('main')).toContainText('Returned 5 PCS, returnable 25 PCS');
  });

  test('given a sale with a Posted return, then reversing the sale is refused in the API sentence, and reversing the return lets the sale be reversed', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    const editor = new DocumentEditor(page);
    await goTo(page, `/issues/${data.firstDelivery.id}`);
    await bar(page, 'Reverse').click();
    await editor.confirm(
      `Reverse ${data.firstDelivery.no}?`,
      'Reverse',
      'E2E: reverse the first delivery'
    );
    await expect(page.locator('app-banner[role="alert"]')).toHaveCount(1);
    await expect(page.locator('app-banner[role="alert"]')).toContainText(
      `${data.firstDelivery.no} has posted returns, so it cannot be reversed. Reverse the returns first.`
    );
    await expect(status(page)).toHaveText('Posted');
    expect((await admin().getDocument(data.firstDelivery.id)).status).toBe('Posted');

    // The way out the sentence names: reverse the return, then the sale.
    await goTo(page, `/receipts/${data.customerReturn.id}`);
    await bar(page, 'Reverse').click();
    await editor.confirm(
      `Reverse ${data.customerReturn.no}?`,
      'Reverse',
      'E2E: the customer sent nothing back'
    );
    await expect(shell.toast('Reversed. Stock adjusted.')).toHaveCount(1);
    await expect(status(page)).toHaveText('Reversed');

    await goTo(page, `/issues/${data.firstDelivery.id}`);
    await bar(page, 'Reverse').click();
    await editor.confirm(
      `Reverse ${data.firstDelivery.no}?`,
      'Reverse',
      'E2E: reverse the first delivery'
    );
    await expect(status(page)).toHaveText('Reversed');
    // 32 - 5 (supplier) - 2 (return reversed) + 6 (delivery reversed); the order is not reopened.
    expect(await admin().onHand(data.productId)).toBe(31);
    const order = await getSalesOrder(data.order.id);
    expect(order.status).toBe('Closed');
    expect(order.lines[0]?.deliveredQty).toBe(4);
  });

  test('given the story is told, then the movements export carries the Return rows and only those', async () => {
    const file = await apiAs('supervisor').download(
      `/api/reports/movements/export?ProductId=${data.productId}`
    );
    const [header, ...rows] = readFirstSheet(file);
    const column = (name: string) => header!.indexOf(name);
    expect(header, 'every column the Supervisor may see').toEqual(
      expect.arrayContaining(['Document', 'Stock effect', 'Unit cost', 'Reversal', 'Return'])
    );
    const of = (docNo: string) => rows.filter(row => row[column('Document')] === docNo);
    const flag = (row: string[]) => row[column('Return')];
    const effect = (row: string[]) => Number(row[column('Stock effect')]);

    // The customer return: its posting (+2, at the cost of the sale) and its reversal (-2), both flagged.
    const customer = of(data.customerReturn.no);
    expect(customer.map(effect).sort((a, b) => a - b)).toEqual([-2, 2]);
    expect(customer.map(flag)).toEqual(['RETURN', 'RETURN']);
    expect(Number(customer.find(row => effect(row) === 2)![column('Unit cost')])).toBe(3);
    // The supplier return: one row, -5.
    const supplier = of(data.supplierReturn.no);
    expect(supplier.map(effect)).toEqual([-5]);
    expect(supplier.map(flag)).toEqual(['RETURN']);
    // Nothing else is flagged: the deliveries, the dearer receipt and the PO receipt are ordinary movements.
    for (const docNo of [data.firstDelivery.no, data.secondDelivery.no, data.poReceipt!.no]) {
      const ordinary = of(docNo);
      expect(ordinary.length, docNo).toBeGreaterThan(0);
      expect(ordinary.map(flag).filter(Boolean), docNo).toEqual([]);
    }
    expect(rows.filter(row => flag(row) === 'RETURN')).toHaveLength(3);
  });

  test('given Warehouse staff, then no screen of the story shows a price or a cost', async ({
    browser
  }) => {
    const staff = await openSession(browser, 'staff');
    try {
      const { page } = staff;
      const screens: [string, string][] = [
        [`/sales-orders/${data.order.id}`, data.order.no],
        [`/issues/${data.firstDelivery.id}`, data.firstDelivery.no],
        [`/issues/${data.secondDelivery.id}`, data.secondDelivery.no],
        [`/receipts/${data.customerReturn.id}`, data.customerReturn.no],
        [`/issues/${data.supplierReturn.id}`, data.supplierReturn.no],
        [`/receipts/${data.poReceipt!.id}`, data.poReceipt!.no]
      ];
      for (const [path, no] of screens) {
        await goTo(page, path);
        await expect(page.getByRole('heading', { level: 1 })).toContainText(no);
        await expect(page.getByRole('main'), path).toContainText('PCS');
        await expect(page.getByRole('main'), path).not.toContainText(MONEY);
        await expect(page.getByText(/Unit cost|Unit price|Amount/), path).toHaveCount(0);
      }
      await goTo(page, '/sales-orders');
      await expect(page.getByRole('row').filter({ hasText: data.order.no })).toBeVisible();
      await expect(page.getByRole('main')).not.toContainText(MONEY);
      await new StockPages(page).openStock(data.sku);
      await expect(page.getByRole('row').filter({ hasText: data.sku })).toBeVisible();
      await expect(page.getByRole('main')).not.toContainText(MONEY);
    } finally {
      await staff.context.close();
    }
  });

  test('given the story is told, then the audit log screen shows its key events', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await goTo(page, '/audit-log');
    await expect(page.getByRole('heading', { level: 1, name: 'Audit log' })).toBeVisible();

    const events: [string, string][] = [
      ['sales-order.created', data.order.no],
      ['sales-order.price-changed', data.order.no],
      ['sales-order.submitted', data.order.no],
      ['sales-order.approved', data.order.no],
      ['sales-order.closed', data.order.no],
      ['price-list.created', data.priceListId],
      ['document.posted', data.firstDelivery.no],
      ['document.posted', data.customerReturn.no],
      ['document.approved', data.customerReturn.no],
      ['document.posted', data.supplierReturn.no],
      ['document.reversed', data.customerReturn.no],
      ['document.reversed', data.firstDelivery.no],
      ['module.enabled', 'returns']
    ];
    for (const [action, text] of events) {
      await expect(await auditRows(page, action, text), `${action} ${text}`).not.toHaveCount(0);
    }
  });
});
