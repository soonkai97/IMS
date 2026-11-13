import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { apiAs, unique, type ApiDocument, type PublicId } from '../src/api';
import { purchasingIsOn, setPurchasing } from '../src/purchasing';
import { CUSTOMER_PREFIX, SKU_PREFIX, salesIsOn, setSales } from '../src/sales';
import { expect, openSession, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';

// Returns on the real API (PL3.18), serial: Return is offered on a Posted sale issue or receipt to a holder of returns.create while
// the module is on; the return opens as a draft of the shared editor (original named and linked, partner and warehouse read, no Add
// item, per-line returnable cap in words, no cost); it needs approval by someone else; a return that would take back more than is
// left is refused in the API's words; Warehouse staff see no money; the layout at 360 and 1280 px. The module states are read first
// and put back last; every document is reversed or cancelled again.
test.describe.configure({ mode: 'serial' });

/** A money figure on this app is written with two decimals ("5.00"); quantities never are. */
const MONEY = /\d[\d,]*\.\d{2}\b/;
const token = unique();
const data = {
  found: { sales: false, purchasing: false, returns: false },
  customer: '' as PublicId,
  supplier: '' as PublicId,
  productId: '' as PublicId,
  sku: `${SKU_PREFIX}R${token}`,
  productName: '',
  sale: undefined as ApiDocument | undefined,
  receipt: undefined as ApiDocument | undefined,
  /** Every document made here, newest last, so the clean-up finds it even when a scenario failed half way. */
  made: [] as PublicId[]
};
const admin = () => apiAs('admin');
const status = (page: Page) => page.locator('app-page-header app-status-chip').first();
const bar = (page: Page, name: string) => page.getByRole('button', { name, exact: true });

async function returnsIsOn(): Promise<boolean> {
  const list = await admin().call<{ code: string; enabled: boolean }[]>('GET', '/api/modules');
  return list.find(item => item.code === 'returns')?.enabled === true;
}
async function setReturns(enabled: boolean, reason: string): Promise<void> {
  if ((await returnsIsOn()) !== enabled) {
    await admin().call('PUT', '/api/modules/returns', { enabled, reason });
  }
}

/** Submits as the Supervisor, approves as the Admin (a return is never approved by its submitter) and posts as the Supervisor. */
async function postReturn(id: PublicId): Promise<void> {
  const supervisor = apiAs('supervisor');
  const submitted = await supervisor.command(id, 'submit', {});
  const approved = await admin().command(id, 'approve', {}, submitted.concurrencyToken);
  await supervisor.command(id, 'post', {}, approved.concurrencyToken);
}

async function startReturn(original: PublicId): Promise<ApiDocument> {
  const draft = await apiAs('supervisor').call<ApiDocument>('POST', '/api/returns', {
    originalDocumentId: original
  });
  data.made.push(draft.id);
  return draft;
}

test.describe('Returns, real API', () => {
  test.beforeAll(async () => {
    data.found = {
      sales: await salesIsOn(),
      purchasing: await purchasingIsOn(),
      returns: await returnsIsOn()
    };
    await setReturns(false, 'E2E: start from off');
    await setSales(true, 'E2E: returns');
    await setPurchasing(true, 'E2E: returns');
    data.customer = (
      await admin().createPartner('Customer', { name: `${CUSTOMER_PREFIX}R${token}` })
    ).id;
    data.supplier = (
      await admin().createPartner('Supplier', { name: `E2E Supplier R${token}` })
    ).id;
    const product = await admin().createProduct({ sku: data.sku });
    data.productId = product.id;
    data.productName = product.name;
    // Stock to sell, bought from the supplier; then a sale of 5 and a receipt of 8 to return from.
    data.receipt = await admin().post(
      'receipts',
      [{ productId: product.id, qty: 30, unitCost: 2 }],
      {
        partnerId: data.supplier
      }
    );
    data.made.push(data.receipt.id);
    data.sale = await admin().post('issues', [{ productId: product.id, qty: 5 }], {
      partnerId: data.customer
    });
    data.made.push(data.sale.id);
  });

  test.afterAll(async () => {
    try {
      // Newest first: a return goes before the document it returns, and the first receipt (the stock) last.
      for (const id of [...data.made].reverse()) {
        const { status: state } = await admin().getDocument(id);
        if (state === 'Posted') {
          await admin()
            .reverse(id, 'E2E: throw-away document')
            .catch(() => undefined);
        } else if (['Draft', 'Submitted', 'Approved'].includes(state)) {
          for (const role of ['supervisor', 'admin'] as const) {
            if ((await apiAs(role).raw('POST', `/api/documents/${id}/cancel`, {})).status < 300) {
              break;
            }
          }
        }
      }
    } finally {
      await setReturns(data.found.returns, 'E2E: put back as found');
      await setPurchasing(data.found.purchasing, 'E2E: put back as found');
      await setSales(data.found.sales, 'E2E: put back as found');
    }
  });

  test('given returns is off, then there is no Return button and the API answers 404; with it on, a Supervisor sees Return on the sale and the Owner (no returns.create) does not', async ({
    page,
    signIn,
    browser
  }) => {
    await signIn('supervisor');
    await goTo(page, `/issues/${data.sale!.id}`);
    await expect(status(page)).toHaveText('Posted');
    await expect(bar(page, 'Return')).toHaveCount(0);
    expect(
      (await apiAs('supervisor').raw('POST', '/api/returns', { originalDocumentId: data.sale!.id }))
        .status
    ).toBe(404);

    await setReturns(true, 'E2E: returns');
    await page.reload();
    await expect(bar(page, 'Return')).toBeVisible();

    const owner = await openSession(browser, 'owner');
    try {
      await goTo(owner.page, `/issues/${data.sale!.id}`);
      await expect(status(owner.page)).toHaveText('Posted');
      await expect(bar(owner.page, 'Return')).toHaveCount(0);
    } finally {
      await owner.context.close();
    }
  });

  test('given a Posted sale of 5, when a Supervisor returns 2 from the Return button, then the draft is a receipt naming the sale, with partner and warehouse read, no cost and 5 returnable, and 6 is said in words', async ({
    page,
    signIn
  }) => {
    await signIn('supervisor');
    await goTo(page, `/issues/${data.sale!.id}`);
    await bar(page, 'Return').click();
    await expect(page).toHaveURL(/\/receipts\/[0-9a-f-]{36}$/);
    data.made.push(page.url().split('/').pop()!);
    const editor = new DocumentEditor(page);

    await expect(page.getByRole('main')).toContainText(`This is a return of ${data.sale!.no}.`);
    await expect(page.getByRole('link', { name: 'Open the original.' })).toHaveAttribute(
      'href',
      new RegExp(`/issues/${data.sale!.id}$`)
    );
    await expect(page.getByRole('main')).toContainText('Customer');
    await expect(page.getByRole('button', { name: 'Add item' })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: /^(Customer|Warehouse)/ })).toHaveCount(0);
    await expect(page.getByRole('main')).toContainText('A return always needs approval');
    await expect(page.getByRole('main')).toContainText('already returned 0 PCS, returnable 5 PCS');
    await expect(page.getByRole('textbox', { name: /Unit cost/ })).toHaveCount(0);
    await expect(page.getByRole('spinbutton', { name: /Unit cost|Cost of/ })).toHaveCount(0);

    await editor.setQty(data.sku, 6);
    await expect(page.getByText('Only 5 PCS of this line can still be returned.')).toBeVisible();
    await editor.setQty(data.sku, 2);
    await editor.action('Save draft').click();
    await expect(page.getByText('Draft saved.')).toBeVisible();
  });

  test('given the return is submitted, then the Supervisor cannot approve it (self-approval), the Admin can, and posting it takes the stock back and shows the figures on the sale', async ({
    page,
    signIn
  }) => {
    const id = data.made[data.made.length - 1];
    await signIn('supervisor');
    await goTo(page, `/receipts/${id}`);
    const editor = new DocumentEditor(page);
    // The bar offers Submit & post; a return needs approval, so it stops at Submitted.
    await editor.submitAndPost();
    await expect(status(page)).toHaveText('Submitted');
    // The submitter's Approve is shown but refused (aria-disabled), with the reason beside it.
    await expect(bar(page, 'Approve')).toHaveAttribute('aria-disabled', 'true');

    const reviewer = await openSession(page.context().browser()!, 'admin');
    try {
      await goTo(reviewer.page, `/receipts/${id}`);
      await reviewer.page.getByRole('button', { name: 'Approve', exact: true }).click();
      await new DocumentEditor(reviewer.page).confirm(/^Approve/, 'Approve');
      await expect(status(reviewer.page)).toHaveText('Approved');
    } finally {
      await reviewer.context.close();
    }

    await page.reload();
    await editor.action('Post').click();
    await editor.confirm(/^Post/, 'Post');
    await expect(status(page)).toHaveText('Posted');

    await goTo(page, `/issues/${data.sale!.id}`);
    await expect(page.getByRole('main')).toContainText('Returned 2 PCS, returnable 3 PCS');
  });

  test('given two drafts of 3 against the 3 left, when the first is posted, then posting the second is ONE alert in the API words with what is returnable, and the draft stays', async ({
    page,
    signIn
  }) => {
    const a = await startReturn(data.sale!.id);
    const b = await startReturn(data.sale!.id);
    const supervisor = apiAs('supervisor');
    const submittedB = await supervisor.command(b.id, 'submit', {});
    const approvedB = await admin().command(b.id, 'approve', {}, submittedB.concurrencyToken);
    await postReturn(a.id);

    await signIn('supervisor');
    await goTo(page, `/receipts/${b.id}`);
    expect(approvedB.status).toBe('Approved');
    await page.getByRole('button', { name: 'Post', exact: true }).click();
    await new DocumentEditor(page).confirm(/^Post/, 'Post');

    const alert = page.locator('app-banner[role="alert"]');
    await expect(alert).toHaveCount(1);
    await expect(alert).toContainText(/more than can (still )?be returned/);
    await expect(alert).toContainText(/returnable|can still be returned/i);
    await expect(page.locator('app-toast')).toHaveCount(0);
    expect((await admin().getDocument(b.id)).status).not.toBe('Posted');
  });

  test('given a Posted receipt, then a supplier return is an issue with the purpose Return shown, not chosen, and Warehouse staff see no money on it', async ({
    page,
    signIn,
    browser
  }) => {
    await signIn('supervisor');
    await goTo(page, `/receipts/${data.receipt!.id}`);
    await bar(page, 'Return').click();
    await expect(page).toHaveURL(/\/issues\/[0-9a-f-]{36}$/);
    const id = page.url().split('/').pop()!;
    data.made.push(id);
    await expect(page.getByRole('main')).toContainText(`This is a return of ${data.receipt!.no}.`);
    await expect(page.getByRole('main')).toContainText('Purpose: Return');
    await expect(page.getByRole('radio')).toHaveCount(0);
    await expect(page.getByRole('main')).toContainText('Supplier');

    const staff = await openSession(browser, 'staff');
    try {
      await goTo(staff.page, `/issues/${id}`);
      await expect(staff.page.getByRole('main')).toContainText('This is a return of');
      await expect(staff.page.getByRole('main')).not.toContainText(MONEY);
    } finally {
      await staff.context.close();
    }
  });

  test.describe('layout and accessibility', () => {
    let draft: ApiDocument;
    test.beforeAll(async () => {
      draft = await startReturn(data.receipt!.id);
    });

    test.describe('360 px phone', () => {
      test.use({ viewport: { width: 360, height: 740 } });

      test('given a phone, then the return editor does not scroll sideways', async ({
        page,
        signIn
      }) => {
        await signIn('supervisor');
        await goTo(page, `/issues/${draft.id}`);
        await expect(page.getByRole('main')).toContainText('This is a return of');
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        ).toBeLessThanOrEqual(0);
      });
    });

    for (const scheme of ['light', 'dark'] as const) {
      for (const width of [360, 1280]) {
        test.describe(`axe, ${scheme}, ${width} px`, () => {
          test.use({ colorScheme: scheme, viewport: { width, height: 740 } });

          test(`given the return and the original, then axe finds no serious or critical violation (${scheme}, ${width})`, async ({
            page,
            signIn
          }) => {
            await signIn('supervisor');
            for (const path of [`/issues/${draft.id}`, `/issues/${data.sale!.id}`]) {
              await goTo(page, path);
              await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
              await page.waitForLoadState('networkidle');
              await page.waitForFunction(() => document.getAnimations().length === 0);
              const { violations } = await new AxeBuilder({ page })
                .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
                .analyze();
              const serious = violations.filter(
                v => v.impact === 'serious' || v.impact === 'critical'
              );
              expect(
                serious.map(
                  v => `${path} ${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(' | ')}`
                )
              ).toEqual([]);
            }
          });
        });
      }
    }
  });

  test('given a sale with a Posted return, then reversing the sale is refused in the API sentence', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await goTo(page, `/issues/${data.sale!.id}`);
    await bar(page, 'Reverse').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('textbox').fill('E2E: reverse the sale');
    await dialog.getByRole('button', { name: 'Reverse', exact: true }).click();

    await expect(page.locator('app-banner[role="alert"]')).toHaveCount(1);
    expect((await admin().getDocument(data.sale!.id)).status).toBe('Posted');
  });

  test('given returns is switched off again, then Return goes and the API answers 404', async ({
    page,
    signIn
  }) => {
    await setReturns(false, 'E2E: returns over');
    await signIn('supervisor');
    await goTo(page, `/issues/${data.sale!.id}`);
    await expect(bar(page, 'Return')).toHaveCount(0);
    expect(
      (await apiAs('supervisor').raw('POST', '/api/returns', { originalDocumentId: data.sale!.id }))
        .status
    ).toBe(404);
  });
});
