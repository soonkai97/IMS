import type { Page } from '@playwright/test';
import type { ImsApi } from '../src/api';
import type { Role } from '../src/config';
import { expect, test } from '../src/fixtures';
import { DocumentEditor, DocumentListPage } from '../src/pages/documents';

// Phone QA round (novice, 360 px): a new page opens at its top, Submit & post is one clear sequence, Edit asks first, and the
// empty-lines and quantity messages stay next to what they are about.
test.use({ viewport: { width: 360, height: 740 }, hasTouch: true, isMobile: true });

/** Issues whose value (qty x cost) reaches the seeded 5,000 approval rule: 60 x 100 = 6,000. */
const COST = 100;
const BIG_QTY = 60;

const scrollY = (page: Page): Promise<number> => page.evaluate(() => Math.round(window.scrollY));

/** Records what shows on screen while a sequence runs: the dialogs that opened and every moment "Ready to post" was visible. */
async function watchScreen(page: Page): Promise<void> {
  await page.evaluate(() => {
    const seen = { dialogs: [] as string[], readyToPost: 0 };
    (window as unknown as { __seen: typeof seen }).__seen = seen;
    new MutationObserver(() => {
      for (const dialog of Array.from(document.querySelectorAll('dialog[open]'))) {
        const name = dialog.getAttribute('aria-label') ?? dialog.querySelector('h2')?.textContent ?? '';
        if (name && !seen.dialogs.includes(name.trim())) {
          seen.dialogs.push(name.trim());
        }
      }
      if (document.body.innerText.includes('Ready to post')) {
        seen.readyToPost++;
      }
    }).observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true });
  });
}

const screenLog = (page: Page): Promise<{ dialogs: string[]; readyToPost: number }> =>
  page.evaluate(
    () => (window as unknown as { __seen: { dialogs: string[]; readyToPost: number } }).__seen
  );

/** Opens a new receipt and adds `count` products to it, so that the page is long. */
async function fillNewReceipt(
  page: Page,
  api: (role?: Role) => ImsApi,
  count: number
): Promise<DocumentEditor> {
  const editor = new DocumentEditor(page);
  await editor.openNew('receipts');
  for (let i = 0; i < count; i++) {
    await editor.addItem(await api().createProduct());
  }
  return editor;
}

