import type { Locator, Page } from "@playwright/test";
import { expect, openSession, test } from "../src/fixtures";
import type { Role } from "../src/config";
import {
  LONG_SENTENCE,
  announcedBy,
  isReachable,
  overlap,
  problemBody,
} from "../src/feedback";
import { goTo } from "../src/pages/shell";

// R11 (ruling C6 v2): on a phone an error is an in-flow banner under the top bar. It pushes the page down instead of lying on it,
// so no title, filter or primary action is ever covered, whatever the length of the sentence. In a modal dialog the dialog
// shows its own copy. Measured on three phone sizes: 360x740, 320x568 and 320x256 (a landscape phone, or 400% zoom).

const VIEWPORTS = [
  { width: 360, height: 740 },
  { width: 320, height: 568 },
  { width: 320, height: 256 },
];

interface ListPage {
  readonly role: Role;
  readonly path: string;
  readonly list: RegExp;
  /** The primary actions of the page; each one that the role has is checked. */
  readonly actions: readonly string[];
}

const LIST_PAGES: readonly ListPage[] = [
  {
    role: "supervisor",
    path: "/receipts",
    list: /\/api\/documents\?/,
    actions: ["New receipt"],
  },
  {
    role: "admin",
    path: "/partners",
    list: /\/api\/partners\?/,
    actions: ["New partner"],
  },
  {
    role: "admin",
    path: "/warehouses",
    list: /\/api\/warehouses(\?|$)/,
    actions: ["New warehouse"],
  },
  {
    role: "admin",
    path: "/users",
    list: /\/api\/users\?/,
    actions: ["New user"],
  },
  {
    role: "supervisor",
    path: "/stock",
    list: /\/api\/stock\?/,
    actions: ["Export Excel"],
  },
  {
    role: "admin",
    path: "/products",
    list: /\/api\/products\?/,
    actions: ["New product", "Import", "Export Excel"],
  },
];

async function fail(page: Page, url: RegExp, detail: string): Promise<void> {
  await page.route(url, (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: problemBody(detail),
    }),
  );
}

/** Distance from the top of the page (the page may have scrolled to bring a focused control into view). */
async function documentTop(item: Locator): Promise<number> {
  return item.evaluate(
    (element) => element.getBoundingClientRect().top + window.scrollY,
  );
}

/** A button or link in the page's main region, found by the start of its visible text. */
function control(page: Page, name: string): Locator {
  return page
    .getByRole("main")
    .locator("button, a")
    .filter({ hasText: new RegExp(`^\\s*${name}`) });
}

