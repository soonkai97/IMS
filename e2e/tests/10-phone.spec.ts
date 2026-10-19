import type { Page } from '@playwright/test';
import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';

// D1: phone-first. Warehouse staff and clerks work one-handed at 360 px.
test.use({ viewport: { width: 360, height: 740 }, hasTouch: true, isMobile: true });

const hasHorizontalScroll = (page: Page): Promise<boolean> =>
  page.evaluate(
    () =>
      document.documentElement.scrollWidth > window.innerWidth ||
      document.body.scrollWidth > window.innerWidth
  );

test.describe('D1 Phone 360x740', () => {
  test('given a phone, when staff saves a draft receipt, submits and posts it, then exactly one document exists, the buttons are reachable and stock increases', async ({
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
    await editor.addItem(product);
    await editor.setQty(product.sku, 12);
    await expect(editor.action('Save draft')).toBeInViewport();
    await expect(editor.action('Submit & post')).toBeInViewport();
    await editor.action('Save draft').click();
    await expect(shell.toast('Draft saved.')).toBeVisible();
    const id = await editor.savedId();
    expect(await staff.countMine('receipts', 'Draft')).toBe(draftsBefore + 1);

    // The bottom tab bar replaces the sidebar; leaving a saved draft asks nothing (NV-01).
    await shell.nav.getByRole('link', { name: 'Receipts' }).click();
    await expect(page).toHaveURL(/\/receipts$/);
    await expect(shell.dialog('Discard changes?')).toHaveCount(0);

    await editor.open('receipts', id);
    await editor.submitAndPost();
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(editor.status).toHaveText('Posted');
    expect(await api().onHand(product.id)).toBe(12);
    expect(await staff.countMine('receipts', 'Draft')).toBe(draftsBefore);
    expect(await hasHorizontalScroll(page)).toBe(false);
  });

  test('given a phone, then the main screens of Warehouse staff and of a supervisor never scroll sideways', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct({ safetyStock: 5 });
    const posted = await api().post('receipts', [{ productId: product.id, qty: 3, unitCost: 1 }]);
    const issue = await api('staff').createDocument('issues', [{ productId: product.id, qty: 1 }]);

    const screens = [
      '/',
      '/receipts',
      '/receipts/new',
      '/issues',
      `/issues/${issue.id}`,
      `/receipts/${posted.id}`,
      '/stock',
      '/low-stock',
      '/products',
      '/partners'
    ];
    await signIn('staff');
    for (const path of screens) {
      await goTo(page, path);
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      expect(await hasHorizontalScroll(page), `${path} scrolls sideways for Warehouse staff`).toBe(
        false
      );
    }

    await shell.signOut();
    await signIn('supervisor');
    for (const path of ['/', '/approvals', '/reports', '/warehouses']) {
      await goTo(page, path);
      await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      expect(await hasHorizontalScroll(page), `${path} scrolls sideways for the supervisor`).toBe(
        false
      );
    }
  });
});
