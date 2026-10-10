import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';

// A Bluetooth scanner types the code and presses Enter (design D8). NV-03 / PS-05: the search box is focused on open.
test.describe('D8 Barcode and SKU scan in Add item', () => {
  test('given an active product, when the user scans its barcode or types its SKU and presses Enter, then the line is added or its quantity grows', async ({
    page,
    signIn,
    api
  }) => {
    const product = await api().createProduct();
    const barcode = product.barcodes[0]!;
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');

    const sheet = await editor.openAddItem();
    const search = sheet.getByRole('searchbox', { name: 'Find a product' });
    await expect(search).toBeFocused();

    await page.keyboard.type(barcode);
    await page.keyboard.press('Enter');
    await expect(sheet.getByText(`${product.sku} added.`)).toBeVisible();
    await expect(editor.qty(product.sku)).toHaveValue('1');

    await search.fill(product.sku);
    await page.keyboard.press('Enter');
    await expect(sheet.getByText(`${product.sku}: now 2.`)).toBeVisible();
    await expect(editor.qty(product.sku)).toHaveValue('2');
    await expect(page.getByText('Items (1)')).toBeVisible();
  });

  test('given a code that belongs to no product or to a deactivated one, when it is scanned, then a quiet message explains and no line is added', async ({
    page,
    signIn,
    api
  }) => {
    const inactive = await api().createProduct({ isActive: false });
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('issues');

    const sheet = await editor.openAddItem();
    const search = sheet.getByRole('searchbox', { name: 'Find a product' });
    await search.fill('0000000000000');
    await page.keyboard.press('Enter');
    await expect(sheet.getByText('No product has the code 0000000000000.')).toBeVisible();

    await search.fill(inactive.barcodes[0]!);
    await page.keyboard.press('Enter');
    await expect(
      sheet.getByText(`${inactive.sku} is deactivated and can't be added.`)
    ).toBeVisible();

    await sheet.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByText('Items (0)')).toBeVisible();
  });
});
