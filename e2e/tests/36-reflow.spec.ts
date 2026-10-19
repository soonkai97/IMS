import type { Page } from "@playwright/test";
import { expect, test } from "../src/fixtures";
import { goTo } from "../src/pages/shell";

// QA round 2026-10-19: novice finding 2 (the Products list on a phone is 7,000 px per page and a new page opens at the bottom)
// and edge finding 7 (320 px with the text at 200% clips Home, the role page and the tab bar; WCAG 1.4.4, 1.4.10).

const sidewaysScroll = (page: Page): Promise<number> =>
  page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);

/** Nothing is loading and no route is crossfading: geometry can be read. */
async function settled(page: Page): Promise<void> {
  await page.waitForFunction(
    () =>
      document.querySelector('[aria-busy="true"], .ui-table-progress') ===
        null && !document.documentElement.matches(":active-view-transition"),
  );
}

const PRODUCTS_TOTAL = 80;

/** The product list answers with 80 rows whatever the database holds: the page count and the card count are known. */
async function stubProducts(page: Page): Promise<void> {
  await page.route(/\/api\/products(\?|$)/, (route) => {
    const params = new URL(route.request().url()).searchParams;
    const read = (name: string, fallback: number): number => {
      for (const [key, value] of params) {
        if (key.toLowerCase() === name) {
          return Number(value) || fallback;
        }
      }
      return fallback;
    };
    const number = read("page", 1);
    const size = read("pagesize", 25);
    const first = (number - 1) * size;
    const items = Array.from(
      { length: Math.max(0, Math.min(size, PRODUCTS_TOTAL - first)) },
      (_, index) => ({
        id: `00000000-0000-4000-8000-${String(first + index + 1).padStart(12, "0")}`,
        sku: `REFLOW-${first + index + 1}`,
        name: `Reflow product ${first + index + 1}`,
        baseUomId: "00000000-0000-4000-8000-0000000000aa",
        baseUomCode: "PCS",
        safetyStock: 0,
        isActive: true,
        barcodes: [],
        conversions: [],
      }),
    );
    return route.fulfill({
      json: { items, total: PRODUCTS_TOTAL, page: number, pageSize: size },
    });
  });
}

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
    await stubProducts(page);
    await signIn("staff");
    await goTo(page, "/products");
    const cards = page.locator("app-table .ui-table-row");
    await expect(cards.first()).toBeVisible();
    await expect(cards).toHaveCount(25);

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
    await stubProducts(page);
    await signIn("staff");
    await goTo(page, "/products");
    const table = page.locator("app-table");
    const range = page.locator("app-pager .ui-pager-range");
    await expect(range).toHaveText("1-25 of 80");
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
      await settled(page);
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

test.describe("Home stat cards keep their chevron beside the text", () => {
  // The desktop sidebar at 1280 and 1024, the rail at 768, a phone at 360, and 320 with the text at 200%.
  const SHAPES = [
    { width: 1280, height: 800, text: "100%" },
    { width: 1024, height: 768, text: "100%" },
    { width: 768, height: 1024, text: "100%" },
    { width: 360, height: 740, text: "100%" },
    { width: 320, height: 640, text: "200%" },
  ];

  test("given the Admin Home at five window shapes, then no linked card puts its chevron on a line of its own and none scrolls sideways", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
    await expect(page.locator(".ui-stat-card-link").first()).toBeVisible();

    for (const shape of SHAPES) {
      await page.setViewportSize({ width: shape.width, height: shape.height });
      await page.evaluate(
        (text) => (document.documentElement.style.fontSize = text),
        shape.text,
      );
      await settled(page);
      const alone = await page
        .locator(".ui-stat-card-link")
        .evaluateAll((cards) =>
          cards
            .filter((card) => {
              const chevron = card.querySelector(".ui-stat-card-chevron");
              // A card too narrow for it has no chevron at all (display none): nothing to be alone.
              if (!chevron || chevron.getClientRects().length === 0) {
                return false;
              }
              const at = chevron.getBoundingClientRect();
              const others = [".ui-stat-card-badge", ".ui-stat-card-text"].map(
                (selector) =>
                  card.querySelector(selector)?.getBoundingClientRect(),
              );
              // On the line of the badge or of the text.
              return !others.some(
                (box) => box && at.top < box.bottom && at.bottom > box.top,
              );
            })
            .map((card) => card.textContent?.trim() ?? ""),
        );
      expect(
        alone,
        `${shape.width}px at ${shape.text}: chevron alone on its line in ${alone.join(" | ")}`,
      ).toEqual([]);
      expect(
        await sidewaysScroll(page),
        `${shape.width}px at ${shape.text} scrolls sideways`,
      ).toBeLessThanOrEqual(0);
    }
  });
});

test.describe("Tab bar focus", () => {
  test.use({ viewport: { width: 360, height: 740 } });

  test("given a keyboard user on the tab bar, then the focus ring of a tab is drawn inside the bar, not cut off by it", async ({
    page,
    signIn,
  }) => {
    await signIn("clerk");
    await goTo(page, "/");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
    await settled(page);

    // A key press first, so the browser treats the focus that follows as the keyboard's.
    await page.keyboard.press("Tab");
    const tab = page.locator(".shell-tab").nth(1);
    await tab.focus();
    const ring = await tab.evaluate((element) => {
      const style = getComputedStyle(element);
      const box = element.getBoundingClientRect();
      const bar = element.closest(".shell-tabbar")!.getBoundingClientRect();
      const width = parseFloat(style.outlineWidth);
      const offset = parseFloat(style.outlineOffset);
      return {
        shown: style.outlineStyle !== "none" && width > 0,
        // The ring spans from offset to offset + width outside the border box (negative: inside).
        outerEdge: offset + width,
        insideTop: box.top >= bar.top,
        insideBottom: box.bottom <= bar.bottom,
      };
    });

    expect(ring.shown).toBe(true);
    expect(ring.outerEdge).toBeLessThanOrEqual(0);
    expect(ring.insideTop && ring.insideBottom).toBe(true);
  });
});
