import { expect, openSession, test } from '../src/fixtures';
import { DocumentEditor, DocumentListPage } from '../src/pages/documents';
import { ApprovalsPage } from '../src/pages/screens';

/** Issues whose value (qty x cost) reaches the seeded 5,000 approval rule: 60 x 100 = 6,000. */
const COST = 100;
const BIG_QTY = 60;
/** An issue of this many pieces or more needs the Owner (a Large document). */
const LARGE_QTY = 777;

test.describe('RQ4 / NR-02 / PR-02 Correct a document before it posts (R28, R31)', () => {
  test('given staff submitted a receipt with 110 instead of 10, when staff taps Edit items in the Post dialog, then the same document is a Draft that is fixed and posted (NR-02)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const staff = api('staff');
    const wrong = await staff.createDocument('receipts', [{ productId: product.id, qty: 110 }]);
    await staff.command(wrong.id, 'submit');
    const receiptsBefore = await staff.countMine('receipts');
    await signIn('staff');
    const editor = new DocumentEditor(page);

    await editor.open('receipts', wrong.id);
    await expect(editor.status).toHaveText('Submitted');
    await expect(editor.action('Edit')).toBeVisible();
    await editor.action('Post').click();
    await shell
      .dialog(/^Post /)
      .getByRole('button', { name: 'Edit items', exact: true })
      .click();

    await expect(shell.toast('Back to draft. Fix it, then submit again.')).toBeVisible();
    await expect(editor.status).toHaveText('Draft');
    await expect(editor.heading).toHaveText(wrong.no);
    await expect(page.getByText('Returned', { exact: true })).toHaveCount(0);
    await expect(editor.qty(product.sku)).toHaveValue('110');
    expect(await api().onHand(product.id)).toBe(0);

    await editor.setQty(product.sku, 10);
    await editor.action('Submit & post').click();
    await editor.confirm(/^Post /, 'Post');
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(editor.status).toHaveText('Posted');
    expect(await api().onHand(product.id)).toBe(10);
    expect(await staff.countMine('receipts')).toBe(receiptsBefore);
  });

  test('given a submitted receipt, when staff closes the Post dialog with Escape and taps Edit, then it is a Draft again with no dialog (DC-03)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    const staff = api('staff');
    const doc = await staff.createDocument('receipts', [{ productId: product.id, qty: 3 }]);
    await staff.command(doc.id, 'submit');
    await signIn('staff');
    const editor = new DocumentEditor(page);

    await editor.open('receipts', doc.id);
    await editor.action('Post').click();
    await expect(shell.dialog(/^Post /)).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(shell.dialog(/^Post /)).toBeHidden();
    await expect(editor.status).toHaveText('Submitted');
    await expect(editor.action('Edit')).toBeVisible();
    await expect(editor.action('Post')).toBeVisible();

    await editor.action('Edit').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(editor.status).toHaveText('Draft');
    await expect(editor.action('Save draft')).toBeVisible();
    expect((await staff.getDocument(doc.id)).status).toBe('Draft');
  });

  test('given an issue waiting for approval, when its submitter edits it, then it leaves the inbox, and neither the Supervisor nor the Owner is offered Edit (DC-04, DC-06)', async ({
    page,
    browser,
    signIn,
    shell,
    api
  }) => {
    const product = await api().stockedProduct(100, COST);
    const staff = api('staff');
    const doc = await staff.createDocument('issues', [{ productId: product.id, qty: BIG_QTY }]);
    const submitted = await staff.command(doc.id, 'submit');
    expect(submitted.requiresApproval).toBe(true);

    // The wrong role never sees Edit: the approvers of a document are not its submitter.
    const supervisor = await openSession(browser, 'supervisor');
    const review = new DocumentEditor(supervisor.page);
    await review.open('issues', doc.id);
    await expect(review.action('Approve')).toBeVisible();
    await expect(review.action('Edit')).toHaveCount(0);
    const inbox = new ApprovalsPage(supervisor.page);
    await inbox.open();
    await expect(inbox.row(doc.no)).toHaveCount(1);
    const owner = await openSession(browser, 'owner');
    await new DocumentEditor(owner.page).open('issues', doc.id);
    await expect(owner.page.getByRole('button', { name: 'Edit', exact: true })).toHaveCount(0);
    await owner.context.close();

    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.open('issues', doc.id);
    await expect(editor.status).toHaveText('Submitted');
    await expect(page.getByText('Need to change something? Tap Edit.')).toBeVisible();
    await expect(editor.action('Post')).toHaveCount(0);
    await editor.action('Edit').click();
    const dialog = shell.dialog(`Edit ${doc.no}?`);
    await expect(dialog).toContainText('leaves the approval list');
    await dialog.getByRole('button', { name: 'Keep waiting', exact: true }).click();
    await expect(editor.status).toHaveText('Submitted');
    await editor.action('Edit').click();
    await editor.confirm(`Edit ${doc.no}?`, 'Edit');
    await expect(shell.toast('Back to draft. Fix it, then submit again.')).toBeVisible();
    await expect(editor.status).toHaveText('Draft');

    // The approver's list drops it, and a smaller quantity needs no approval any more (DC-06).
    await inbox.open();
    await supervisor.page.reload();
    await expect(inbox.row(doc.no)).toHaveCount(0);
    await supervisor.context.close();

    await editor.setQty(product.sku, 10);
    await editor.action('Submit & post').click();
    await editor.confirm(/^Post /, 'Post');
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    expect(await api().onHand(product.id)).toBe(90);
  });

  test('given an approved issue, when the Supervisor cancels it with a reason, then it is Cancelled, stock is unchanged and its creator is only told whom to ask (R31)', async ({
    page,
    browser,
    signIn,
    shell,
    api
  }) => {
    const product = await api().stockedProduct(100, COST);
    const doc = await api('staff').createDocument('issues', [
      { productId: product.id, qty: BIG_QTY }
    ]);
    await api('staff').command(doc.id, 'submit');
    const approved = await api('supervisor').command(doc.id, 'approve');
    expect(approved.status).toBe('Approved');

    // Its creator has no Cancel and no Edit, and the page names the approver.
    const staff = await openSession(browser, 'staff');
    const staffView = new DocumentEditor(staff.page);
    await staffView.open('issues', doc.id);
    await expect(staffView.status).toHaveText('Approved');
    await expect(staff.page.getByText(/Wrong quantity\? Ask .+ to cancel it\./)).toBeVisible();
    await expect(staffView.action('Edit')).toHaveCount(0);
    await expect(staffView.action('Cancel document')).toHaveCount(0);
    await staff.context.close();

    await signIn('supervisor');
    const editor = new DocumentEditor(page);
    await editor.open('issues', doc.id);
    await expect(editor.status).toHaveText('Approved');
    await editor.action('Cancel document').click();
    const dialog = shell.dialog(`Cancel ${doc.no}?`);
    await expect(dialog).toContainText('Stock does not change');
    await dialog.getByRole('textbox').fill('abc');
    await dialog.getByRole('button', { name: 'Cancel document', exact: true }).click();
    await expect(dialog.getByText('Enter a reason of at least 5 characters.')).toBeVisible();
    await dialog.getByRole('textbox').fill('Customer withdrew the order');
    await dialog.getByRole('button', { name: 'Cancel document', exact: true }).click();

    await expect(shell.toast('Document cancelled.')).toBeVisible();
    await expect(editor.status).toHaveText('Cancelled');
    await expect(page.getByText('Customer withdrew the order')).toBeVisible();
    await expect(editor.action('Post')).toHaveCount(0);
    expect(await api().onHand(product.id)).toBe(100);

    // The list shows the new status.
    await shell.nav.getByRole('link', { name: 'Issues' }).click();
    const list = new DocumentListPage(page, 'issues');
    await list.searchFor(doc.no);
    await expect(list.rows(doc.no)).toContainText('Cancelled');
  });

  test('given an approved Large issue, when the Supervisor and the Owner open it, then only the Owner can cancel it, with a reason, although the Owner can not post (R31)', async ({
    browser,
    api
  }) => {
    const admin = api();
    // One rule for every run (a rule cannot be deleted, and two identical rows would break the unique row-action names
    // that 15-containment checks on the settings screen): found again when it is there, switched off at the end.
    const ruleBody = (isActive: boolean) => ({
      docType: 'Issue',
      amountThreshold: null,
      qtyThreshold: LARGE_QTY,
      approverTier: 'Large',
      isActive
    });
    const rules = await admin.call<
      { id: string; docType: string; qtyThreshold: number | null; roleRequired: string }[]
    >('GET', '/api/approval-rules');
    const known = rules.find(
      r => r.docType === 'Issue' && r.qtyThreshold === LARGE_QTY && r.roleRequired === 'Owner'
    );
    const rule = known
      ? await admin.call<{ id: string }>('PUT', `/api/approval-rules/${known.id}`, ruleBody(true))
      : await admin.call<{ id: string }>('POST', '/api/approval-rules', ruleBody(true));
    try {
      const product = await admin.stockedProduct(2000, 1);
      const doc = await api('staff').createDocument('issues', [
        { productId: product.id, qty: 800 }
      ]);
      const submitted = await api('staff').command(doc.id, 'submit');
      expect(submitted.requiresApproval).toBe(true);
      await api('owner').command(doc.id, 'approve');

      const supervisor = await openSession(browser, 'supervisor');
      const supervisorView = new DocumentEditor(supervisor.page);
      await supervisorView.open('issues', doc.id);
      await expect(supervisorView.status).toHaveText('Approved');
      await expect(supervisorView.action('Cancel document')).toHaveCount(0);
      // The Supervisor may post an approved document (not cancel a Large one); the Owner may cancel it (not post).
      await expect(supervisorView.action('Post')).toBeVisible();
      await supervisor.context.close();

      const owner = await openSession(browser, 'owner');
      const ownerView = new DocumentEditor(owner.page);
      await ownerView.open('issues', doc.id);
      await expect(ownerView.status).toHaveText('Approved');
      await expect(ownerView.action('Post')).toHaveCount(0);
      await ownerView.action('Cancel document').click();
      await ownerView.confirm(
        `Cancel ${doc.no}?`,
        'Cancel document',
        'Order withdrawn by customer'
      );
      await expect(owner.shell.toast('Document cancelled.')).toBeVisible();
      await expect(ownerView.status).toHaveText('Cancelled');
      expect(await admin.onHand(product.id)).toBe(2000);
      await owner.context.close();
    } finally {
      await admin.call('PUT', `/api/approval-rules/${rule.id}`, ruleBody(false));
    }
  });

  test('given an approved issue that stock can no longer cover, when the Supervisor opens it, then one alert says it cannot be posted and offers Cancel document (PR-02, NR-11)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const restore = await api().setNegativeStock(false);
    try {
      const product = await api().stockedProduct(10, 1000);
      const first = await api('staff').createDocument('issues', [
        { productId: product.id, qty: 8 }
      ]);
      const second = await api('staff').createDocument('issues', [
        { productId: product.id, qty: 8 }
      ]);
      await api('staff').command(first.id, 'submit');
      await api('staff').command(second.id, 'submit');
      await api('supervisor').command(first.id, 'approve');
      await api('supervisor').command(second.id, 'approve');
      await api('supervisor').command(first.id, 'post');
      expect(await api().onHand(product.id)).toBe(2);

      await signIn('supervisor');
      const editor = new DocumentEditor(page);
      await editor.open('issues', second.id);
      await expect(editor.status).toHaveText('Approved');
      const alert = page.getByRole('alert').filter({ hasText: "Can't be posted now" });
      await expect(alert).toHaveCount(1);
      await expect(alert).toContainText(`${product.sku} needs 8, but only 2 are on hand`);

      await alert.getByRole('button', { name: 'Cancel document', exact: true }).click();
      await editor.confirm(`Cancel ${second.no}?`, 'Cancel document', 'No stock arrived in time');
      await expect(shell.toast('Document cancelled.')).toBeVisible();
      await expect(editor.status).toHaveText('Cancelled');
      expect(await api().onHand(product.id)).toBe(2);
    } finally {
      await restore();
    }
  });
});
