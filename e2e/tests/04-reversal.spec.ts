import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';

test.describe('BR-02 Reverse a posted document (Supervisor), UX error E6', () => {
  test('given a later issue used the received stock, when a supervisor reverses the receipt, then the message lists the products and nothing changes until the issue is reversed first', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const first = await api().createProduct();
    const second = await api().createProduct();
    const receipt = await api().post('receipts', [
      { productId: first.id, qty: 10, unitCost: 2 },
      { productId: second.id, qty: 10, unitCost: 2 }
    ]);
    const issue = await api().post('issues', [
      { productId: first.id, qty: 7 },
      { productId: second.id, qty: 3 }
    ]);
    await signIn('supervisor');
    const editor = new DocumentEditor(page);

    await editor.open('receipts', receipt.id);
    await editor.action('Reverse').click();
    const dialog = shell.dialog(`Reverse ${receipt.no}?`);
    await dialog.getByRole('textbox').fill('abc');
    await dialog.getByRole('button', { name: 'Reverse', exact: true }).click();
    await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
    await dialog.getByRole('textbox').fill('Delivery was refused');
    await dialog.getByRole('button', { name: 'Reverse', exact: true }).click();

    const blocked = page.getByRole('alert').filter({ hasText: `Can't reverse ${receipt.no}` });
    await expect(blocked).toContainText(first.sku);
    await expect(blocked).toContainText(second.sku);
    await expect(blocked).toContainText('negative');
    await expect(editor.status).toHaveText('Posted');
    expect(await api().onHand(first.id)).toBe(3);

    // The way out the message names: reverse the later issue first, then the receipt.
    await editor.open('issues', issue.id);
    await editor.action('Reverse').click();
    await editor.confirm(`Reverse ${issue.no}?`, 'Reverse', 'Issued by mistake');
    await expect(shell.toast('Reversed. Stock adjusted.')).toBeVisible();
    expect(await api().onHand(first.id)).toBe(10);

    await editor.open('receipts', receipt.id);
    await editor.action('Reverse').click();
    await editor.confirm(`Reverse ${receipt.no}?`, 'Reverse', 'Delivery was refused');
    await expect(shell.toast('Reversed. Stock adjusted.')).toBeVisible();
    await expect(editor.status).toHaveText('Reversed');
    expect(await api().onHand(first.id)).toBe(0);
  });

  test('given a posted receipt nobody has used, when a supervisor reverses it, then it shows Reversed with the reason and stock returns to zero', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const receipt = await api().post('receipts', [{ productId: product.id, qty: 6, unitCost: 1 }]);
    await signIn('supervisor');
    const editor = new DocumentEditor(page);

    await editor.open('receipts', receipt.id);
    await editor.action('Reverse').click();
    await editor.confirm(`Reverse ${receipt.no}?`, 'Reverse', 'Counted wrong');
    await expect(shell.toast('Reversed. Stock adjusted.')).toBeVisible();
    await expect(editor.status).toHaveText('Reversed');
    await expect(
      page.getByRole('status').filter({ hasText: 'Reversed' }).filter({ hasText: 'Counted wrong' })
    ).toBeVisible();
    await expect(editor.action('Reverse')).toHaveCount(0);
    expect(await api().onHand(product.id)).toBe(0);
  });

  test('given a posted receipt, when its creator (Warehouse staff) opens it, then there is no Reverse and the screen says who can', async ({
    page,
    signIn,
    api
  }) => {
    const product = await api().createProduct();
    // Staff send quantities only; the server would ignore a cost anyway.
    const receipt = await api('staff').post('receipts', [{ productId: product.id, qty: 2 }]);
    await signIn('staff');
    const editor = new DocumentEditor(page);

    await editor.open('receipts', receipt.id);
    await expect(editor.status).toHaveText('Posted');
    await expect(editor.action('Reverse')).toHaveCount(0);
    await expect(
      page.getByText('Wrong quantity? Ask a supervisor to reverse this document.')
    ).toBeVisible();
  });
});
