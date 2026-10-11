import type { Page } from '@playwright/test';
import { expect, test } from '../src/fixtures';
import { DocumentEditor, DocumentListPage } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';
import { StockPages } from '../src/pages/screens';

// EA-23, docs/design/mvp-ux.md 1.4: cost.view (Clerk, Supervisor, Owner, Admin) and valuation.view (Supervisor, Owner,
// Admin). A money figure on this app is always written with two decimals ("145.00"); quantities never are in these tests.
const MONEY = /\d[\d,]*\.\d{2}\b/;

const PRICE = 7.25;
const STOCKED = 20;
const RECEIPT_QTY = 3;
const RECEIPT_COST = 4.5;

async function expectNoMoney(page: Page): Promise<void> {
  await expect(page.getByRole('main')).not.toContainText(MONEY);
}

async function openHome(page: Page): Promise<void> {
  await goTo(page, '/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
}

test.describe('EA-23 Cost and value visibility', () => {
  test('given Warehouse staff, then no cost or value shows on the receipt, issue, stock, home and report screens', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().stockedProduct(STOCKED, PRICE);
    const posted = await api().post('receipts', [
      { productId: product.id, qty: RECEIPT_QTY, unitCost: RECEIPT_COST }
    ]);
    const issue = await api('staff').createDocument('issues', [{ productId: product.id, qty: 2 }]);
    await signIn('staff');
    const editor = new DocumentEditor(page);

    // Receipt editor: the quantity, no unit cost, no amount.
    await editor.openNew('receipts');
    await editor.addItem(product);
    await editor.setQty(product.sku, RECEIPT_QTY);
    await expect(editor.qty(product.sku)).toBeVisible();
    await expect(editor.unitCost(product.sku)).toHaveCount(0);
    await expect(page.getByText('Unit cost')).toHaveCount(0);
    await expect(editor.line(product.sku)).toBeVisible();
    await expectNoMoney(page);
    // Saved, so leaving the editor asks nothing; the saved draft shows no amount either.
    await editor.action('Save draft').click();
    await expect(shell.toast('Draft saved.')).toBeVisible();
    await expectNoMoney(page);

    // Receipt list and a posted receipt: no Amount column, no Amount line, no unit cost next to the quantity.
    const receipts = new DocumentListPage(page, 'receipts');
    await receipts.open();
    await receipts.searchFor(posted.no);
    await expect(receipts.rows(posted.no)).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Amount' })).toHaveCount(0);
    await expectNoMoney(page);
    await editor.open('receipts', posted.id);
    await expect(page.getByText('Amount', { exact: true })).toHaveCount(0);
    await expectNoMoney(page);

    // Issue list and draft issue.
    const issues = new DocumentListPage(page, 'issues');
    await issues.open();
    await expect(page.getByRole('columnheader', { name: 'Amount' })).toHaveCount(0);
    await expectNoMoney(page);
    await editor.open('issues', issue.id);
    await expect(page.getByText('Amount', { exact: true })).toHaveCount(0);
    await expectNoMoney(page);

    // Stock: no Value column, nothing to sort by value, no export; the product page carries no cost either.
    const stock = new StockPages(page);
    await stock.openStock(product.sku);
    await expect(stock.stockRow(product.sku)).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Value' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Export Excel' })).toHaveCount(0);
    await expectNoMoney(page);
    await stock.openProductLedger(product.sku);
    await expectNoMoney(page);
    await stock.openLowStock();
    await expect(page.getByRole('button', { name: 'Export Excel' })).toHaveCount(0);
    await expectNoMoney(page);

    // Home: counts and low stock, no Stock value tile.
    await openHome(page);
    await expect(page.getByText('Low stock', { exact: true }).first()).toBeVisible();
    await expect(page.getByText('Stock value')).toHaveCount(0);
    await expectNoMoney(page);

    // Reports and the exports behind them are not for Warehouse staff (403 on the API): no menu entry, polite page.
    expect(await shell.menu()).not.toContain('Reports');
    await goTo(page, '/reports');
    await expect(page).toHaveURL(/\/forbidden$/);
  });

  test('given a Clerk, then the receipt shows the unit cost and the total, and no stock value is shown anywhere', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().stockedProduct(STOCKED, PRICE);
    await signIn('clerk');
    const editor = new DocumentEditor(page);

    await editor.openNew('receipts');
    await editor.addItem(product);
    await editor.setQty(product.sku, RECEIPT_QTY);
    await editor.setUnitCost(product.sku, RECEIPT_COST);
    await expect(page.getByRole('main')).toContainText('13.50');
    await editor.action('Save draft').click();
    await expect(shell.toast('Draft saved.')).toBeVisible();
    const id = await editor.savedId();

    // The saved draft keeps its cost, and the list shows the amount.
    await editor.open('receipts', id);
    await expect(editor.unitCost(product.sku)).toHaveValue(String(RECEIPT_COST));
    const receipts = new DocumentListPage(page, 'receipts');
    await receipts.open();
    await expect(page.getByRole('columnheader', { name: 'Amount' })).toBeVisible();
    const { no } = await api('clerk').getDocument(id);
    await receipts.searchFor(no);
    await expect(receipts.rows(no)).toContainText('13.50');

    // Cost is not value: no Value column, no sort by value, no export, no Stock value tile.
    const stock = new StockPages(page);
    await stock.openStock(product.sku);
    await expect(stock.stockRow(product.sku)).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Value' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Export Excel' })).toHaveCount(0);
    await expectNoMoney(page);
    await openHome(page);
    await expect(page.getByText('Stock value')).toHaveCount(0);
    expect(await shell.menu()).not.toContain('Reports');
  });

  test('given a Supervisor, then stock shows value and can be sorted by it, and the movements report shows the signed stock effect, the amount and the reversal row', async ({
    page,
    signIn,
    api
  }) => {
    const product = await api().stockedProduct(STOCKED, PRICE);
    // Another product, so the reversal cannot move the average cost of the one whose value is asserted.
    const other = await api().createProduct();
    const reversed = await api().post('receipts', [{ productId: other.id, qty: 5, unitCost: 2 }]);
    await api().reverse(reversed.id, 'E2E reversal for the stock effect');
    const { no } = await api().getDocument(reversed.id);
    await signIn('supervisor');

    // Stock: value column with the value, sortable, no error from the API.
    const stock = new StockPages(page);
    await stock.openStock(product.sku);
    await expect(stock.stockRow(product.sku)).toContainText('145.00');
    await page.getByRole('columnheader', { name: 'Value' }).getByRole('button').click();
    await expect(stock.stockRow(product.sku)).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
    await openHome(page);
    await expect(page.getByText('Stock value')).toBeVisible();

    // Movements report: both rows of the reversed receipt, the posting +5 and the reversal -5 with its badge.
    await goTo(page, '/reports');
    await expect(page.getByRole('heading', { level: 1, name: 'Reports' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Quantity' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Stock effect' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Amount' })).toBeVisible();
    // PR-06: Product, Unit and Unit cost are on the screen for a role with cost.view.
    for (const name of ['Product', 'Unit', 'Unit cost (per base unit)']) {
      await expect(page.getByRole('columnheader', { name, exact: true })).toBeVisible();
    }
    const rows = page.getByRole('row').filter({ hasText: no });
    await expect(rows).toHaveCount(2);
    const reversal = rows.filter({ hasText: 'Reversal' });
    await expect(reversal).toHaveCount(1);
    await expect(reversal).toContainText('-5');
    await expect(rows.filter({ hasNotText: 'Reversal' })).toContainText('+5');
    await expect(rows.filter({ hasNotText: 'Reversal' })).toContainText('10.00');
    await expect(page.getByRole('button', { name: 'Export Excel' }).first()).toBeVisible();
  });
});
