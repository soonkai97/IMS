import { expect, test } from "../src/fixtures";

// R08 (docs/design/2026-refresh-plan.md): a busy button keeps its size and its label, whatever the label. The sign-in button is a
// text-only button, the case that once spilled its label out of a frozen box (a unit test cannot measure layout). The login
// request is held back by the test itself, so nothing reaches the API and no sign-in budget is spent.

test.describe("R08 A busy button does not change size or clip its label", () => {
  test("given a slow sign-in, when the spinner shows, then the Sign in button has the same box and its label is inside it", async ({
    login,
    page,
  }) => {
    await page.route("**/api/auth/login", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 2000));
      await route.fulfill({
        status: 401,
        contentType: "application/problem+json",
        body: JSON.stringify({ title: "Unauthorized", status: 401 }),
      });
    });
    await login.open();
    // The sign-in button fills its row, which hides the problem (spare room); shrink it to its label like "OK" or "Delete".
    await page.addStyleTag({
      content:
        "app-button.--block { display: inline-flex !important; align-self: flex-start !important; width: auto !important; }",
    });
    const idle = await login.submit.boundingBox();
    expect(idle?.width).toBeLessThan(150);

    await login.username.fill("nobody");
    await login.passwordBox.fill("not-a-password");
    await login.submit.click();

    await expect(login.submit).toHaveAttribute("aria-busy", "true");
    await expect(login.submit.locator("app-spinner")).toBeVisible();
    const busy = await login.submit.boundingBox();
    expect(busy?.width).toBeCloseTo(idle?.width ?? 0, 1);
    expect(busy?.height).toBeCloseTo(idle?.height ?? 0, 1);

    const fit = await login.submit.evaluate((button) => {
      const box = button.getBoundingClientRect();
      const face = button
        .querySelector(".ui-button-face")
        ?.getBoundingClientRect();
      return {
        overflow: button.scrollWidth - button.clientWidth,
        faceInside:
          !!face &&
          face.left >= box.left - 0.5 &&
          face.right <= box.right + 0.5,
      };
    });
    expect(fit.overflow).toBeLessThanOrEqual(0);
    expect(fit.faceInside).toBe(true);
  });

  test("given reduced motion, when a button is pressed, then it does not shrink", async ({
    login,
    page,
  }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await login.open();

    const box = await login.submit.boundingBox();
    await page.mouse.move(
      (box?.x ?? 0) + (box?.width ?? 0) / 2,
      (box?.y ?? 0) + 10,
    );
    await page.mouse.down();
    const transform = await login.submit.evaluate(
      (button) => getComputedStyle(button).transform,
    );
    await page.mouse.up();

    expect(["none", "matrix(1, 0, 0, 1, 0, 0)"]).toContain(transform);
  });
});
