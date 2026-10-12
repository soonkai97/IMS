import type { Page } from '@playwright/test';
import { expect, test } from '../src/fixtures';
import { goTo } from '../src/pages/shell';

// R10 (docs/design/2026-refresh-plan.md): the app shell. Phone tab bar and the sticky action bar above it, the skip link, the thin
// progress bar that waits 300ms before it shows, and the tab strip's ARIA. Layout is measured because a unit test cannot.

const emptyPage = { items: [], total: 0, page: 1, pageSize: 25, nextCursor: null };

/** Answers every documents list at once, so a "fast call" does not depend on the API's speed. */
async function answerDocumentsAtOnce(page: Page): Promise<void> {
  await page.route(/\/api\/documents(\?|$)/, route => route.fulfill({ json: emptyPage }));
}

test.describe('R10 Phone shell at 360x740', () => {
  test.use({ viewport: { width: 360, height: 740 }, hasTouch: true, isMobile: true });

  test('given a phone, then the tab bar sits at the bottom with 44px+ cells and marks the current page, and the action bar rests directly above it without covering Submit', async ({
    page,
    signIn
  }) => {
    await signIn('staff');
    await goTo(page, '/receipts/new');
    await expect(page.getByRole('heading', { level: 1, name: 'New receipt' })).toBeVisible();

    const bar = page.locator('nav.shell-tabbar');
    const barBox = await bar.boundingBox();
    expect(barBox?.y ?? 0).toBeCloseTo(740 - (barBox?.height ?? 0), 0);
    const cells = await bar.locator('.shell-tab').evaluateAll(nodes =>
      nodes.map(node => {
        const box = node.getBoundingClientRect();
        return { width: box.width, height: box.height };
      })
    );
    expect(cells.length).toBeGreaterThanOrEqual(4);
    for (const cell of cells) {
      expect(cell.width).toBeGreaterThanOrEqual(44);
      expect(cell.height).toBeGreaterThanOrEqual(44);
    }
    await expect(bar.locator('a[aria-current="page"]')).toHaveText(/Receipts/);

    // Directly above the tab bar, and the main action is what a tap at its centre reaches (nothing lies on top of it).
    const actionBar = await page.locator('.document-editor-bar').boundingBox();
    expect((actionBar?.y ?? 0) + (actionBar?.height ?? 0)).toBeCloseTo(barBox?.y ?? 0, 0);
    const submit = page.getByRole('button', { name: /Submit/ });
    const box = await submit.boundingBox();
    const topmost = await page.evaluate(
      ([x, y]: [number, number]) =>
        document.elementFromPoint(x, y)?.closest('button')?.textContent?.trim(),
      [(box?.x ?? 0) + (box?.width ?? 0) / 2, (box?.y ?? 0) + (box?.height ?? 0) / 2] as [
        number,
        number
      ]
    );
    expect(topmost).toMatch(/Submit/);
  });
});

test.describe('R10 Skip link', () => {
  test('given a keyboard user on a list, when Tab then Enter on the first stop, then focus is in the content and the page is not reloaded', async ({
    page,
    signIn
  }) => {
    await signIn('supervisor');
    await goTo(page, '/receipts');
    await expect(page.getByRole('heading', { level: 1, name: 'Receipts' })).toBeVisible();
    await page.evaluate(() => ((window as unknown as { __kept: boolean }).__kept = true));

    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
    await page.keyboard.press('Enter');

    await expect(page.locator('main#main-content')).toBeFocused();
    expect(new URL(page.url()).pathname).toBe('/receipts');
    expect(await page.evaluate(() => (window as unknown as { __kept?: boolean }).__kept)).toBe(
      true
    );
  });
});

test.describe('R10 Top progress bar', () => {
  const bar = (page: Page) => page.getByRole('progressbar', { name: 'Loading' });

  test('given a slow list, then the bar shows after the delay and goes when the answer comes; a fast one never flashes it', async ({
    page,
    signIn
  }) => {
    await signIn('supervisor');
    await goTo(page, '/issues');
    await expect(page.getByRole('heading', { level: 1, name: 'Issues' })).toBeVisible();

    await answerDocumentsAtOnce(page);
    await page.evaluate(() => {
      const w = window as unknown as { __shown: number };
      w.__shown = 0;
      new MutationObserver(() => {
        if (document.querySelector('.shell-progress')) {
          w.__shown++;
        }
      }).observe(document.body, { childList: true, subtree: true });
    });
    await goTo(page, '/receipts');
    await expect(page.getByRole('heading', { level: 1, name: 'Receipts' })).toBeVisible();
    await page.waitForTimeout(800);
    expect(await page.evaluate(() => (window as unknown as { __shown: number }).__shown)).toBe(0);

    await page.unroute(/\/api\/documents(\?|$)/);
    await page.route(/\/api\/documents(\?|$)/, async route => {
      await new Promise(resolve => setTimeout(resolve, 2500));
      await route.fulfill({ json: emptyPage });
    });
    await goTo(page, '/issues');
    await expect(bar(page)).toBeVisible();
    expect(await bar(page).getAttribute('aria-valuenow')).toBeNull();
    await expect(bar(page)).toBeHidden({ timeout: 6000 });
  });

  test('given reduced motion, when the bar shows, then it does not sweep (one iteration of 0.01ms)', async ({
    page,
    signIn
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await signIn('supervisor');
    await goTo(page, '/receipts');
    await expect(page.getByRole('heading', { level: 1, name: 'Receipts' })).toBeVisible();
    await page.route(/\/api\/documents(\?|$)/, async route => {
      await new Promise(resolve => setTimeout(resolve, 2500));
      await route.fulfill({ json: emptyPage });
    });
    await goTo(page, '/issues');
    await expect(bar(page)).toBeVisible();

    const animation = await bar(page).evaluate(element => {
      const style = getComputedStyle(element, '::after');
      return { count: style.animationIterationCount, transform: style.transform };
    });
    expect(animation.count).toBe('1');
    expect(['none', 'matrix(1, 0, 0, 1, 0, 0)']).toContain(animation.transform);
  });
});

test.describe('R10 Tab strip', () => {
  test('given the Mine / All tabs of Receipts, then each tab controls a tabpanel that names the selected tab', async ({
    page,
    signIn
  }) => {
    await signIn('supervisor');
    await goTo(page, '/receipts');
    const all = page.getByRole('tab', { name: 'All' });
    await all.click();

    const controlled = await all.getAttribute('aria-controls');
    expect(controlled).toBeTruthy();
    const panel = page.locator(`#${controlled}`);
    await expect(panel).toHaveAttribute('role', 'tabpanel');
    await expect(panel).toHaveAttribute('aria-labelledby', (await all.getAttribute('id')) ?? '');
    await expect(panel.getByRole('table')).toBeVisible();
  });
});
