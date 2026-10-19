import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { StockPages } from '../src/pages/screens';

test.describe('T16 Low stock (PS-04, NV-13)', () => {
  test('given a product below its safety level, when staff receives enough through the New receipt shortcut, then it leaves the low-stock list', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct({ safetyStock: 20 });
    await signIn('staff');
    const stock = new StockPages(page);
    await stock.openLowStock();

    // A product that was never stocked is listed too, and the row offers a prefilled receipt.
    const row = stock.lowStockRow(product.sku);
    await expect(row).toBeVisible();
    await row.getByRole('link', { name: 'New receipt' }).click();
    const editor = new DocumentEditor(page);
    await expect(page.getByRole('heading', { level: 1, name: 'New receipt' })).toBeVisible();
    await expect(editor.line(product.sku)).toBeVisible();
    await expect(editor.qty(product.sku)).toBeFocused();
    // Wait for the initial value so a late write cannot drop the selection before the keys arrive.
    await expect(editor.qty(product.sku)).toHaveValue('1');

    // NR-01: the shortcut focuses the quantity with 1 in it; typing 25 replaces it (it used to make 125).
    await page.keyboard.type('25');
    await expect(editor.qty(product.sku)).toHaveValue('25');
    await editor.submitAndPost();
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    expect(await api().onHand(product.id)).toBe(25);

    await stock.openLowStock();
    await expect(
      page.getByRole('table', { name: 'Low stock' }).or(page.getByText('Nothing is low on stock.'))
    ).toBeVisible();
    await expect(stock.lowStockRow(product.sku)).toHaveCount(0);
  });
});
