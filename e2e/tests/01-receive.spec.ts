import { unique } from '../src/api';
import { expect, test } from '../src/fixtures';
import { DocumentEditor, DocumentListPage } from '../src/pages/documents';

test.describe('IN-01 Receive stock (Warehouse staff)', () => {
  test('given a product without stock, when staff saves a draft receipt, submits and posts it, then exactly one document exists and stock increases', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const staff = api('staff');
    const draftsBefore = await staff.countMine('receipts', 'Draft');
    await signIn('staff');
    const editor = new DocumentEditor(page);

    await editor.openNew('receipts');
    await editor.remark.fill(`E2E receipt ${unique()}`);
    await editor.addItem(product);
    await editor.setQty(product.sku, 12);
    await editor.setUnitCost(product.sku, 3);
    await editor.action('Save draft').click();
    await expect(shell.toast('Draft saved.')).toBeVisible();
    const id = await editor.savedId();
    await expect(editor.status).toHaveText('Draft');

    // NV-01: leaving the saved draft asks nothing, and exactly one new draft exists.
    await shell.nav.getByRole('link', { name: 'Receipts' }).click();
    await expect(page).toHaveURL(/\/receipts$/);
    await expect(shell.dialog('Discard changes?')).toHaveCount(0);
    const { no } = await staff.getDocument(id);
    await expect(new DocumentListPage(page, 'receipts').rows(no)).toContainText('Draft');
    expect(await staff.countMine('receipts', 'Draft')).toBe(draftsBefore + 1);
    expect(await api().onHand(product.id)).toBe(0);

    await editor.open('receipts', id);
    await editor.action('Submit & post').click();
    await editor.confirm(/^Post /, 'Post');
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(editor.status).toHaveText('Posted');
    await expect(editor.action('Save draft')).toHaveCount(0);
    expect(await api().onHand(product.id)).toBe(12);
    expect(await staff.countMine('receipts', 'Draft')).toBe(draftsBefore);
  });

  test('given a filled receipt, when staff backs out of the Post dialog and posts again, then only one document is created (NV-02)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const staff = api('staff');
    const receiptsBefore = await staff.countMine('receipts');
    await signIn('staff');
    const editor = new DocumentEditor(page);

    await editor.openNew('receipts');
    await editor.addItem(product);
    await editor.setQty(product.sku, 5);

    await editor.action('Submit & post').click();
    await expect(shell.dialog(/^Post /)).toBeVisible();
    await shell
      .dialog(/^Post /)
      .getByRole('button', { name: 'Back' })
      .click();
    await expect(shell.dialog(/^Post /)).toBeHidden();
    await expect(editor.status).toHaveText('Submitted');

    await editor.action('Post').click();
    await editor.confirm(/^Post /, 'Post');
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(editor.status).toHaveText('Posted');

    expect(await staff.countMine('receipts')).toBe(receiptsBefore + 1);
    expect(await api().onHand(product.id)).toBe(5);
  });

  test('given a draft with a remark, when staff searches the list by that remark, then the draft is found', async ({
    page,
    signIn,
    api
  }) => {
    const product = await api().createProduct();
    const remark = `E2E remark ${unique()}`;
    const draft = await api('staff').createDocument(
      'receipts',
      [{ productId: product.id, qty: 1 }],
      { remark }
    );
    await signIn('staff');
    const list = new DocumentListPage(page, 'receipts');
    await list.open();
    await list.searchFor(remark);
    await expect(list.rows(draft.no)).toHaveCount(1);

    await list.searchFor(`${remark} other`);
    await expect(page.getByText('No documents match these filters.')).toBeVisible();
  });
});
