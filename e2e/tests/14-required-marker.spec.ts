import { unique } from '../src/api';
import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { PartnersPage } from '../src/pages/screens';
import { spendLoginBudget } from '../src/rate-limit';

// D4 / FR-06 / RQ-01..RQ-06 (docs/design/2026-refresh-ux.md 3.3): the red asterisk is for the eyes, the word "required"
// is for screen readers, the control carries `required`, and one legend "* means required" explains the star per form.

const LEGEND = '* means required';

test.describe('FR-06 Required fields show an asterisk, say "required" once and never print "(required)"', () => {
  test('given the sign-in page, then both fields are named "... required" once, the legend shows once and no "(required)" text exists', async ({
    login,
    page
  }) => {
    await spendLoginBudget(1);
    await login.open();

    // Exact names: the star is aria-hidden, so a screen reader hears the label and "required" and never "star".
    await expect(page.getByRole('textbox', { name: 'Email or username required', exact: true })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Password required', exact: true })).toBeVisible();
    await expect(login.username).toHaveAttribute('required', '');
    await expect(login.passwordBox).toHaveAttribute('required', '');

    await expect(login.legend).toHaveCount(1);
    await expect(login.legend).toBeVisible();
    await expect(page.locator('.ui-form-field-asterisk[aria-hidden="true"]')).toHaveCount(2);
    await expect(page.getByText('(required)')).toHaveCount(0);
  });

  test('given a 320px phone, when a label wraps, then the asterisk stays on the line of the last word (RQ-06)', async ({
    login,
    page
  }) => {
    await page.setViewportSize({ width: 320, height: 700 });
    await spendLoginBudget(1);
    await login.open();

    const placement = await page.evaluate(() => {
      const label = document.querySelector('label.ui-form-field-label');
      const star = label?.querySelector('.ui-form-field-asterisk');
      const text = Array.from(label?.childNodes ?? []).find(
        node => node.nodeType === Node.TEXT_NODE && (node.textContent ?? '').trim().length > 0
      );
      if (!label || !star || !text) {
        return null;
      }
      // Squeeze the label so its words wrap onto several lines, then compare the lines.
      (label as HTMLElement).style.display = 'block';
      (label as HTMLElement).style.width = '5ch';
      const range = document.createRange();
      range.selectNodeContents(text);
      const lines = new Set(Array.from(range.getClientRects()).map(rect => Math.round(rect.top)));
      const lastLine = Math.max(...lines);
      return { lines: lines.size, lastLine, starLine: Math.round(star.getBoundingClientRect().top) };
    });

    expect(placement).not.toBeNull();
    expect(placement?.lines).toBeGreaterThan(1);
    expect(placement?.starLine).toBe(placement?.lastLine);
  });

  test('given a receipt, then Warehouse is required and Supplier and Remark are not, with one legend', async ({
    page,
    signIn
  }) => {
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');

    await expect(page.getByRole('combobox', { name: 'Warehouse required', exact: true })).toHaveAttribute('required', '');
    await expect(page.getByRole('combobox', { name: 'Supplier', exact: true })).not.toHaveAttribute('required', '');
    await expect(page.getByRole('textbox', { name: 'Remark', exact: true })).not.toHaveAttribute('required', '');
    await expect(page.getByText(LEGEND)).toHaveCount(1);
    await expect(page.getByText('(required)')).toHaveCount(0);
  });

  test('given an issue, then Warehouse and the Purpose group are required, the group is named "Purpose required" and the legend shows once', async ({
    page,
    signIn
  }) => {
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('issues');

    await expect(page.getByRole('combobox', { name: 'Warehouse required', exact: true })).toBeVisible();
    await expect(page.getByRole('group', { name: 'Purpose required', exact: true })).toBeVisible();
    await expect(page.getByRole('radio', { name: 'Sale' })).toHaveAttribute('required', '');
    await expect(page.getByText(LEGEND)).toHaveCount(1);
    await expect(page.getByText('(required)')).toHaveCount(0);
  });

  test('given the New partner dialog, then Type and Name are required, Contact is not, and the legend is inside the dialog', async ({
    page,
    signIn
  }) => {
    await signIn('clerk');
    const partners = new PartnersPage(page);
    await partners.open();
    await partners.newButton.click();

    const dialog = page.getByRole('dialog', { name: 'New partner' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Type required', exact: true })).toBeVisible();
    await expect(dialog.getByRole('textbox', { name: 'Name required', exact: true })).toBeVisible();
    await expect(dialog.getByRole('textbox', { name: 'Contact', exact: true })).not.toHaveAttribute('required', '');
    await expect(dialog.getByText(LEGEND)).toHaveCount(1);

    // The asterisk is a picture, not content: filling the form with the star in the label works as before.
    const name = `E2E Supplier ${unique()}`;
    await dialog.getByRole('textbox', { name: 'Name required', exact: true }).fill(name);
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(partners.row(name)).toBeVisible();
  });

  test('given a list page, then no legend and no asterisk are shown', async ({
    page,
    signIn
  }) => {
    await signIn('staff');
    // A list page has filters and a search box only: those are never marked (UX 3.3 item 5).
    await page.getByRole('link', { name: 'Receipts' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Receipts' })).toBeVisible();

    await expect(page.getByText(LEGEND)).toHaveCount(0);
    await expect(page.locator('.ui-form-field-asterisk')).toHaveCount(0);
  });
});
