import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../src/fixtures";
import { DocumentEditor } from "../src/pages/documents";
import { goTo } from "../src/pages/shell";

// R11 (docs/design/2026-refresh-plan.md): one failure, one announcement; a toast never covers the main action.
// The API is made to fail with route stubs, so the sentence is known. "Announced" means: a live region holds the sentence.

const SENTENCE = "The database is not available.";
const problem = { title: "Unavailable", status: 503, detail: SENTENCE };

/** How many live regions (role alert or status, or aria-live) hold the sentence, outermost only. */
async function announcedBy(page: Page): Promise<number> {
  return page.evaluate((sentence) => {
    const live =
      '[role="alert"], [role="status"], [aria-live]:not([aria-live="off"])';
    return Array.from(document.querySelectorAll<HTMLElement>(live)).filter(
      (region) =>
        !region.parentElement?.closest(live) &&
        (region.textContent ?? "").includes(sentence),
    ).length;
  }, SENTENCE);
}

/** True when the two boxes share any area. */
async function overlap(a: Locator, b: Locator): Promise<boolean> {
  const [first, second] = await Promise.all([a.boundingBox(), b.boundingBox()]);
  if (!first || !second) {
    return false;
  }
  return (
    first.x < second.x + second.width &&
    second.x < first.x + first.width &&
    first.y < second.y + second.height &&
    second.y < first.y + first.height
  );
}

/** A tap at the centre of the button reaches the button itself: nothing lies on top of it. */
async function isReachable(button: Locator): Promise<boolean> {
  return button.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const hit = document.elementFromPoint(
      box.x + box.width / 2,
      box.y + box.height / 2,
    );
    return hit?.closest("button") === element;
  });
}

for (const viewport of [
  { name: "a 360px phone", width: 360, height: 740 },
  { name: "a 1280px desktop", width: 1280, height: 800 },
]) {
  test.describe(`R11 Feedback on ${viewport.name}`, () => {
    test.use({
      viewport: { width: viewport.width, height: viewport.height },
      hasTouch: viewport.width < 600,
      isMobile: viewport.width < 600,
    });

    test("given Save draft fails, then the failure is announced once, as one toast, and Save draft is not covered", async ({
      page,
      signIn,
      api,
    }) => {
      const product = await api().createProduct();
      const draft = await api("staff").createDocument("receipts", [
        { productId: product.id, qty: 2, unitCost: 1 },
      ]);
      await signIn("staff");
      const editor = new DocumentEditor(page);
      await editor.open("receipts", draft.id);
      await page.route(new RegExp(`/api/documents/${draft.id}$`), (route) =>
        route.request().method() === "PUT"
          ? route.fulfill({
              status: 503,
              contentType: "application/problem+json",
              body: JSON.stringify(problem),
            })
          : route.fallback(),
      );

      await editor.remark.fill("R11 save that fails");
      const save = editor.action("Save draft");
      await save.click();

      const toast = page.locator("app-toast").filter({ hasText: SENTENCE });
      await expect(toast).toHaveCount(1);
      await expect(page.getByText(SENTENCE)).toHaveCount(1);
      expect(await announcedBy(page)).toBe(1);
      await expect(
        page.getByRole("alert").filter({ hasText: SENTENCE }),
      ).toHaveCount(0);

      // The toast does not take focus, does not cover the button, and stays inside the window.
      await expect(save).toBeEnabled();
      expect(await overlap(toast, save)).toBe(false);
      expect(await isReachable(save)).toBe(true);
      const box = await toast.boundingBox();
      expect(box?.y ?? -1).toBeGreaterThanOrEqual(0);
      expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(
        viewport.height,
      );
      expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(
        viewport.width,
      );

      // The next action takes the old error away; the same failure again is still one toast.
      await save.click();
      await expect(toast).toHaveCount(1);
      expect(await announcedBy(page)).toBe(1);
    });
  });
}

test.describe("R11 A failure the screen shows in place", () => {
  test("given a document that cannot be loaded, then one alert with Retry is announced and no toast repeats it", async ({
    page,
    signIn,
    api,
  }) => {
    const product = await api().createProduct();
    const draft = await api("staff").createDocument("receipts", [
      { productId: product.id, qty: 1, unitCost: 1 },
    ]);
    await signIn("staff");
    await page.route(new RegExp(`/api/documents/${draft.id}$`), (route) =>
      route.request().method() === "GET"
        ? route.fulfill({
            status: 503,
            contentType: "application/problem+json",
            body: JSON.stringify(problem),
          })
        : route.fallback(),
    );

    await goTo(page, `/receipts/${draft.id}`);

    const banner = page.getByRole("alert").filter({ hasText: SENTENCE });
    await expect(banner).toHaveCount(1);
    await expect(banner.getByRole("button", { name: "Retry" })).toBeVisible();
    await expect(page.locator("app-toast")).toHaveCount(0);
    expect(await announcedBy(page)).toBe(1);
  });

  test("given a list that cannot be loaded, then the sentence is announced once and the panel offers Retry", async ({
    page,
    signIn,
  }) => {
    await signIn("supervisor");
    await page.route(/\/api\/documents\?/, (route) =>
      route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: JSON.stringify(problem),
      }),
    );

    await goTo(page, "/receipts");

    await expect(page.getByRole("button", { name: "Retry" })).toBeVisible();
    await expect(
      page.locator("app-toast").filter({ hasText: SENTENCE }),
    ).toHaveCount(1);
    expect(await announcedBy(page)).toBe(1);
  });

  test('given two quick saves that both succeed, then "Draft saved." shows once', async ({
    page,
    signIn,
    api,
  }) => {
    const product = await api().createProduct();
    const draft = await api("staff").createDocument("receipts", [
      { productId: product.id, qty: 2, unitCost: 1 },
    ]);
    await signIn("staff");
    const editor = new DocumentEditor(page);
    await editor.open("receipts", draft.id);

    await editor.remark.fill("R11 quick saves");
    await editor.action("Save draft").click();
    await expect(editor.shell.toast("Draft saved.")).toHaveCount(1);
    await editor.remark.fill("R11 quick saves again");
    await editor.action("Save draft").click();

    await expect(editor.shell.toast("Draft saved.")).toHaveCount(1);
  });
});
