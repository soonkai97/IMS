import { expect, test } from "../src/fixtures";
import {
  SENTENCE,
  announcedBy,
  isReachable,
  overlap,
  problemBody,
} from "../src/feedback";
import { DocumentEditor } from "../src/pages/documents";
import { goTo } from "../src/pages/shell";

// R11 (docs/design/2026-refresh-plan.md): one failure, one announcement; a toast never covers the main action.
// The API is made to fail with route stubs, so the sentence is known. "Announced" means: a live region holds the sentence.
// The phone layout (the in-flow banner) and the dialog case are in 18-feedback-layout.

for (const viewport of [
  { name: "a 1280px desktop", width: 1280, height: 800 },
  { name: "a 1024px desktop", width: 1024, height: 768 },
  { name: "a 360px phone", width: 360, height: 740 },
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
              body: problemBody(SENTENCE),
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
      await expect(page.getByRole("alert")).toHaveCount(1);

      // The toast does not take focus, does not cover the button, and is not a layer over the page.
      await expect(save).toBeEnabled();
      expect(await overlap(toast, save)).toBe(false);
      expect(await isReachable(save)).toBe(true);

      // The next action takes the old error away; the same failure again is still one toast.
      // (The old node fades out for a moment, so the count settles at one.)
      await save.click();
      await expect.poll(() => announcedBy(page)).toBe(1);
      await expect(toast).toHaveCount(1);
    });
  });
}

test.describe("R11 A failure the screen shows in place", () => {
  test("given the API is down for a saved document (every call fails), then the banner is the one announcement and no toast says it again", async ({
    page,
    signIn,
    api,
  }) => {
    const product = await api().createProduct();
    const draft = await api("staff").createDocument("receipts", [
      { productId: product.id, qty: 1, unitCost: 1 },
    ]);
    await signIn("staff");
    await page.route(/\/api\/(?!auth\/)/, (route) =>
      route.fulfill({
        status: 503,
        contentType: "application/problem+json",
        body: problemBody(SENTENCE),
      }),
    );

    await goTo(page, `/receipts/${draft.id}`);

    const banner = page.getByRole("alert").filter({ hasText: SENTENCE });
    await expect(banner).toHaveCount(1);
    await expect(banner.getByRole("button", { name: "Retry" })).toBeVisible();
    await expect(page.locator("app-toast")).toHaveCount(0);
    expect(await announcedBy(page)).toBe(1);
  });

  test("given Home when the summary and the counts both fail, then one banner announces it and no toast", async ({
    page,
    signIn,
  }) => {
    await signIn("supervisor");
    await page.route(
      /\/api\/(stock\/summary|documents\/counts)/,
      (route) =>
        route.fulfill({
          status: 503,
          contentType: "application/problem+json",
          body: problemBody(SENTENCE),
        }),
    );

    await page.reload();

    await expect(page.getByRole("alert").filter({ hasText: SENTENCE })).toHaveCount(1);
    await expect(page.locator("app-toast")).toHaveCount(0);
    expect(await announcedBy(page)).toBe(1);
  });

  test("given a sign-in the server answers with a 500 and a sentence, then the banner shows it once and no toast", async ({
    page,
    login,
  }) => {
    await page.route(/\/api\/auth\/login/, (route) =>
      route.fulfill({
        status: 500,
        contentType: "application/problem+json",
        body: problemBody(SENTENCE),
      }),
    );
    await login.open();
    await login.fill("someone", "not-a-real-password");

    await expect(page.getByRole("alert").filter({ hasText: SENTENCE })).toHaveCount(1);
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
        body: problemBody(SENTENCE),
      }),
    );

    await goTo(page, "/receipts");

    await expect(page.getByRole("button", { name: "Retry" })).toBeVisible();
    await expect(
      page.locator("app-toast").filter({ hasText: SENTENCE }),
    ).toHaveCount(1);
    expect(await announcedBy(page)).toBe(1);
  });

  test("given two quick saves that both succeed, then Draft saved shows once", async ({
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
