import type { Locator, Page } from "@playwright/test";
import { expect, openSession, test } from "../src/fixtures";
import type { Role } from "../src/config";
import { LONG_SENTENCE, announcedBy, failCalls, isReachable, overlap } from "../src/feedback";
import { goTo } from "../src/pages/shell";

// R11 (ruling C6 v3): on a phone an error is an in-flow banner that pushes the page down; a dialog shows its own copy.
const VIEWPORTS = [
  { width: 360, height: 740 },
  { width: 320, height: 568 },
  { width: 320, height: 256 },
];

/** Each list page, its list call and the primary actions its role has (every one found is checked). */
const LIST_PAGES: readonly { role: Role; path: string; list: RegExp; actions: string[] }[] = [
  { role: "supervisor", path: "/receipts", list: /\/api\/documents\?/, actions: ["New receipt"] },
  { role: "admin", path: "/partners", list: /\/api\/partners\?/, actions: ["New partner"] },
  { role: "admin", path: "/warehouses", list: /\/api\/warehouses(\?|$)/, actions: ["New warehouse"] },
  { role: "admin", path: "/users", list: /\/api\/users\?/, actions: ["New user"] },
  { role: "supervisor", path: "/stock", list: /\/api\/stock\?/, actions: ["Export Excel"] },
  {
    role: "admin",
    path: "/products",
    list: /\/api\/products\?/,
    actions: ["New product", "Import", "Export Excel"],
  },
];

/** Distance from the top of the page (the page may have scrolled to bring a focused control into view). */
const documentTop = (item: Locator) =>
  item.evaluate((element) => element.getBoundingClientRect().top + window.scrollY);

/** A button or link in the page's main region, found by the start of its visible text. */
const control = (page: Page, name: string) =>
  page.getByRole("main").locator("button, a").filter({ hasText: new RegExp(`^\\s*${name}`) });

for (const size of VIEWPORTS) {
  test.describe(`R11 Phone banner at ${size.width}x${size.height}`, () => {
    test("given each list page when its list call fails, then the banner pushes the page down, covers no action, title or filter, and is at most a fifth of the window", async ({
      browser,
    }) => {
      test.setTimeout(180_000); // six pages with a two-second retry check each, on a loaded machine
      const sessions = new Map<Role, Awaited<ReturnType<typeof openSession>>>();
      for (const role of new Set(LIST_PAGES.map((entry) => entry.role))) {
        sessions.set(role, await openSession(browser, role, { viewport: size }));
      }
      try {
        for (const entry of LIST_PAGES) {
          const page = sessions.get(entry.role)!.page;
          await failCalls(page, entry.list, LONG_SENTENCE);
          await goTo(page, entry.path);
          const banner = page.locator("app-toast").filter({ hasText: "database" });
          await expect(banner, entry.path).toHaveCount(1);
          const host = page.locator("app-toast-host:not(.--in-dialog)");
          const hostBox = (await host.boundingBox())!;
          const hostBottom = hostBox.y + hostBox.height - 0.5;

          // C. At most a fifth of the window (inner scroll shows the rest).
          expect(hostBox.height, `${entry.path} banner height`).toBeLessThanOrEqual(
            size.height * 0.2 + 1,
          );
          // The title and the search box sit below the banner.
          const heading = page.getByRole("heading", { level: 1 });
          const titleTop = await documentTop(heading);
          expect((await heading.boundingBox())!.y, `${entry.path} title`).toBeGreaterThanOrEqual(
            hostBottom,
          );
          const search = page.getByRole("searchbox").first();
          if (await search.count()) {
            expect((await search.boundingBox())!.y, `${entry.path} search`).toBeGreaterThanOrEqual(
              hostBottom,
            );
          }

          // A. Every primary action is clear of the banner and clickable (a trial click fails on "intercepts pointer events").
          let found = 0;
          for (const name of entry.actions) {
            if ((await control(page, name).count()) === 0) {
              continue;
            }
            found++;
            const action = control(page, name).first();
            await action.scrollIntoViewIfNeeded();
            expect(await overlap(host, action), `${entry.path} ${name}`).toBe(false);
            await action.click({ trial: true });
          }
          expect(found, `${entry.path} has a primary action`).toBeGreaterThan(0);

          // D. One alert; a retry of the same failed call leaves one node; Dismiss removes it and the layout returns.
          await expect(page.getByRole("alert")).toHaveCount(1);
          expect(await announcedBy(page, "database")).toBe(1);
          const retry = page.getByRole("button", { name: "Retry" });
          if (await retry.count()) {
            await retry.click();
            await page.waitForTimeout(2300);
            await expect(page.getByRole("alert")).toHaveCount(1);
          }
          await banner.getByRole("button", { name: "Dismiss" }).click();
          await expect(page.getByRole("alert")).toHaveCount(0);
          expect(await documentTop(heading), `${entry.path} layout returns`).toBeLessThan(titleTop);
          await page.unroute(entry.list);
        }
      } finally {
        for (const session of sessions.values()) {
          await session.context.close();
        }
      }
    });

    test("given product-edit and the editor when a Save fails, then the header Save and every action-bar button are clear of the banner and reachable", async ({
      browser,
      api,
    }) => {
      const product = await api().createProduct();
      const draft = await api("staff").createDocument("receipts", [
        { productId: product.id, qty: 2, unitCost: 1 },
      ]);
      const cases = [
        { role: "admin" as const, path: `/products/${product.id}`, url: `/api/products/${product.id}` },
        { role: "staff" as const, path: `/receipts/${draft.id}`, url: `/api/documents/${draft.id}` },
      ];
      for (const { role, path, url } of cases) {
        const session = await openSession(browser, role, { viewport: size });
        try {
          const page = session.page;
          await failCalls(page, new RegExp(`${url}$`), LONG_SENTENCE, { method: "PUT" });
          await goTo(page, path);
          const isEditor = role === "staff";
          await page
            .getByRole("textbox", { name: isEditor ? "Remark" : /^Name/ })
            .fill("R11 changed");
          await page.getByRole("button", { name: isEditor ? "Save draft" : "Save" }).first().click();
          await expect(page.locator("app-toast").filter({ hasText: "database" })).toHaveCount(1);

          const buttons = isEditor
            ? page.locator(".document-editor-bar").getByRole("button")
            : page.getByRole("button", { name: "Save" }).first();
          for (let index = 0; index < (await buttons.count()); index++) {
            const button = buttons.nth(index);
            expect(await overlap(page.locator("app-toast-host"), button), `${path} #${index}`).toBe(
              false,
            );
            expect(await isReachable(button), `${path} #${index} reachable`).toBe(true);
          }
          expect(await buttons.count()).toBeGreaterThan(isEditor ? 1 : 0);
        } finally {
          await session.context.close();
        }
      }
    });

    test("given a long draft scrolled to the bottom when Save fails, then the banner is brought into view", async ({
      browser,
      api,
    }) => {
      const products = [];
      for (let index = 0; index < 15; index++) {
        products.push(await api().createProduct()); // one at a time: the API answers 409 to a burst
      }
      const lines = products.map((product) => ({ productId: product.id, qty: 1, unitCost: 1 }));
      const draft = await api("staff").createDocument("receipts", lines);
      const session = await openSession(browser, "staff", { viewport: size });
      try {
        const page = session.page;
        await failCalls(page, new RegExp(`/api/documents/${draft.id}$`), LONG_SENTENCE, {
          method: "PUT",
        });
        await goTo(page, `/receipts/${draft.id}`);
        const save = page.getByRole("button", { name: "Save draft" });
        await save.waitFor();
        await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
        await save.click();

        const host = page.locator("app-toast-host");
        await expect(host.locator("app-toast")).toHaveCount(1);
        await expect
          .poll(async () => {
            const box = await host.boundingBox();
            return !!box && box.y >= 0 && box.y + box.height <= size.height;
          })
          .toBe(true);
      } finally {
        await session.context.close();
      }
    });
  });
}

