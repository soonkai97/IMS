import type { Page } from "@playwright/test";
import { expect, test } from "../src/fixtures";
import { goTo } from "../src/pages/shell";

// TB (docs/design/lean-scope.md): the table toolbar. First and Last, a Rows per page select and a Columns menu on a table with
// room; a phone keeps the two arrows and the range. The audit list is answered by a route stub (333 rows), so the page count
// is known and nothing depends on the data in the database. Sign-in is real.

const TOTAL = 333;

/** The query value, whatever the capitalisation the API client wrote the name in. */
function queryOf(url: string, name: string): number {
  const params = new URL(url).searchParams;
  for (const [key, value] of params) {
    if (key.toLowerCase() === name) {
      return Number(value);
    }
  }
  return 0;
}

/** Answers the audit list with TOTAL rows, and records the (page, pageSize) of every request. */
async function stubAudit(page: Page): Promise<Array<[number, number]>> {
  const asked: Array<[number, number]> = [];
  await page.route(/\/api\/audit(\?|$)/, (route) => {
    const number = queryOf(route.request().url(), "page") || 1;
    const size = queryOf(route.request().url(), "pagesize") || 25;
    asked.push([number, size]);
    const first = (number - 1) * size;
    const items = Array.from(
      { length: Math.max(0, Math.min(size, TOTAL - first)) },
      (_, index) => ({
        actor: "admin",
        action: "product.updated",
        entityType: "Product",
        entityId: String(first + index + 1),
        at: "2026-10-09T08:15:00Z",
      }),
    );
    return route.fulfill({
      json: { items, total: TOTAL, page: number, pageSize: size },
    });
  });
  return asked;
}

async function openAudit(page: Page): Promise<Array<[number, number]>> {
  const asked = await stubAudit(page);
  await goTo(page, "/audit-log");
  await expect(
    page.getByRole("heading", { level: 1, name: "Audit log" }),
  ).toBeVisible();
  await expect(page.locator("app-pager .ui-pager-range")).toHaveText(
    "1-25 of 333",
  );
  return asked;
}

