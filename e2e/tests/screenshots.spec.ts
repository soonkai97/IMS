import type { Browser, Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { apiAs, type ApiDocument, type ApiProduct, type PublicId } from '../src/api';
import { API_URL, WEB_URL, type Role } from '../src/config';
import { expect, openSession, test, type Session } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { Shell, goTo, signIn } from '../src/pages/shell';
import {
  mainWarehouseId,
  setPurchasing,
  type ApiOrder,
  type ReceiptDraft,
  command as purchaseCommand
} from '../src/purchasing';
import {
  createPriceList,
  salesCommand,
  setSales,
  startDelivery,
  submitDelivery,
  type ApiDelivery,
  type ApiSalesOrder
} from '../src/sales';

// README screenshots. NOT a test of the product: it fills a throw-away database with made-up trading data and saves pictures of
// the real screens to docs/screenshots (SCREENSHOTS_DIR changes the folder). It runs only with SCREENSHOTS=1 and is skipped in the
// normal suite and in CI. Use a NEW, EMPTY database every time (the demo data is made once: a second run stops on the duplicate SKU).
// Everything on the pictures is invented: the demo users of the seed, the products, customers and suppliers below. How to run it:
// e2e/README.md, "Regenerating the README screenshots".
const ENABLED = process.env['SCREENSHOTS'] === '1';
const OUT = resolve(
  process.env['SCREENSHOTS_DIR'] ?? join(__dirname, '..', '..', 'docs', 'screenshots')
);
const DESKTOP = { width: 1280, height: 800 };
const PHONE = { width: 360, height: 740 };

test.describe.configure({ mode: 'serial' });

/** Product: SKU, name, safety stock, unit cost, quantity received, quantity already sold, selling price on the default list. */
const PRODUCTS: [string, string, number, number, number, number, number][] = [
  ['MUG-350', 'Ceramic mug 350 ml', 40, 4.2, 400, 90, 9.9],
  ['BTL-500', 'Steel water bottle 500 ml', 30, 11.5, 60, 38, 24.9],
  ['TOTE-01', 'Cotton tote bag', 50, 2.8, 500, 140, 7.5],
  ['NB-A5', 'Notebook A5 ruled', 100, 1.9, 300, 230, 4.5],
  ['PEN-B50', 'Ballpoint pen blue, box of 50', 20, 14, 120, 40, 29],
  ['CBL-C1', 'USB-C cable 1 m', 25, 6.5, 80, 70, 15.9],
  ['LAMP-D2', 'LED desk lamp', 15, 28, 160, 35, 59],
  ['TAPE-48', 'Packing tape 48 mm', 60, 1.1, 700, 180, 2.9]
];
/** Prices of the customer lists (the rest comes from the default list). */
const RIVERSIDE_PRICES: Record<string, number> = {
  'MUG-350': 8.9,
  'BTL-500': 22,
  'PEN-B50': 26,
  'TOTE-01': 6.9
};
const SUPPLIERS = ['Acme Packaging Supplies', 'Lakeview Wholesale'];
const CUSTOMERS = ['Riverside Cafe', 'Greenfield Grocers', 'Sunrise Gift Shop'];

const data = {
  products: new Map<string, ApiProduct>(),
  customers: new Map<string, PublicId>(),
  suppliers: new Map<string, PublicId>(),
  purchaseOrder: { id: '' as PublicId, no: '' },
  salesOrder: { id: '' as PublicId, no: '' },
  riversideList: '' as PublicId,
  sale: { id: '' as PublicId, no: '' }
};
const admin = () => apiAs('admin');
const product = (sku: string): ApiProduct => data.products.get(sku)!;

/** What may never be in a picture: an address other than the demo domain, an IP address, a token. */
const FORBIDDEN: [string, RegExp][] = [
  ['an email address that is not the demo domain', /[\w.+-]+@(?!ims\.local\b)[\w-]+(\.[\w-]+)+/i],
  ['an IP address', /\b\d{1,3}(\.\d{1,3}){3}\b/],
  ['a token', /eyJ[\w-]{10,}|Bearer\s+\S+/],
  ['a host name', /\b(localhost|127\.0\.0\.1)\b/i]
];

/** Saves the page as one PNG after a last look at its text for anything that must not be published. */
async function shot(page: Page, name: string, keepScroll = false): Promise<void> {
  await page.evaluate(() => document.fonts.ready);
  await page.waitForLoadState('networkidle');
  // No toast in a picture, the page at the top (unless the caller scrolled to what it wants to show) and no focus ring on a field.
  await expect(page.locator('app-toast')).toHaveCount(0);
  await page.evaluate(top => {
    (document.activeElement as HTMLElement | null)?.blur();
    if (top) {
      window.scrollTo(0, 0);
      for (const element of Array.from(document.querySelectorAll('*'))) {
        if (element.scrollTop > 0) {
          element.scrollTop = 0;
        }
      }
    }
  }, !keepScroll);
  // The text on the page, the account button included: only the demo user (a role name) and invented names may be there.
  const text = await page.locator('body').innerText();
  for (const [what, pattern] of FORBIDDEN) {
    expect(pattern.test(text), `${name}: the page shows ${what}`).toBe(false);
  }
  mkdirSync(OUT, { recursive: true });
  await page.screenshot({ path: join(OUT, `${name}.png`), animations: 'disabled', caret: 'hide' });
}

/** A phone: 360x740 at twice the pixels, touch on, signed in as a role. */
async function phone(browser: Browser, role: Role): Promise<Session> {
  const context = await browser.newContext({
    baseURL: WEB_URL,
    locale: 'en-US',
    timezoneId: 'Asia/Kuala_Lumpur',
    serviceWorkers: 'block',
    colorScheme: 'light',
    viewport: PHONE,
    deviceScaleFactor: 2,
    isMobile: true,
    hasTouch: true
  });
  await context.route(/\/assets\/config\.json/, route =>
    route.fulfill({ json: { apiBaseUrl: API_URL } })
  );
  const page = await context.newPage();
  await signIn(page, role);
  return { context, page, shell: new Shell(page) };
}

async function returnsOn(): Promise<void> {
  const list = await admin().call<{ code: string; enabled: boolean }[]>('GET', '/api/modules');
  if (list.find(item => item.code === 'returns')?.enabled !== true) {
    await admin().call('PUT', '/api/modules/returns', {
      enabled: true,
      reason: 'Demo data for the README pictures'
    });
  }
}

/** A purchase order of several lines, made by the Clerk and submitted; an order under the approval amount is Approved at once. */
async function purchaseOrder(
  supplier: string,
  lines: [sku: string, qty: number, unitPrice: number][],
  remark: string
): Promise<ApiOrder> {
  const order = await apiAs('clerk').call<ApiOrder>('POST', '/api/purchase-orders', {
    warehouseId: await mainWarehouseId(),
    supplierId: data.suppliers.get(supplier),
    remark,
    lines: lines.map(([sku, qty, unitPrice]) => ({
      productId: product(sku).id,
      qty,
      uomId: null,
      unitPrice,
      remark: null
    }))
  });
  return purchaseCommand('clerk', order.id, 'submit');
}

/** A sales order of several lines, made by the Clerk and submitted, and approved by the Supervisor when a rule holds it. */
async function salesOrder(
  customer: string,
  lines: [sku: string, qty: number, unitPrice: number][],
  remark: string,
  options: { submit: boolean; approve: boolean }
): Promise<ApiSalesOrder> {
  let order = await apiAs('clerk').call<ApiSalesOrder>('POST', '/api/sales-orders', {
    warehouseId: await mainWarehouseId(),
    customerId: data.customers.get(customer),
    remark,
    lines: lines.map(([sku, qty, unitPrice]) => ({
      productId: product(sku).id,
      qty,
      uomId: null,
      unitPrice,
      remark: null
    }))
  });
  if (options.submit) {
    order = await salesCommand('clerk', order.id, 'submit');
  }
  if (options.approve && order.status !== 'Approved') {
    order = await salesCommand('supervisor', order.id, 'approve');
  }
  return order;
}

/** Receives some of an approved purchase order: one quantity per line, in line order. */
async function receiveSome(order: ApiOrder, quantities: number[]): Promise<void> {
  const supervisor = apiAs('supervisor');
  const draft = await supervisor.call<ReceiptDraft>(
    'POST',
    `/api/purchase-orders/${order.id}/receipt`
  );
  const saved = await supervisor.call<ApiDocument>('PUT', `/api/documents/${draft.id}`, {
    warehouseId: draft.warehouseId,
    partnerId: draft.partnerId,
    purpose: null,
    remark: null,
    lines: draft.lines.map((line, index) => ({
      productId: line.productId,
      qty: quantities[index],
      uomId: line.uomId,
      unitCost: line.unitCost ?? undefined,
      remark: null,
      purchaseOrderLineNo: line.purchaseOrderLineNo
    }))
  });
  let current = await supervisor.command(draft.id, 'submit', {}, saved.concurrencyToken);
  if (current.requiresApproval) {
    current = await apiAs('owner').command(draft.id, 'approve', {}, current.concurrencyToken);
  }
  await supervisor.command(draft.id, 'post', {}, current.concurrencyToken);
}

/** Delivers some of an approved sales order: one quantity per line, in line order. */
async function deliverSome(order: ApiSalesOrder, quantities: number[]): Promise<void> {
  const supervisor = apiAs('supervisor');
  const draft = await startDelivery(order);
  const saved = await supervisor.call<ApiDelivery>('PUT', `/api/documents/${draft.id}`, {
    warehouseId: draft.warehouseId,
    partnerId: draft.partnerId,
    purpose: 'Sale',
    remark: null,
    lines: draft.lines.map((line, index) => ({
      productId: line.productId,
      qty: quantities[index],
      uomId: line.uomId,
      remark: null,
      salesOrderLineNo: line.salesOrderLineNo
    }))
  });
  const ready = await submitDelivery(saved);
  await supervisor.command(ready.id, 'post', {}, ready.concurrencyToken);
}

test.describe('README screenshots (SCREENSHOTS=1 only, new database)', () => {
  test.skip(!ENABLED, 'Set SCREENSHOTS=1 to save the README pictures (see e2e/README.md).');

  test('the demo data: stock, orders, price lists, a sale with a return, approvals waiting', async () => {
    test.setTimeout(8 * 60_000);
    // Modules first: purchasing and sales, then returns (it needs both).
    await setPurchasing(true, 'Demo data for the README pictures');
    await setSales(true, 'Demo data for the README pictures');
    await returnsOn();

    for (const name of SUPPLIERS) {
      data.suppliers.set(name, (await admin().createPartner('Supplier', { name })).id);
    }
    for (const name of CUSTOMERS) {
      data.customers.set(name, (await admin().createPartner('Customer', { name })).id);
    }
    for (const [sku, name, safetyStock] of PRODUCTS) {
      data.products.set(sku, await admin().createProduct({ sku, name, safetyStock }));
    }

    // Stock: everything is bought from the two suppliers and part of it sold to the customers.
    const [acme, lakeview] = SUPPLIERS;
    const received = (skus: string[]) =>
      PRODUCTS.filter(row => skus.includes(row[0])).map(row => ({
        productId: product(row[0]).id,
        qty: row[4],
        unitCost: row[3]
      }));
    await admin().post('receipts', received(['TOTE-01', 'TAPE-48', 'MUG-350', 'NB-A5']), {
      partnerId: data.suppliers.get(acme!)!
    });
    await admin().post('receipts', received(['BTL-500', 'PEN-B50', 'CBL-C1', 'LAMP-D2']), {
      partnerId: data.suppliers.get(lakeview!)!
    });
    for (const [index, customer] of CUSTOMERS.entries()) {
      const rows = PRODUCTS.filter((_, i) => i % CUSTOMERS.length === index);
      await admin().post(
        'issues',
        rows.map(row => ({ productId: product(row[0]).id, qty: row[5] })),
        { partnerId: data.customers.get(customer)! }
      );
    }

    // Price lists: the default one and one for Riverside Cafe.
    await createPriceList({
      name: 'Standard retail',
      isDefault: true,
      items: PRODUCTS.map(row => ({ productId: product(row[0]).id, price: row[6] }))
    });
    data.riversideList = (
      await createPriceList({
        name: 'Riverside Cafe wholesale',
        customerId: data.customers.get('Riverside Cafe')!,
        items: Object.entries(RIVERSIDE_PRICES).map(([sku, price]) => ({
          productId: product(sku).id,
          price
        }))
      })
    ).id;

    // A purchase order received in part (the other one waits for the Supervisor: 6,300.00 is over the approval amount).
    const open = await purchaseOrder(
      'Acme Packaging Supplies',
      [
        ['TOTE-01', 200, 2.8],
        ['TAPE-48', 240, 1.1],
        ['MUG-350', 120, 4.2]
      ],
      'Spring restock'
    );
    expect(open.status).toBe('Approved');
    await receiveSome(open, [120, 240, 60]);
    data.purchaseOrder = { id: open.id, no: open.no };
    const waitingPo = await purchaseOrder(
      'Lakeview Wholesale',
      [['LAMP-D2', 1500, 4.2]],
      'Bulk order for the autumn sale'
    );
    expect(waitingPo.status).toBe('Submitted');

    // Sales orders: one Approved and delivered in part, one fully delivered (Closed), one Draft, one waiting for approval.
    const approved = await salesOrder(
      'Riverside Cafe',
      [
        ['MUG-350', 60, 8.9],
        ['BTL-500', 10, 22],
        ['PEN-B50', 12, 26]
      ],
      'Weekly cafe order',
      { submit: true, approve: true }
    );
    await deliverSome(approved, [40, 10, 6]);
    data.salesOrder = { id: approved.id, no: approved.no };
    const closed = await salesOrder('Sunrise Gift Shop', [['TOTE-01', 30, 7.5]], 'Window display', {
      submit: true,
      approve: true
    });
    await deliverSome(closed, [30]);
    await salesOrder('Greenfield Grocers', [['TAPE-48', 40, 2.9]], 'Quote for the new shop', {
      submit: false,
      approve: false
    });
    const waitingSo = await salesOrder(
      'Greenfield Grocers',
      [['LAMP-D2', 100, 59]],
      'Opening week lamps',
      { submit: true, approve: false }
    );
    expect(waitingSo.status).toBe('Submitted');

    // A posted sale of 30 mugs; 6 of them are returned from the screen later (see the return editor picture).
    const sale = await admin().post('issues', [{ productId: product('MUG-350').id, qty: 30 }], {
      partnerId: data.customers.get('Riverside Cafe')!
    });
    data.sale = { id: sale.id, no: sale.no };
    // Issues that go below zero wait for a Supervisor with the stock override (the setting is put back at once).
    const restore = await admin().setNegativeStock(true);
    try {
      for (const [role, sku, qty, customer, remark] of [
        ['staff', 'CBL-C1', 20, 'Greenfield Grocers', 'Counter sale'],
        ['clerk', 'BTL-500', 20, 'Riverside Cafe', 'Bottles for the cafe event'],
        ['staff', 'NB-A5', 90, 'Sunrise Gift Shop', 'School term order']
      ] as const) {
        const short = await apiAs(role).createDocument(
          'issues',
          [{ productId: product(sku).id, qty }],
          { partnerId: data.customers.get(customer)!, remark }
        );
        await apiAs(role).command(short.id, 'submit', {}, short.concurrencyToken);
      }
    } finally {
      await restore();
    }

    // Two drafts of the Supervisor, for the home screen.
    await apiAs('supervisor').createDocument(
      'receipts',
      [{ productId: product('NB-A5').id, qty: 200, unitCost: 1.9 }],
      { partnerId: data.suppliers.get(acme!)!, remark: 'Delivery expected on Monday' }
    );
    await apiAs('supervisor').createDocument(
      'issues',
      [{ productId: product('TAPE-48').id, qty: 60 }],
      { partnerId: data.customers.get('Greenfield Grocers')!, remark: 'Shop opening' }
    );
  });

  test('home, stock enquiry, purchase order (Supervisor, 1280x800)', async ({ browser }) => {
    test.setTimeout(120_000);
    const supervisor = await openSession(browser, 'supervisor', { viewport: DESKTOP });
    try {
      const { page } = supervisor;
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await shot(page, 'home-dashboard');

      await goTo(page, '/stock');
      await expect(page.getByRole('row').filter({ hasText: 'MUG-350' })).toBeVisible();
      await shot(page, 'stock-enquiry');

      await goTo(page, `/purchase-orders/${data.purchaseOrder.id}`);
      await expect(page.getByRole('main')).toContainText(data.purchaseOrder.no);
      await expect(page.getByRole('row').filter({ hasText: 'TOTE-01' })).toBeVisible();
      await shot(page, 'purchase-order-detail');

      await goTo(page, '/approvals');
      await expect(page.getByRole('heading', { level: 1, name: 'Approvals' })).toBeVisible();
      await expect(page.getByRole('row').filter({ hasText: 'Stock override' })).toHaveCount(3);
      await shot(page, 'approvals-inbox');

      await goTo(page, '/sales-orders');
      await expect(page.getByRole('row').filter({ hasText: 'Riverside Cafe' })).toBeVisible();
      await shot(page, 'sales-order-list');

      await goTo(page, `/sales-orders/${data.salesOrder.id}`);
      await expect(page.getByRole('row').filter({ hasText: 'BTL-500' })).toBeVisible();
      await shot(page, 'sales-order-detail');

      await goTo(page, `/price-lists/${data.riversideList}`);
      await expect(page.getByRole('textbox', { name: /^Name/ })).toHaveValue(
        'Riverside Cafe wholesale'
      );
      await shot(page, 'price-list-editor');

      // The return of the sale: 6 of 30 mugs go back first, then a second return is started from what is left.
      await goTo(page, `/issues/${data.sale.id}`);
      await page.getByRole('button', { name: 'Return', exact: true }).click();
      await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
      const editor = new DocumentEditor(page);
      await editor.setQty('MUG-350', 6);
      await editor.action('Save draft').click();
      await expect(page.getByText('Draft saved.')).toBeVisible();
      const firstReturn = new URL(page.url()).pathname.split('/').pop()!;
      // A return always needs approval, never by the person who made it: the Supervisor submits, the Admin approves, the Supervisor posts.
      const submitted = await apiAs('supervisor').command(firstReturn, 'submit', {});
      const approved = await admin().command(
        firstReturn,
        'approve',
        {},
        submitted.concurrencyToken
      );
      await apiAs('supervisor').command(firstReturn, 'post', {}, approved.concurrencyToken);

      await goTo(page, `/issues/${data.sale.id}`);
      await page.getByRole('button', { name: 'Return', exact: true }).click();
      await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
      await expect(page.getByRole('main')).toContainText('already returned 6');
      await editor.setQty('MUG-350', 4);
      await expect(page.getByRole('main')).toContainText('returnable 24');
      await shot(page, 'return-editor');
    } finally {
      await supervisor.context.close();
    }
  });

  test('the sales order editor with a price from the list (Clerk, 1280x800)', async ({
    browser
  }) => {
    test.setTimeout(120_000);
    const clerk = await openSession(browser, 'clerk', { viewport: DESKTOP });
    try {
      const { page } = clerk;
      await goTo(page, '/sales-orders/new');
      const editor = new DocumentEditor(page);
      await editor.partner.selectOption({ label: 'Riverside Cafe' });
      for (const [sku, qty] of [
        ['MUG-350', 48],
        ['LAMP-D2', 6]
      ] as const) {
        await editor.addItem(product(sku));
        await editor.setQty(sku, qty);
        await expect(
          page.getByRole('spinbutton', { name: `Unit price of ${sku}` })
        ).not.toHaveValue('');
      }
      await expect(page.locator('.so-editor-listprice').first()).toBeVisible();
      await shot(page, 'sales-order-editor');
    } finally {
      await clerk.context.close();
    }
  });

  test('Settings, Modules (Admin, 1280x800)', async ({ browser }) => {
    test.setTimeout(120_000);
    const adminSession = await openSession(browser, 'admin', { viewport: DESKTOP });
    try {
      const { page } = adminSession;
      await goTo(page, '/settings/modules');
      await expect(page.getByRole('switch', { name: /Purchasing/i })).toHaveAttribute(
        'aria-checked',
        'true'
      );
      await shot(page, 'settings-modules');
    } finally {
      await adminSession.context.close();
    }
  });

  test('three phone views (360x740)', async ({ browser }) => {
    test.setTimeout(120_000);
    const staff = await phone(browser, 'staff');
    try {
      const { page } = staff;
      await goTo(page, '/stock');
      await expect(page.getByText('MUG-350').first()).toBeVisible();
      await shot(page, 'phone-stock-enquiry');

      // The editor last: leaving it with typed values would ask "Discard changes?".
      const editor = new DocumentEditor(page);
      await editor.openNew('issues');
      await editor.choosePurpose('Sale');
      for (const [sku, qty] of [
        ['MUG-350', 12],
        ['PEN-B50', 5]
      ] as const) {
        await editor.addItem(product(sku));
        await editor.setQty(sku, qty);
      }
      await expect(editor.line('PEN-B50')).toBeVisible();
      // The form is above, the lines are the picture: scroll the Items heading to the middle of the screen.
      await page
        .getByRole('heading', { name: /^Items/ })
        .evaluate(heading => heading.scrollIntoView({ block: 'center' }));
      await shot(page, 'phone-issue-editor', true);
    } finally {
      await staff.context.close();
    }
    const supervisor = await phone(browser, 'supervisor');
    try {
      await goTo(supervisor.page, '/approvals');
      await expect(supervisor.page.getByText('Stock override').first()).toBeVisible();
      await shot(supervisor.page, 'phone-approvals');
    } finally {
      await supervisor.context.close();
    }
  });
});
