import { expect, test } from "../src/fixtures";
import { goTo } from "../src/pages/shell";
import { StockPages } from "../src/pages/screens";

// QA round 2026-10-19: wording and validation findings fixed in the web app (novice 9, edge 9, supervisor 2).

test.describe("A Low stock card on a 360 px phone", () => {
  test.use({ viewport: { width: 360, height: 740 } });

  test("given a product below its safety level, then the number it is short by has its words next to it", async ({
    page,
    signIn,
    api,
  }) => {
    const product = await api().createProduct({ safetyStock: 7 });
    await signIn("staff");
    const stock = new StockPages(page);
    await stock.openLowStock();

    const row = stock.lowStockRow(product.sku);
    await expect(row).toBeVisible();
    // The words are drawn from the cell's data-label by the card layout: read what the browser paints.
    const cell = row.locator('.ui-table-cell[data-label="Short by"]');
    await expect(cell).toContainText("7");
    const label = await cell.evaluate((element) => {
      const before = getComputedStyle(element, "::before");
      return { content: before.content, display: before.display };
    });
    expect(label.display).not.toBe("none");
    expect(label.content).toContain("Short by");
  });
});

test.describe("Product form", () => {
  test("given an empty form, when Save is pressed, then the focus goes to the first field to fix", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/products/new");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();

    await page.getByRole("button", { name: "Save" }).first().click();

    const sku = page.getByRole("textbox", { name: /^SKU/ });
    await expect(sku).toHaveAttribute("aria-invalid", "true");
    await expect(sku).toBeFocused();
  });

  test("given a product name of 300 characters, then the field takes all of it", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/products/new");
    const name = page.getByRole("textbox", { name: /^Name/ });
    await name.fill("N".repeat(300));

    await expect(name).toHaveValue("N".repeat(300));
  });
});