test.describe("TB Paging on a table with room", () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  test("given 333 rows, when Last, Previous and a bigger page size are used, then the range, the request and the focus follow", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    const asked = await openAudit(page);
    const pager = page.locator("app-pager");
    const range = pager.locator(".ui-pager-range");

    await pager.getByRole("button", { name: "Last page" }).click();
    await expect(range).toHaveText("326-333 of 333");
    expect(asked.at(-1)).toEqual([14, 25]);
    // The new page cannot use Last and Next: they are aria-disabled, so the pressed button keeps the focus.
    await expect(
      pager.getByRole("button", { name: "Last page" }),
    ).toBeFocused();
    await expect(
      pager.getByRole("button", { name: "Last page" }),
    ).toHaveAttribute("aria-disabled", "true");
    await expect(
      pager.getByRole("button", { name: "Next page" }),
    ).toHaveAttribute("aria-disabled", "true");
    await expect(pager.getByRole("navigation")).toHaveAccessibleName(
      "Page 14 of 14",
    );

    await pager.getByRole("button", { name: "Previous page" }).click();
    await expect(range).toHaveText("301-325 of 333");
    expect(asked.at(-1)).toEqual([13, 25]);

    // A new size starts again on page 1.
    await pager.getByLabel("Rows per page").selectOption("50");
    await expect(range).toHaveText("1-50 of 333");
    expect(asked.at(-1)).toEqual([1, 50]);
    await expect(pager.getByLabel("Rows per page")).toHaveValue("50");

    // 333 rows at 50 a page are 7 pages: Last shows 301-333, First is page 1 again.
    await pager.getByRole("button", { name: "Last page" }).click();
    await expect(range).toHaveText("301-333 of 333");
    await pager.getByRole("button", { name: "First page" }).click();
    await expect(range).toHaveText("1-50 of 333");
  });

  test("given Rows per page has the focus, when ArrowDown is pressed twice, then it goes 25, 50, 100 and the focus stays on the select", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    const asked = await openAudit(page);
    const size = page.locator("app-pager").getByLabel("Rows per page");

    await size.focus();
    await page.keyboard.press("ArrowDown");
    await expect(page.locator("app-pager .ui-pager-range")).toHaveText(
      "1-50 of 333",
    );
    await expect(size).toBeFocused();
    await page.keyboard.press("ArrowDown");
    await expect(page.locator("app-pager .ui-pager-range")).toHaveText(
      "1-100 of 333",
    );
    await expect(size).toBeFocused();
    expect(asked.at(-1)).toEqual([1, 100]);
  });

  test("given a page change, then a screen reader hears one polite sentence with the page and the range", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await openAudit(page);
    const pager = page.locator("app-pager");

    await expect(pager.locator("[aria-live]")).toHaveCount(1);
    await pager.getByRole("button", { name: "Next page" }).click();

    await expect(pager.locator("[aria-live]")).toHaveText(
      "Page 2 of 14, 26-50 of 333",
    );
    await expect(pager.locator("[aria-live]")).toHaveAttribute(
      "aria-live",
      "polite",
    );
  });

  test("given the Columns menu, when a column is switched off and the menu closed with Escape, then the column is gone, the focus is back on the button and Show all columns restores it", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await openAudit(page);
    const button = page.getByRole("button", { name: "Columns" });
    const menu = page.getByRole("group", { name: "Columns" });
    const entity = page.getByRole("columnheader", { name: "Object" });
    await expect(entity).toBeVisible();
    await expect(menu).toBeHidden();

    await button.click();
    await expect(menu).toBeVisible();
    // The first item has the focus, so the keyboard needs no extra Tab.
    await expect(menu.getByRole("checkbox").first()).toBeFocused();
    // The identifier and the action column are not offered.
    await expect(menu.getByRole("checkbox", { name: "When" })).toHaveCount(0);
    await expect(menu.getByRole("checkbox", { name: "Details" })).toHaveCount(
      0,
    );

    await menu.getByRole("checkbox", { name: "Object" }).uncheck();
    await expect(entity).toHaveCount(0);

    await page.keyboard.press("Escape");
    await expect(menu).toBeHidden();
    await expect(button).toBeFocused();
    await expect(entity).toHaveCount(0);

    // A click outside closes it too.
    await button.click();
    await expect(menu).toBeVisible();
    await page.getByRole("heading", { level: 1 }).click();
    await expect(menu).toBeHidden();

    // Tab out of the last item closes it (it never stays open behind the focus).
    await button.click();
    await expect(menu).toBeVisible();
    await menu.getByRole("button", { name: "Show all columns" }).focus();
    await page.keyboard.press("Tab");
    await expect(menu).toBeHidden();

    await button.click();
    await menu.getByRole("button", { name: "Show all columns" }).click();
    await expect(entity).toBeVisible();
  });

  test("given a column is hidden at 1280, when the window narrows to a phone, then the card shows that column again and there is no Columns button", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await openAudit(page);
    await page.getByRole("button", { name: "Columns" }).click();
    await page
      .getByRole("group", { name: "Columns" })
      .getByRole("checkbox", { name: "Object" })
      .uncheck();
    await expect(
      page.getByRole("columnheader", { name: "Object" }),
    ).toHaveCount(0);
    await page.keyboard.press("Escape");

    await page.setViewportSize({ width: 360, height: 740 });

    // A card list: every cell carries its label, the hidden column's too.
    await expect(
      page.locator('app-table td[data-label="Object"]:visible').first(),
    ).toBeVisible();
    await expect(page.getByRole("button", { name: "Columns" })).toBeHidden();
  });
});

test.describe("TB Paging on a phone", () => {
  test.use({
    viewport: { width: 360, height: 740 },
    hasTouch: true,
    isMobile: true,
  });

  test("given 360px, then the pager keeps Previous, Next and the range, there is no Columns menu and the page does not scroll sideways", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await openAudit(page);
    const pager = page.locator("app-pager");

    await expect(
      pager.getByRole("button", { name: "Next page" }),
    ).toBeVisible();
    await expect(
      pager.getByRole("button", { name: "Previous page" }),
    ).toBeVisible();
    await expect(
      pager.getByRole("button", { name: "First page" }),
    ).toBeHidden();
    await expect(pager.getByRole("button", { name: "Last page" })).toBeHidden();
    await expect(pager.getByLabel("Rows per page")).toBeHidden();
    await expect(page.getByRole("button", { name: "Columns" })).toBeHidden();

    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);

    await pager.getByRole("button", { name: "Next page" }).click();
    await expect(pager.locator(".ui-pager-range")).toHaveText("26-50 of 333");
  });
});
