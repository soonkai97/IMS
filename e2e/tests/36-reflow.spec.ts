import type { Page } from "@playwright/test";
import { expect, test } from "../src/fixtures";
import { goTo } from "../src/pages/shell";

// QA round 2026-10-19: novice finding 2 (the Products list on a phone is 7,000 px per page and a new page opens at the bottom)
// and edge finding 7 (320 px with the text at 200% clips Home, the role page and the tab bar; WCAG 1.4.4, 1.4.10).

const sidewaysScroll = (page: Page): Promise<number> =>
  page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);

test.describe("Products list on a 360 px phone", () => {
  test.use({
    viewport: { width: 360, height: 740 },
    hasTouch: true,
    isMobile: true,
  });

  test("given 25 product cards, then each is a compact card and the whole page is far shorter than 7,000 px", async ({
    page,
    signIn,
  }) => {
    await signIn("staff");
    await goTo(page, "/products");
    const cards = page.locator("app-table .ui-table-row");
    await expect(cards.first()).toBeVisible();
    expect(await cards.count()).toBeGreaterThan(5);

    const heights = await cards.evaluateAll((rows) =>
      rows.map((row) => row.getBoundingClientRect().height),
    );
    // Was about 290 px per card; the compact card is a title line and one line of values.
    expect(Math.max(...heights)).toBeLessThan(130);
    expect(await sidewaysScroll(page)).toBeLessThanOrEqual(0);
  });

  test("given the reader is at the bottom of page 1, when Next page is pressed, then the table starts at the top of the window", async ({
    page,
    signIn,
  }) => {
    await signIn("staff");
    await goTo(page, "/products");
    const table = page.locator("app-table");
    const range = page.locator("app-pager .ui-pager-range");
    await expect(range).toContainText("1-");
    const first = (await range.textContent()) ?? "";

    await page.locator("app-pager").scrollIntoViewIfNeeded();
    await page.evaluate(() =>
      window.scrollTo(0, document.documentElement.scrollHeight),
    );
    expect(
      await table.evaluate((element) => element.getBoundingClientRect().top),
    ).toBeLessThan(0);

    await page.getByRole("button", { name: "Next page" }).click();
    await expect(range).not.toHaveText(first);

    const top = await table.evaluate(
      (element) => element.getBoundingClientRect().top,
    );
    expect(top).toBeGreaterThanOrEqual(0);
    expect(top).toBeLessThan(160);
  });
});

test.describe("320 px with the text at 200%", () => {
  test.use({ viewport: { width: 320, height: 640 } });

  async function enlarge(page: Page): Promise<void> {
    await page.evaluate(
      () => (document.documentElement.style.fontSize = "200%"),
    );
  }

  test("given the Home screen, then both big buttons end inside the window and nothing scrolls sideways", async ({
    page,
    signIn,
  }) => {
    await signIn("clerk");
    await goTo(page, "/");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
    await enlarge(page);

    const edges = await page
      .locator(".home-page-action")
      .evaluateAll((links) =>
        links.map((link) => link.getBoundingClientRect().right),
      );
    expect(edges.length).toBeGreaterThanOrEqual(2);
    for (const right of edges) {
      expect(right).toBeLessThanOrEqual(320);
    }
    expect(await sidewaysScroll(page)).toBeLessThanOrEqual(0);
  });

  test('given the tab bar, then every label shows whole (it is not cut to "H...") and the bar, not the page, scrolls sideways', async ({
    page,
    signIn,
  }) => {
    await signIn("clerk");
    await goTo(page, "/");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
    await enlarge(page);

    const labels = await page.locator(".shell-tab-label").evaluateAll((items) =>
      items.map((item) => ({
        text: item.textContent?.trim() ?? "",
        cutWide: item.scrollWidth > item.clientWidth + 1,
        cutTall: item.scrollHeight > item.clientHeight + 4,
      })),
    );
    expect(labels.length).toBeGreaterThanOrEqual(4);
    for (const label of labels) {
      expect(label.cutWide, `${label.text} is cut sideways`).toBe(false);
      expect(label.cutTall, `${label.text} is cut after two lines`).toBe(false);
    }
    const bar = await page
      .locator(".shell-tabbar")
      .evaluate((element) => element.scrollWidth > element.clientWidth);
    expect(bar).toBe(true);
    expect(await sidewaysScroll(page)).toBeLessThanOrEqual(0);
  });

  test("given the text at its normal size on 320 px, then the five tab labels fit and the bar does not scroll", async ({
    page,
    signIn,
  }) => {
    await signIn("clerk");
    await goTo(page, "/");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();

    const cut = await page
      .locator(".shell-tab-label")
      .evaluateAll(
        (items) =>
          items.filter((item) => item.scrollWidth > item.clientWidth + 1)
            .length,
      );
    expect(cut).toBe(0);
    const bar = await page
      .locator(".shell-tabbar")
      .evaluate((element) => element.scrollWidth > element.clientWidth);
    expect(bar).toBe(false);
  });

  test("given the Admin, then no main screen scrolls sideways at 320 px with the text at 200%", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    const screens = [
      "/",
      "/receipts",
      "/receipts/new",
      "/issues",
      "/stock",
      "/low-stock",
      "/products",
      "/products/new",
      "/partners",
      "/warehouses",
      "/users",
      "/roles",
      "/roles/new",
      "/approvals",
      "/reports",
      "/audit-log",
      "/settings",
    ];
    for (const path of screens) {
      await goTo(page, path);
      await expect(
        page.getByRole("heading", { level: 1 }).first(),
      ).toBeVisible();
      await page.waitForLoadState("networkidle");
      await enlarge(page);
      expect(
        await sidewaysScroll(page),
        `${path} scrolls sideways`,
      ).toBeLessThanOrEqual(0);
      await page.evaluate(() => (document.documentElement.style.fontSize = ""));
    }
  });

  test("given a new role, then the high-impact chip and its help text stay inside the window", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/roles/new");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
    await expect(
      page.locator(".permission-list-details").first(),
    ).toBeVisible();
    await enlarge(page);

    expect(await sidewaysScroll(page)).toBeLessThanOrEqual(0);
    const overflow = await page
      .locator(".permission-list-details > *")
      .evaluateAll(
        (items) =>
          items.filter((item) => item.getBoundingClientRect().right > 320)
            .length,
      );
    expect(overflow).toBe(0);
  });
});
