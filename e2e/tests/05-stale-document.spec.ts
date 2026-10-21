import { expect, openSession, test } from '../src/fixtures';
import { DocumentEditor, DocumentListPage } from '../src/pages/documents';
import { Shell } from '../src/pages/shell';

test.describe('BR-12 Concurrent edits (UX error E4, PS-08)', () => {
  test('given two sessions edit the same draft, when the second saves after the first, then it sees the stale banner, is locked, and Reload shows the first change', async ({
    page,
    browser,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const draft = await api('staff').createDocument(
      'receipts',
      [{ productId: product.id, qty: 3 }],
      {
        remark: 'original'
      }
    );
    await signIn('staff');
    const first = new DocumentEditor(page);
    await first.open('receipts', draft.id);
    const other = await openSession(browser, 'staff');
    const second = new DocumentEditor(other.page);
    await second.open('receipts', draft.id);

    await first.remark.fill('changed by the first session');
    await first.action('Save draft').click();
    await expect(shell.toast('Draft saved.')).toBeVisible();

    await second.remark.fill('changed by the second session');
    await second.action('Save draft').click();
    await expect(second.staleBanner).toContainText('Your changes were not saved');
    await expect(second.staleBanner).toContainText(draft.no);
    await expect(second.action('Save draft')).toBeDisabled();
    await expect(second.action('Submit & post')).toBeDisabled();

    await second.staleBanner.getByRole('button', { name: 'Reload' }).click();
    await expect(second.staleBanner).toHaveCount(0);
    await expect(second.remark).toHaveValue('changed by the first session');
    await expect(second.action('Save draft')).toBeEnabled();
    await other.context.close();
  });

  test('given a stale banner on draft A, when the person goes to the list and opens draft B without a reload, then B shows no banner and its buttons work', async ({
    page,
    browser,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const staff = api('staff');
    const lines = [{ productId: product.id, qty: 3 }];
    const [a, b] = [
      await staff.createDocument('receipts', lines, { remark: 'A' }),
      await staff.createDocument('receipts', lines, { remark: 'B' })
    ];
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.open('receipts', a.id);
    const other = await openSession(browser, 'staff');
    const changer = new DocumentEditor(other.page);
    await changer.open('receipts', a.id);
    await changer.remark.fill('changed by the other session');
    await changer.action('Save draft').click();
    await expect(new Shell(other.page).toast('Draft saved.')).toBeVisible();
    await other.context.close();

    await editor.remark.fill('my change');
    await editor.action('Save draft').click();
    await expect(editor.staleBanner).toBeVisible();
    await expect(editor.action('Save draft')).toBeDisabled();

    // No reload: the root store still holds the failed save of A while the person walks to B.
    await shell.nav.getByRole('link', { name: 'Receipts' }).click();
    await shell.dialog('Discard changes?').getByRole('button', { name: 'Discard' }).click();
    await expect(page).toHaveURL(/\/receipts$/);
    await new DocumentListPage(page, 'receipts')
      .rows(b.no)
      .getByRole('link', { name: b.no })
      .click();
    await expect(editor.heading).toContainText(b.no);
    await expect(editor.status).toHaveText('Draft');
    await expect(editor.staleBanner).toHaveCount(0);
    await expect(editor.action('Save draft')).toBeEnabled();
    await expect(editor.action('Submit & post')).toBeEnabled();

    for (const draft of [a, b]) {
      await staff.command(draft.id, 'cancel', { reason: 'E2E: throw-away draft' });
    }
  });
});