test.describe("R11 A failure while a dialog is open", () => {
  test.use({ viewport: { width: 360, height: 740 }, hasTouch: true, isMobile: true });

  test("given the New partner dialog when its Save fails, then the sentence is inside the dialog, Dismiss works there, the page copy is hidden and there is one alert, also after closing", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await failCalls(page, /\/api\/partners$/, LONG_SENTENCE, { method: "POST" });
    await goTo(page, "/partners");
    await page.getByRole("button", { name: "New partner" }).click();
    const dialog = page.getByRole("dialog", { name: "New partner" });
    await dialog.getByRole("textbox", { name: /^Name/ }).fill("R11 dialog failure");
    await dialog.getByRole("button", { name: "Save" }).click();

    const inside = dialog.locator("app-toast").filter({ hasText: "database" });
    await expect(inside).toHaveCount(1);
    const dialogBox = (await dialog.boundingBox())!;
    const toastBox = (await inside.boundingBox())!;
    expect(toastBox.y).toBeGreaterThanOrEqual(dialogBox.y);
    expect(toastBox.y + toastBox.height).toBeLessThanOrEqual(dialogBox.y + dialogBox.height);
    await expect(page.locator("app-toast-host:not(.--in-dialog)")).toBeHidden();
    await expect(page.getByRole("alert")).toHaveCount(1);
    expect(await announcedBy(page, "database")).toBe(1);

    // Dismiss is clickable inside the modal, and the dialog buttons are not covered.
    await inside.getByRole("button", { name: "Dismiss" }).click({ trial: true });
    await dialog.getByRole("button", { name: "Save" }).click({ trial: true });

    await dialog.getByRole("button", { name: "Cancel" }).click();
    await expect(dialog).toBeHidden();
    // The dialog takes its error with it: nothing is announced a second time.
    await expect(page.getByRole("alert")).toHaveCount(0);
    await expect(page.locator("app-toast")).toHaveCount(0);
  });
});
