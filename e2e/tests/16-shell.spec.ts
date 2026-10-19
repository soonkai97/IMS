import type { Page } from '@playwright/test';
import { expect, test } from '../src/fixtures';
import { goTo } from '../src/pages/shell';

// R10 (docs/design/2026-refresh-plan.md): the app shell. Phone tab bar and the sticky action bar above it, the skip link, the thin
// progress bar that waits 300ms before it shows, and the tab strip's ARIA. Layout is measured because a unit test cannot.

const LINES = 8; // enough that the page scrolls and the action bar is stuck
const emptyPage = { items: [], total: 0, page: 1, pageSize: 25, nextCursor: null };

/** Answers every documents list after `delay` ms, so "fast" and "slow" calls do not depend on the API's speed. */
async function answerDocuments(page: Page, delay: number): Promise<void> {
  const documents = /\/api\/documents(\?|$)/;
  await page.unroute(documents);
  await page.route(documents, async route => {
    await new Promise(resolve => setTimeout(resolve, delay));
    await route.fulfill({ json: emptyPage });
  });
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
    const cells = await bar.locator('.shell-tab').evaluateAll(nodes => {
      const boxes = nodes.map(node => node.getBoundingClientRect());
      return {
        count: boxes.length,
        width: Math.min(...boxes.map(box => box.width)),
        height: Math.min(...boxes.map(box => box.height))
      };
    });
    expect(cells.count).toBeGreaterThanOrEqual(4);
    expect(Math.min(cells.width, cells.height)).toBeGreaterThanOrEqual(44);
    await expect(bar.locator('a[aria-current="page"]')).toHaveText(/Receipts/);

    // Directly above the tab bar, and the main action is what a tap at its centre reaches (nothing lies on top of it).
    const actionBar = await page.locator('.document-editor-bar').boundingBox();
    expect((actionBar?.y ?? 0) + (actionBar?.height ?? 0)).toBeCloseTo(barBox?.y ?? 0, 0);
    const submit = page.getByRole('button', { name: /Submit/ });
    await expect(async () => {
      const reached = await submit.evaluate(button => {
        const box = button.getBoundingClientRect();
        const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
        return hit?.closest('button') === button;
      });
      expect(reached).toBe(true);
    }).toPass();
  });
});

test.describe('R10 Action bar at 320x560', () => {
  test.use({ viewport: { width: 320, height: 560 }, hasTouch: true, isMobile: true });

  test('given a filled draft on a 320px phone where the action bar wraps to two rows, when Tab reaches the last line, then its Remove button is fully above the bar', async ({
    page,
    signIn,
    api
  }) => {
    const products = [];
    for (let index = 0; index < LINES; index++) {
      products.push(await api().createProduct());
    }
    const draft = await api('staff').createDocument(
      'receipts',
      products.map(product => ({ productId: product.id, qty: 2, unitCost: 1 }))
    );
    await signIn('staff');
    await goTo(page, `/receipts/${draft.id}`);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await expect(page.getByRole('button', { name: /^Remove / })).toHaveCount(LINES);
    const bar = page.locator('.document-editor-bar');
    // Two rows of buttons: taller than the old fixed 8rem reserve could cover.
    expect((await bar.boundingBox())?.height).toBeGreaterThan(128);

    // For each line: scroll so that its Remove button is half under the bar, stand on the previous control, press Tab. The page
    // must scroll the button fully above the bar (a fixed reserve smaller than the bar leaves part of it underneath).
    for (let index = 0; index < LINES; index++) {
      await page.evaluate(line => {
        const buttons = document.querySelectorAll<HTMLElement>('button[aria-label^="Remove "]');
        const target = buttons[line] as HTMLElement;
        const barTop = document.querySelector('.document-editor-bar')!.getBoundingClientRect().top;
        window.scrollBy(0, target.getBoundingClientRect().bottom - (barTop + 25));
        const all = document.querySelectorAll<HTMLElement>(
          'a[href], button, input, select, textarea'
        );
        const stops = Array.from(all).filter(element => !element.hasAttribute('disabled'));
        stops[stops.indexOf(target) - 1]?.focus({ preventScroll: true });
      }, index);
      await page.keyboard.press('Tab');
      await expect(page.getByRole('button', { name: /^Remove / }).nth(index)).toBeFocused();
      await page.waitForTimeout(300); // the scroll that brings a focused control into view may be smooth

      const barTop = (await bar.boundingBox())?.y ?? 0;
      const bottom = await page.evaluate(() => {
        const box = document.activeElement?.getBoundingClientRect();
        return (box?.y ?? 0) + (box?.height ?? 0);
      });
      expect(bottom, `Remove button ${index + 1} is under the action bar`).toBeLessThanOrEqual(
        barTop
      );
    }
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

    await answerDocuments(page, 0);
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

    await answerDocuments(page, 2500);
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
    await answerDocuments(page, 2500);
    await goTo(page, '/issues');
    await expect(bar(page)).toBeVisible();

    const sweep = () =>
      bar(page).evaluate(element => {
        const style = getComputedStyle(element, '::after');
        return { count: style.animationIterationCount, transform: style.transform };
      });
    expect((await sweep()).count).toBe('1');
    // The 0.01ms animation may not have run its one frame when the bar first shows (the sample then reads the start keyframe):
    // wait until it rests where an unmoving bar rests.
    await expect
      .poll(async () => (await sweep()).transform)
      .toMatch(/^(none|matrix\(1, 0, 0, 1, 0, 0\))$/);
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