for (const size of VIEWPORTS) {
  test.describe(`R11 Phone banner at ${size.width}x${size.height}`, () => {
    test("given each list page when its list call fails, then the banner pushes the page down, covers no action, title or filter, and is at most a fifth of the window", async ({
      browser,
    }) => {
      const sessions = new Map<Role, Awaited<ReturnType<typeof openSession>>>();
      for (const role of new Set(LIST_PAGES.map((entry) => entry.role))) {
        sessions.set(role, await openSession(browser, role, { viewport: size }));
      }
      try {
        for (const entry of LIST_PAGES) {
          const page = sessions.get(entry.role)!.page;
          await fail(page, entry.list, LONG_SENTENCE);
          await goTo(page, entry.path);
          const banner = page.locator("app-toast").filter({ hasText: "database" });
          await expect(banner, entry.path).toHaveCount(1);
          const host = page.locator("app-toast-host:not(.--in-dialog)");
          const hostBox = (await host.boundingBox())!;

          // C. At most a fifth of the window (inner scroll shows the rest).
          expect(hostBox.height, `${entry.path} banner height`).toBeLessThanOrEqual(
            size.height * 0.2 + 1,
          );
          // The banner is in flow under the top bar, at the top of the page content.
          const heading = page.getByRole("heading", { level: 1 });
          const headingBox = (await heading.boundingBox())!;
          const titleTop = await documentTop(heading);
          expect(
            headingBox.y,
            `${entry.path} title sits below the banner`,
          ).toBeGreaterThanOrEqual(hostBox.y + hostBox.height - 0.5);
          const search = page.getByRole("searchbox").first();
          if (await search.count()) {
            const searchBox = (await search.boundingBox())!;
            expect(
              searchBox.y,
              `${entry.path} search sits below the banner`,
            ).toBeGreaterThanOrEqual(hostBox.y + hostBox.height - 0.5);
          }

          // A. Every primary action is clear of the banner and can be clicked (a trial click fails on "intercepts pointer events").
          let found = 0;
          for (const name of entry.actions) {
            const action = control(page, name).first();
            if ((await control(page, name).count()) === 0) {
              continue;
            }
            found++;
            await action.scrollIntoViewIfNeeded();
            expect(
              await overlap(host, action),
              `${entry.path} ${name} overlaps the banner`,
            ).toBe(false);
            await action.click({ trial: true });
          }
          expect(found, `${entry.path} has one of its primary actions`).toBeGreaterThan(0);

          // D. One alert, one node; a retry of the same failed call does not add a second; Dismiss removes it and the layout returns.
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
          const after = await documentTop(heading);
          expect(after, `${entry.path} layout returns`).toBeLessThan(titleTop);
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

      const admin = await openSession(browser, "admin", { viewport: size });
      try {
        const page = admin.page;
        await page.route(new RegExp(`/api/products/${product.id}$`), (route) =>
          route.request().method() === "PUT"
            ? route.fulfill({
                status: 503,
                contentType: "application/problem+json",
                body: problemBody(LONG_SENTENCE),
              })
            : route.fallback(),
        );
        await goTo(page, `/products/${product.id}`);
        await page.getByRole("textbox", { name: /^Name/ }).fill("R11 renamed");
        const save = page.getByRole("button", { name: "Save" }).first();
        await save.click();
        const banner = page.locator("app-toast").filter({ hasText: "database" });
        await expect(banner).toHaveCount(1);
        expect(await overlap(page.locator("app-toast-host"), save)).toBe(false);
        expect(await isReachable(save)).toBe(true);
        await save.click({ trial: true });
      } finally {
        await admin.context.close();
      }

      const staff = await openSession(browser, "staff", { viewport: size });
      try {
        const page = staff.page;
        await page.route(new RegExp(`/api/documents/${draft.id}$`), (route) =>
          route.request().method() === "PUT"
            ? route.fulfill({
                status: 503,
                contentType: "application/problem+json",
                body: problemBody(LONG_SENTENCE),
              })
            : route.fallback(),
        );
        await goTo(page, `/receipts/${draft.id}`);
        await page.getByRole("textbox", { name: "Remark" }).fill("R11 fails");
        await page.getByRole("button", { name: "Save draft" }).click();
        await expect(
          page.locator("app-toast").filter({ hasText: "database" }),
        ).toHaveCount(1);
        const buttons = page.locator(".document-editor-bar").getByRole("button");
        const count = await buttons.count();
        expect(count).toBeGreaterThan(1);
        for (let index = 0; index < count; index++) {
          const button = buttons.nth(index);
          expect(
            await overlap(page.locator("app-toast-host"), button),
            `action bar button ${index} overlaps the banner`,
          ).toBe(false);
          expect(
            await isReachable(button),
            `action bar button ${index} is reachable`,
          ).toBe(true);
        }
      } finally {
        await staff.context.close();
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
    await page.route(/\/api\/partners$/, (route) =>
      route.request().method() === "POST"
        ? route.fulfill({
            status: 503,
            contentType: "application/problem+json",
            body: problemBody(LONG_SENTENCE),
          })
        : route.fallback(),
    );
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
    await expect(page.getByRole("alert")).toHaveCount(1);
    await expect(page.locator("app-toast")).toHaveCount(1);
  });
});