test.describe('Phone QA: scroll, Submit & post, Edit and the empty-lines message', () => {
  for (const reducedMotion of ['no-preference', 'reduce'] as const) {
    test(`given a scrolled list (motion: ${reducedMotion}), when staff opens another page or a document, then it opens at its top with the title in view (QA 1)`, async ({
      page,
      signIn,
      shell,
      api
    }) => {
      await page.emulateMedia({ reducedMotion });
      const product = await api().createProduct();
      const staff = api('staff');
      let last = '';
      for (let i = 0; i < 14; i++) {
        last = (await staff.createDocument('receipts', [{ productId: product.id, qty: i + 1 }])).no;
      }
      await signIn('staff');
      const list = new DocumentListPage(page, 'receipts');
      await list.open();
      await expect(page.getByRole('row').first()).toBeVisible();
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
      await expect.poll(() => scrollY(page)).toBeGreaterThan(400);

      // Another tab of the bar: the new page starts at the top, not where the old list ended.
      await shell.nav.getByRole('link', { name: 'Issues' }).click();
      await expect(page).toHaveURL(/\/issues$/);
      await expect(page.getByRole('heading', { level: 1, name: 'Issues' })).toBeInViewport();
      await expect.poll(() => scrollY(page)).toBe(0);

      // A document opened from the bottom of a list: its title and back link are in view.
      await shell.nav.getByRole('link', { name: 'Receipts' }).click();
      await expect(page.getByRole('row').first()).toBeVisible();
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
      await expect.poll(() => scrollY(page)).toBeGreaterThan(400);
      const row = page.getByRole('row').filter({ hasText: last });
      await row.scrollIntoViewIfNeeded();
      await row.click();
      await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
      await expect(new DocumentEditor(page).heading).toBeInViewport();
      await expect.poll(() => scrollY(page)).toBe(0);
    });
  }

  test('given a warehouse user, when they Submit & post a receipt, then one question comes first, the button says Posting..., no "Ready to post" flashes and no second dialog opens (QA 3)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');
    await editor.addItem(product);
    await editor.setQty(product.sku, 7);
    // A slow post, so the in-between state is on screen long enough to be seen.
    await page.route('**/api/documents/*/post', async route => {
      await new Promise(resolve => setTimeout(resolve, 1500));
      await route.continue();
    });
    await watchScreen(page);

    await editor.action('Submit & post').click();
    const dialog = shell.dialog('Submit & post?');
    await expect(dialog).toContainText('Stock will increase for 1 line.');
    await expect(dialog).toContainText(`Includes ${product.sku} x 7 PCS.`);
    await dialog.getByRole('button', { name: 'Submit & post', exact: true }).click();

    await expect(editor.action('Posting...')).toBeVisible();
    await expect(editor.action('Posting...')).toHaveAttribute('aria-busy', 'true');
    await expect(shell.toast('Posted. Stock updated.')).toBeVisible();
    await expect(editor.status).toHaveText('Posted');
    expect(await api().onHand(product.id)).toBe(7);
    expect(await screenLog(page)).toEqual({ dialogs: ['Submit & post?'], readyToPost: 0 });
  });

  test('given a document that needs approval, when a warehouse user Submit & posts it, then it stops at Submitted and the toast says Submitted for approval (QA 3)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().stockedProduct(100, COST);
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('issues');
    await editor.choosePurpose('Sale');
    await editor.addItem(product);
    await editor.setQty(product.sku, BIG_QTY);
    await watchScreen(page);

    await editor.submitAndPost();

    await expect(shell.toast('Submitted for approval.')).toBeVisible();
    await expect(editor.status).toHaveText('Submitted');
    await expect(editor.action('Post')).toHaveCount(0);
    expect(await api().onHand(product.id)).toBe(100);
    expect(await screenLog(page)).toEqual({ dialogs: ['Submit & post?'], readyToPost: 0 });
  });

  test('given a clerk, when they Submit a receipt, then no question is asked, the result says who it waits for and "Ready to post" is never shown (QA 3)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    await signIn('clerk');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');
    await editor.addItem(product);
    await editor.setQty(product.sku, 4);
    await watchScreen(page);

    await expect(editor.action('Submit & post')).toHaveCount(0);
    await editor.action('Submit').click();

    await expect(shell.toast('Submitted. Waiting for a warehouse user to post.')).toBeVisible();
    await expect(editor.status).toHaveText('Submitted');
    await expect(page.getByText('Waiting for a warehouse user to post.', { exact: true })).toBeVisible();
    await expect(editor.action('Post')).toHaveCount(0);
    expect(await screenLog(page)).toEqual({ dialogs: [], readyToPost: 0 });
  });

  test('given a submitted receipt at 360 px, when its submitter taps Edit, then they are asked first, the answer is announced once and the footer buttons stay on one line (QA 4)', async ({
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

    await editor.action('Edit').click();
    const ask = shell.dialog(`Take ${doc.no} back to draft?`);
    await expect(ask).toBeVisible();
    await ask.getByRole('button', { name: 'Back', exact: true }).click();
    await expect(editor.status).toHaveText('Submitted');
    expect((await staff.getDocument(doc.id)).status).toBe('Submitted');

    await editor.action('Edit').click();
    await editor.confirm(`Take ${doc.no} back to draft?`, 'Back to draft');
    await expect(editor.status).toHaveText('Draft');
    await expect(shell.toast('Back to draft. Fix it, then submit again.')).toHaveCount(1);

    // The footer of the Draft: Save draft, Submit & post and the More button share one row, each label on one line.
    for (const name of ['Save draft', 'Submit & post']) {
      const label = await editor.action(name).locator('.ui-button-face').boundingBox();
      expect(label?.height, `${name} is one line high`).toBeLessThan(30);
    }
    const save = await editor.action('Save draft').boundingBox();
    const submit = await editor.action('Submit & post').boundingBox();
    const more = await page.getByRole('button', { name: 'More actions' }).boundingBox();
    expect(Math.abs((save?.y ?? 0) - (submit?.y ?? 100))).toBeLessThan(2);
    expect(Math.abs((save?.y ?? 0) - (more?.y ?? 100))).toBeLessThan(2);
  });

  test('given an empty receipt, when staff taps Submit & post, then "Add at least one item." is on screen next to Add item, which has the focus (QA 7)', async ({
    page,
    signIn
  }) => {
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');
    await expect(editor.action('Submit & post')).toBeVisible();

    await editor.action('Submit & post').click();

    const message = page.getByText('Add at least one item.', { exact: true });
    await expect(message).toBeInViewport();
    await expect(editor.addItemButton).toBeFocused();
    const button = await editor.addItemButton.boundingBox();
    const text = await message.boundingBox();
    expect((text?.y ?? 0) - ((button?.y ?? 0) + (button?.height ?? 0))).toBeLessThan(40);
  });

  test('given a line with a wrong quantity, when the quantity message appears, then the minus and plus buttons stay where they were (QA 7)', async ({
    page,
    signIn,
    api
  }) => {
    const product = await api().createProduct();
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');
    await editor.addItem(product);
    const field = editor.qty(product.sku);
    // Measured in one frame and inside the line card, so the page scrolling to the field does not count as a move.
    const inCard = (): Promise<number[]> =>
      editor.line(product.sku).evaluate((card, sku) => {
        const top = card.getBoundingClientRect().y;
        return [
          `button[aria-label="Decrease quantity of ${sku}"]`,
          `button[aria-label="Increase quantity of ${sku}"]`,
          `input[aria-label="Quantity of ${sku}"]`
        ].map(selector => (card.querySelector(selector)?.getBoundingClientRect().y ?? 0) - top);
      }, product.sku);
    const before = await inCard();

    await field.fill('0');
    await field.blur();
    await expect(page.getByText('Enter a quantity above 0.')).toBeVisible();

    expect(await inCard()).toEqual(before);
  });

  test('given a long new receipt scrolled to the bottom, when staff saves the draft, then the page stays where it was (the first save only replaces the address)', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    await signIn('staff');
    const editor = await fillNewReceipt(page, api, 6);
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    const before = await scrollY(page);
    expect(before).toBeGreaterThan(400);

    await editor.action('Save draft').click();
    await expect(shell.toast('Draft saved.')).toBeVisible();
    await editor.savedId();

    expect(await scrollY(page)).toBeGreaterThan(before - 150);
  });

  test('given a long new receipt scrolled to the bottom, when staff taps Submit & post and then Back, then nothing moves and the focus is on Submit & post, not on the title', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    await signIn('staff');
    const editor = await fillNewReceipt(page, api, 5);
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    const before = await scrollY(page);
    expect(before).toBeGreaterThan(400);

    await editor.action('Submit & post').click();
    await shell
      .dialog('Submit & post?')
      .getByRole('button', { name: 'Back', exact: true })
      .click();
    await expect(shell.dialog('Submit & post?')).toBeHidden();

    await expect(editor.action('Submit & post')).toBeFocused();
    expect(await scrollY(page)).toBeGreaterThan(before - 150);
  });
});
