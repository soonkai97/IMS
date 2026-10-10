import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { loadPage } from '../src/pages/shell';

test.describe('i18n Language switch keeps the form (PRD 9: English, Chinese, Malay)', () => {
  test('given a half-filled receipt, when the user switches to Chinese, Malay and back, then the text changes and nothing typed is lost', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const product = await api().createProduct();
    await signIn('staff');
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');
    await editor.remark.fill('keep this remark');
    await editor.addItem(product);
    await editor.setQty(product.sku, 7);
    await expect(editor.action('Save draft')).toBeVisible();

    // Labels change with the language, so the form is found by element type: remark = the textarea, quantity = first number box.
    const stillThere = async (): Promise<void> => {
      await expect(page.getByRole('main').locator('textarea')).toHaveValue('keep this remark');
      await expect(page.getByRole('spinbutton').first()).toHaveValue('7');
      await expect(page.getByText(product.sku).first()).toBeVisible();
    };

    await shell.setLanguage('中文');
    await expect(page.getByRole('button', { name: '保存草稿', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save draft', exact: true })).toHaveCount(0);
    await expect(shell.nav.getByRole('link', { name: '首页' })).toBeVisible();
    await stillThere();

    await shell.setLanguage('Bahasa Melayu');
    await expect(page.getByRole('button', { name: 'Simpan draf', exact: true })).toBeVisible();
    await expect(shell.nav.getByRole('link', { name: 'Utama' })).toBeVisible();
    await stillThere();

    await shell.setLanguage('English');
    await expect(editor.action('Save draft')).toBeVisible();
    await expect(shell.nav.getByRole('link', { name: 'Home' })).toBeVisible();
    await stillThere();
    await expect(page).toHaveURL(/\/receipts\/new$/);
  });

  test('given a language was chosen, when the page is reloaded, then the choice is remembered', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('clerk');
    await shell.setLanguage('Bahasa Melayu');
    await expect(shell.nav.getByRole('link', { name: 'Utama' })).toBeVisible();
    await loadPage(page, '/stock');
    await expect(shell.nav.getByRole('link', { name: 'Utama' })).toBeVisible();
  });
});
