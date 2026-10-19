import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';

test.describe('OUT-01 Issue stock below available (BR-03, UX error E1)', () => {
  test('given 10 on hand, when staff issues 25, then the line is short with a one-tap fix and submit stays blocked until fixed', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().stockedProduct(10, 2);
    await signIn('staff');
    const editor = new DocumentEditor(page);

    await editor.openNew('issues');
    await editor.choosePurpose('Sale');
    await editor.addItem(product);
    await expect(editor.line(product.sku)).toContainText('10 PCS available');
    await editor.setQty(product.sku, 25);

    // E1: the sentence on the line, a count under the totals, and the primary button blocked.
    const line = editor.line(product.sku);
    await expect(line.getByRole('alert')).toContainText('Only 10 PCS available in Main warehouse.');
    await expect(page.getByText('1 line is short on stock. Fix it to submit.')).toBeVisible();
    await expect(editor.action('Submit & post')).toBeDisabled();

    // One tap sets the quantity to what is there; the warnings go and the button works.
    await line.getByRole('button', { name: 'Set to 10' }).click();
    await expect(editor.qty(product.sku)).toHaveValue('10');
    await expect(line.getByRole('alert')).toHaveCount(0);
    await expect(page.getByText('is short on stock')).toHaveCount(0);
    await expect(editor.action('Submit & post')).toBeEnabled();

    await editor.submitAndPost();
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(editor.status).toHaveText('Posted');
    expect(await api().onHand(product.id)).toBe(0);
  });
});
