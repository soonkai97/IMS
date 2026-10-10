import { expect, openSession, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { ApprovalsPage, StockPages } from '../src/pages/screens';

test.describe('OUT-02 / BR-07 Issue that needs approval with a stock override', () => {
  let restoreSettings: () => Promise<void>;

  // The override path only exists while "allow negative stock" is on; every scenario here switches it on and restores it.
  test.beforeEach(async ({ api }) => {
    restoreSettings = await api().setNegativeStock(true);
  });
  test.afterEach(async () => {
    await restoreSettings();
  });

  test('given 4 on hand, when staff issues 10, a supervisor approves with a reason and posts, then stock goes to -6 and the ledger shows it', async ({
    page,
    browser,
    signIn,
    shell,
    api
  }) => {
    const product = await api().stockedProduct(4, 2);
    await signIn('staff');
    const editor = new DocumentEditor(page);

    await editor.openNew('issues');
    await editor.choosePurpose('Sale');
    await editor.addItem(product);
    await editor.setQty(product.sku, 10);
    await expect(editor.line(product.sku)).toContainText(
      'Short by 6 PCS. A supervisor must approve this issue.'
    );
    await expect(page.getByText('Override needed')).toBeVisible();
    await expect(editor.action('Submit & post')).toBeEnabled();
    await editor.action('Submit & post').click();
    await expect(shell.toast('Sent for approval.')).toBeVisible();
    await expect(editor.status).toHaveText('Submitted');
    await expect(editor.action('Post')).toHaveCount(0);
    const { no } = await api('staff').getDocument(await editor.savedId());

    // The supervisor finds it in the inbox, flagged, and approves with a reason of at least 5 characters.
    const supervisor = await openSession(browser, 'supervisor');
    const inbox = new ApprovalsPage(supervisor.page);
    await inbox.open();
    await expect(inbox.row(no)).toContainText('Override');
    await inbox.review(no);
    const review = new DocumentEditor(supervisor.page);
    await expect(review.status).toHaveText('Submitted');
    await review.action('Approve').click();
    const dialog = supervisor.shell.dialog(`Approve ${no} with stock override?`);
    await expect(dialog).toContainText(`${product.sku} will go to -6 PCS.`);
    await dialog.getByRole('textbox').fill('abc');
    await dialog.getByRole('button', { name: 'Approve', exact: true }).click();
    await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
    await dialog.getByRole('textbox').fill('Customer collected early');
    await dialog.getByRole('button', { name: 'Approve', exact: true }).click();
    await expect(supervisor.shell.toast('Approved.')).toBeVisible();
    await expect(review.status).toHaveText('Approved');

    await review.action('Post').click();
    await review.confirm(/^Post /, 'Post');
    await expect(supervisor.shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(review.status).toHaveText('Posted');
    expect(await api().onHand(product.id)).toBe(-6);

    const stock = new StockPages(supervisor.page);
    await stock.openStock(product.sku);
    await expect(stock.stockRow(product.sku)).toContainText('-6 PCS');
    await stock.openProductLedger(product.sku);
    await expect(stock.ledgerRows().filter({ hasText: no })).toContainText('-10');
    await supervisor.context.close();
  });

  test('given a supervisor submitted an override issue, when the same supervisor opens it, then Approve is disabled with the reason (BR-07, E3)', async ({
    page,
    signIn,
    api
  }) => {
    const product = await api().stockedProduct(1, 2);
    const own = await api('supervisor').createDocument('issues', [
      { productId: product.id, qty: 5 }
    ]);
    await api('supervisor').command(own.id, 'submit');
    await signIn('supervisor');
    const editor = new DocumentEditor(page);
    await editor.open('issues', own.id);

    await expect(editor.action('Approve')).toHaveAttribute('aria-disabled', 'true');
    await expect(
      page.getByText("You submitted this document, so you can't approve it.")
    ).toBeVisible();
    await editor.action('Approve').click({ force: true });
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(editor.status).toHaveText('Submitted');
    await expect(editor.action('Post')).toHaveCount(0);
  });
});
