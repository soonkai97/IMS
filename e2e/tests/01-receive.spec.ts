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
    // Warehouse staff receive by quantity only: no unit cost input at all (cost.view, docs/design/mvp-ux.md 1.4).
    await expect(editor.unitCost(product.sku)).toHaveCount(0);
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
    await editor.submitAndPost();
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(editor.status).toHaveText('Posted');
    await expect(editor.action('Save draft')).toHaveCount(0);
    expect(await api().onHand(product.id)).toBe(12);
    expect(await staff.countMine('receipts', 'Draft')).toBe(draftsBefore);
  });

  test('given the default quantity 1, when staff taps the field and types 12, then the quantity is 12 not 112 (NR-01) and a decimal can be erased with Backspace (NR-08)', async ({
    page,
    signIn,
    api
  }) => {
    const product = await api().createProduct();
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');
    await editor.addItem(product);
    const qty = editor.qty(product.sku);
    await expect(qty).toHaveValue('1');

    await qty.click();
    await page.keyboard.type('12');
    await expect(qty).toHaveValue('12');

    await page.keyboard.press('Tab');
    await qty.click();
    await page.keyboard.type('1.5');
    await expect(qty).toHaveValue('1.5');
    await page.keyboard.press('Backspace');
    await page.keyboard.press('Backspace');
    await page.keyboard.press('Backspace');
    await expect(qty).toHaveValue('');

    // The point is not eaten while typing: 1.5, Backspace, 7 gives 1.7 (the old field gave 17).
    await page.keyboard.type('1.5');
    await page.keyboard.press('Backspace');
    await page.keyboard.type('7');
    await expect(qty).toHaveValue('1.7');
  });

  test('given a saved draft, when staff double clicks Save draft, then exactly one save is sent and no stale-document banner shows (NR-07)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const draft = await api('staff').createDocument(
      'receipts',
      [{ productId: product.id, qty: 1 }],
      { remark: `E2E double save ${unique()}` }
    );
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.open('receipts', draft.id);
    await editor.setQty(product.sku, 4);
    const saves: string[] = [];
    page.on('request', request => {
      if (request.method() === 'PUT' && request.url().includes('/api/documents/')) {
        saves.push(request.url());
      }
    });

    await editor.action('Save draft').dblclick();
    await expect(shell.toast('Draft saved.')).toHaveCount(1);
    await expect(page.getByText('Someone else changed this document')).toHaveCount(0);
    expect(saves).toHaveLength(1);
  });

  test('given a filled receipt, when staff closes the Submit & post dialog and tries again, then only one document is created (NV-02)', async ({
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

    // The one question comes before anything is sent: closing it leaves the form as it was and creates nothing (phone QA 2).
    await editor.action('Submit & post').click();
    const dialog = shell.dialog('Submit & post?');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Back', exact: true })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(editor.action('Save draft')).toBeVisible();
    expect(await staff.countMine('receipts')).toBe(receiptsBefore);

    await editor.submitAndPost();
    await expect(shell.dialog(/^Post /)).toHaveCount(0);
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
