import { randomUUID } from 'node:crypto';
import { expect, test } from '../src/fixtures';
import { goTo } from '../src/pages/shell';

// SEC-11 and the public-id plan (docs/architecture/adr-guid-keys.md 3.2 item 2 and 6.2): an id in the address is opaque. An old integer
// bookmark, an id that names nothing and the id of a document the user may not see are all the same page, and the first needs no request.
const NOT_FOUND = "This page or document doesn't exist or was removed.";

test.describe('SEC-11 Ids in the address are opaque', () => {
  // A request that names a record by its id: /api/documents/<id>, /api/products/<id> or /api/stock?productId=<id>.
  const namesARecord = (url: URL): boolean =>
    /\/api\/(documents|products)\/[0-9a-f-]+$/i.test(url.pathname) ||
    url.searchParams.has('productId');

  for (const path of ['/receipts/31', '/issues/31', '/products/31', '/stock/31']) {
    test(`given an old bookmark ${path} with an integer id, when staff opens it, then it is the not-found page and no record is requested`, async ({
      page,
      signIn
    }) => {
      await signIn('staff');
      const asked: string[] = [];
      page.on('request', request => {
        if (namesARecord(new URL(request.url()))) {
          asked.push(request.url());
        }
      });

      await goTo(page, path);

      await expect(page.getByText(NOT_FOUND)).toBeVisible();
      expect(asked).toEqual([]);
    });
  }

  test('given the id of a document in a warehouse the user is not assigned to, when staff opens it, then the page is the one for an id that names nothing', async ({
    page,
    signIn,
    api
  }) => {
    const warehouse = await api().createWarehouse();
    const product = await api().createProduct();
    const hidden = await api().createDocument('receipts', [{ productId: product.id, qty: 1 }], {
      warehouseId: warehouse.id
    });
    await signIn('staff');

    await goTo(page, `/receipts/${randomUUID()}`);
    await expect(page.getByText(NOT_FOUND)).toBeVisible();
    const unknown = (await page.locator('main').textContent()) ?? '';

    // The editor stays mounted between the two addresses, so the first message is still on screen until the second answer arrives:
    // wait for the API's answer (a 404, the same as for an id that names nothing), then for the page to read exactly as before.
    const answered = page.waitForResponse(
      response => new URL(response.url()).pathname === `/api/documents/${hidden.id}`
    );
    await goTo(page, `/receipts/${hidden.id}`);
    expect((await answered).status()).toBe(404);
    await expect(page.locator('main')).toHaveText(unknown);
    await expect(page.getByText(hidden.no)).toHaveCount(0);
  });
});
