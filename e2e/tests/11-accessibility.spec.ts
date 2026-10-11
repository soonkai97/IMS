import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { expect, test } from '../src/fixtures';
import { DocumentEditor } from '../src/pages/documents';
import { goTo } from '../src/pages/shell';
import type { Role } from '../src/config';

/**
 * Known exceptions: "<screen>|<axe rule id>" -> the QA finding that tracks it (docs/qa). Empty today: an entry needs a
 * finding id, and is deleted when the finding is fixed.
 */
const KNOWN: Record<string, string> = {};

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

type Scheme = 'light' | 'dark';

/** Serious and critical axe violations of the current screen that are not in KNOWN, as readable lines. */
async function audit(page: Page, screen: string, scheme: Scheme): Promise<string[]> {
  await page.waitForLoadState('networkidle');
  // The palette really switched: _themes.scss sets color-scheme with the colours, so a screen audited "in dark" was dark.
  const applied = await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme);
  expect(applied, `${screen}: colour scheme in use`).toBe(scheme);
  const { violations } = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return violations
    .filter(v => (v.impact === 'serious' || v.impact === 'critical') && !KNOWN[`${screen}|${v.id}`])
    .map(
      v =>
        `${screen}: ${v.id} (${v.impact}) ${v.help} -> ${v.nodes
          .slice(0, 3)
          .map(n => n.target.join(' '))
          .join(' | ')}`
    );
}

const SCREENS: Record<string, { role: Role; paths: string[] }> = {
  'Warehouse staff': {
    role: 'staff',
    paths: [
      '/',
      '/receipts',
      '/issues',
      '/stock',
      '/low-stock',
      '/products',
      '/partners',
      '/warehouses'
    ]
  },
  Supervisor: { role: 'supervisor', paths: ['/', '/approvals', '/reports'] },
  Admin: { role: 'admin', paths: ['/users', '/settings', '/audit-log', '/catalog'] }
};

// R01: every screen is audited in both palettes. The OS preference is emulated by the browser context before the first
// load (no theme is stored), the same as a user whose device is set to light or dark.
for (const scheme of ['light', 'dark'] as const) {
  test.describe(`PRD 9 / WCAG 2.2 AA: axe finds no serious or critical violations (${scheme})`, () => {
    test.use({ colorScheme: scheme });

    test('given the sign-in screen, then it is clean', async ({ page, login }) => {
      await login.open();
      expect(await audit(page, 'login', scheme)).toEqual([]);
    });

    for (const [label, { role, paths }] of Object.entries(SCREENS)) {
      test(`given ${label} browses its main screens, then every one is clean`, async ({
        page,
        signIn
      }) => {
        await signIn(role);
        const problems: string[] = [];
        for (const path of paths) {
          await goTo(page, path);
          await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
          problems.push(...(await audit(page, path, scheme)));
        }
        expect(problems, problems.join('\n')).toEqual([]);
      });
    }

    test('given a draft issue and a new receipt with the Add item sheet open, then the editors are clean', async ({
      page,
      signIn,
      api
    }) => {
      const product = await api().stockedProduct(5, 1, { safetyStock: 10 });
      const draft = await api('staff').createDocument('issues', [
        { productId: product.id, qty: 1 }
      ]);
      await signIn('staff');
      const editor = new DocumentEditor(page);
      const problems: string[] = [];

      await editor.open('issues', draft.id);
      problems.push(...(await audit(page, 'issue editor', scheme)));
      await editor.openNew('receipts');
      await editor.addItem(product);
      problems.push(...(await audit(page, 'receipt editor', scheme)));
      const sheet = await editor.openAddItem();
      await sheet.getByRole('searchbox').fill(product.sku);
      await expect(sheet.getByRole('button', { name: new RegExp(product.sku) })).toBeVisible();
      problems.push(...(await audit(page, 'add item sheet', scheme)));
      expect(problems, problems.join('\n')).toEqual([]);
    });
  });
}
