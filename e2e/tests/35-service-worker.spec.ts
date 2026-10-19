import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../src/fixtures';
import { API_URL } from '../src/config';
import { unique } from '../src/api';
import { DocumentEditor } from '../src/pages/documents';
import { goTo, loadPage } from '../src/pages/shell';

// The only scenarios that run with the service worker on (fixtures.ts blocks it everywhere else, because it would answer
// assets/config.json and bypass the route that points the app at IMS_API_URL). It needs a production build served with
// `API_URL=<the API> npm run serve:prod` (web/scripts/serve-dist.mjs answers assets/config.json); on the dev server the worker
// is off, so the scenarios skip themselves.
test.use({ serviceWorkers: 'allow' });

test.describe('PWA service worker never answers an API call (QA round S1 and defect 2)', () => {
  test.beforeEach(async ({ request, baseURL }) => {
    const manifest = await request.get(`${baseURL}/ngsw.json`);
    test.skip(
      !manifest.ok() || !(manifest.headers()['content-type'] ?? '').includes('json'),
      'No service worker in this build: serve the production build (npm run build:prod, npm run serve:prod).'
    );
  });

  /** The worker takes control of the page some seconds after the app is stable; every answer below needs that. */
  async function controlledByWorker(page: Page): Promise<void> {
    await expect
      .poll(() => page.evaluate(() => navigator.serviceWorker?.controller !== null), {
        timeout: 45_000,
        intervals: [500]
      })
      .toBe(true);
  }

  const dataCaches = (page: Page): Promise<string[]> =>
    page.evaluate(async () => (await caches.keys()).filter(name => name.includes(':data:')));

  test('given the worker is active and master data was browsed, when the user signs out, then the same API addresses go to the server and are refused (S1)', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await controlledByWorker(page);

    const screens: [path: string, title: string][] = [
      ['/products', 'Products'],
      ['/partners', 'Partners'],
      ['/receipts/new', 'New receipt']
    ];
    for (const [path, title] of screens) {
      await goTo(page, path);
      await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible();
    }
    // The page's own resource timings name every address it asked for, also those the worker answered (request events do not).
    const browsed = await page.evaluate(
      api => [
        ...new Set(
          performance
            .getEntriesByType('resource')
            .map(entry => entry.name)
            .filter(name => name.startsWith(`${api}/api/`) && !name.includes('/api/auth/'))
        )
      ],
      API_URL
    );
    expect(browsed.filter(url => /\/api\/(products|partners)/.test(url)).length).toBeGreaterThan(0);
    // Nothing the app read is kept in a worker cache that outlives the session.
    expect(await dataCaches(page)).toEqual([]);

    await shell.signOut();

    const answers = await page.evaluate(
      async urls =>
        Promise.all(
          urls.map(async url => ({
            url,
            status: (await fetch(url, { cache: 'no-store' })).status
          }))
        ),
      [...browsed, `${API_URL}/api/units`, `${API_URL}/api/settings`]
    );
    expect(answers.filter(answer => answer.status !== 401)).toEqual([]);
    expect(await dataCaches(page)).toEqual([]);
  });

  test('given the worker is active, when a product is created elsewhere after a search found nothing, then Add item finds it at once (defect 2)', async ({
    page,
    signIn,
    api
  }) => {
    await signIn('admin');
    await controlledByWorker(page);
    const sku = `E2E-FRESH-${unique()}`;
    const editor = new DocumentEditor(page);
    await editor.openNew('receipts');
    const searchBox = (sheet: Locator) => sheet.getByRole('searchbox', { name: 'Find a product' });

    // The first search is answered by the server (the sku exists nowhere yet): wait for that answer, so it is a real read of the address.
    const sheet = await editor.openAddItem();
    const firstAnswer = page.waitForResponse(
      response => response.url().includes('/api/products') && response.url().includes(sku)
    );
    await searchBox(sheet).fill(sku);
    await firstAnswer;
    await expect(sheet.getByText(`No product matches "${sku}".`)).toBeVisible();

    const product = await api().createProduct({ sku });

    // The page is loaded again and asks for the very same address; an answer kept from the first search would still say "no product".
    await loadPage(page, '/receipts/new');
    const again = await editor.openAddItem();
    await searchBox(again).fill(sku);

    await expect(again.getByRole('button', { name: `${sku}, ${product.name}` })).toBeVisible();
  });
});
