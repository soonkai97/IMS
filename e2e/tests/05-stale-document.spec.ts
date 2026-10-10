import { expect, openSession, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';

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
});
