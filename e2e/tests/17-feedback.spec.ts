import type { Page } from "@playwright/test";
import { expect, test } from "../src/fixtures";
import { SENTENCE, announcedBy, failCalls, isReachable, overlap } from "../src/feedback";
import { DocumentEditor } from "../src/pages/documents";
import { goTo } from "../src/pages/shell";

// R11 (docs/design/2026-refresh-plan.md): one failure, one announcement; a toast never covers the main action.
// The API is made to fail with route stubs, so the sentence is known. "Announced" means: a live region holds the sentence.
// The phone banner and the dialog case are in 18-feedback-layout.

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
      await failCalls(page, new RegExp(`/api/documents/${draft.id}$`), SENTENCE, {
        method: "PUT",
      });

      await editor.remark.fill("R11 save that fails");
      const save = editor.action("Save draft");
      await save.click();

      const toast = page.locator("app-toast").filter({ hasText: SENTENCE });
      await expect(toast).toHaveCount(1);
      await expect(page.getByRole("alert")).toHaveCount(1);
      expect(await announcedBy(page)).toBe(1);
      await expect(save).toBeEnabled();
      expect(await overlap(toast, save)).toBe(false);
      expect(await isReachable(save)).toBe(true);

      // The next action takes the old error away; the same failure again is still one node (the old one fades out).
      await save.click();
      await expect.poll(() => announcedBy(page)).toBe(1);
      await expect(toast).toHaveCount(1);
    });
  });
}

test.describe("R11 A failure the screen shows in place", () => {
  const screens = [
    {
      name: "a saved document when every call fails (Retry offered)",
      role: "staff" as const,
      url: /\/api\/(?!auth\/)/,
      open: async (page: Page, id: number) =>
        goTo(page, `/receipts/${id}`),
    },
    {
      name: "Home when the summary and the counts both fail",
      role: "supervisor" as const,
      url: /\/api\/(stock\/summary|documents\/counts)/,
      open: async (page: Page) => page.reload(),
    },
  ];

  for (const screen of screens) {
    test(`given ${screen.name}, then one banner announces it and no toast`, async ({
      page,
      signIn,
      api,
    }) => {
      const product = await api().createProduct();
      const draft = await api("staff").createDocument("receipts", [
        { productId: product.id, qty: 1, unitCost: 1 },
      ]);
      await signIn(screen.role);
      await failCalls(page, screen.url);
      await screen.open(page, draft.id);

      await expect(page.getByRole("alert").filter({ hasText: SENTENCE })).toHaveCount(1);
      await expect(page.locator("app-toast")).toHaveCount(0);
      expect(await announcedBy(page)).toBe(1);
    });
  }

  test("given a sign-in the server answers with a 500 and a sentence, then the banner shows it once and no toast", async ({
    page,
    login,
  }) => {
    await failCalls(page, /\/api\/auth\/login/, SENTENCE, { status: 500 });
    await login.open();
    await login.fill("someone", "not-a-real-password");

    await expect(page.getByRole("alert").filter({ hasText: SENTENCE })).toHaveCount(1);
    await expect(page.locator("app-toast")).toHaveCount(0);
    expect(await announcedBy(page)).toBe(1);
  });
});
