import type { Page, Request } from "@playwright/test";
import { expect, test } from "../src/fixtures";
import { goTo } from "../src/pages/shell";

// R23 (docs/design/2026-refresh-plan.md): the From / To filters are text date fields in the app format `dd/MM/yyyy`, not the
// browser's mm/dd/yyyy input. The value sent to the API stays yyyy-MM-dd. Covers NV-27, PS-15 and EA-29.

const EXAMPLE = "09/10/2026";
const PATTERN = "dd/mm/yyyy";

/** A request whose query asks for this filter day (the API client writes the names with a capital: From, To). */
const asksFor = (name: "from" | "to", day: string) => (request: Request) =>
  new RegExp(`[?&]${name}=${day}(&|$)`, "i").test(request.url());

const fromBox = (page: Page) =>
  page.getByRole("textbox", { name: "From", exact: true });
const toBox = (page: Page) =>
  page.getByRole("textbox", { name: "To", exact: true });

/** A date typed into a field is read when the user leaves it (Tab), as a person would. */
async function typeDate(
  page: Page,
  box: ReturnType<typeof fromBox>,
  text: string,
): Promise<void> {
  await box.fill(text);
  await box.press("Tab");
}

test.describe("R23 Date filters use the app format", () => {
  test("given the Audit log, when a date is typed in the app format, ISO or day first, then the API gets yyyy-MM-dd and the field shows dd/MM/yyyy", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/audit-log");
    await expect(
      page.getByRole("heading", { level: 1, name: "Audit log" }),
    ).toBeVisible();
    await expect(fromBox(page)).toHaveAttribute("placeholder", PATTERN);
    await expect(page.locator('input[type="date"]:visible')).toHaveCount(0);

    const sent = page.waitForRequest(asksFor("from", "2026-10-09"));
    await typeDate(page, fromBox(page), EXAMPLE);
    await sent;
    await expect(fromBox(page)).toHaveValue(EXAMPLE);

    for (const typed of ["2026-10-09", "09 Oct 2026", "9.10.2026", "09102026"]) {
      await typeDate(page, fromBox(page), "01/01/2026");
      const again = page.waitForRequest(asksFor("from", "2026-10-09"));
      await typeDate(page, fromBox(page), typed);
      await again;
      await expect(fromBox(page)).toHaveValue(EXAMPLE);
    }
  });

  test("given a date that does not exist, when the field is left, then it says what to type, and fixing it removes the sentence", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/audit-log");

    await typeDate(page, fromBox(page), "31 Feb 2026");

    const sentence = `Enter a date as dd/mm/yyyy, such as ${EXAMPLE}.`;
    await expect(page.getByText(sentence)).toBeVisible();
    await expect(fromBox(page)).toHaveAttribute("aria-invalid", "true");
    const described = await fromBox(page).getAttribute("aria-describedby");
    await expect(page.locator(`[id="${described}"]`)).toHaveText(sentence);

    await typeDate(page, fromBox(page), "28 Feb 2026");
    await expect(page.getByText(sentence)).toHaveCount(0);
    await expect(fromBox(page)).toHaveValue("28/02/2026");
  });

  test("given From after To, then the sentence is under To, no request is sent, and fixing or emptying a field removes it", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/receipts");
    const range = "From must be on or before To.";

    await typeDate(page, fromBox(page), "12/10/2026");
    const reversed: string[] = [];
    page.on("request", (request) => {
      if (asksFor("to", "2026-10-10")(request)) {
        reversed.push(request.url());
      }
    });
    await typeDate(page, toBox(page), "10/10/2026");

    await expect(
      page.locator(".ui-form-field-error").getByText(range),
    ).toBeVisible();
    // The empty area says the same instead of "No documents match": no search ran (and it still offers Clear filters).
    await expect(page.locator(".ui-empty-state-message")).toHaveText(range);
    await expect(
      page.getByRole("button", { name: "Clear filters" }),
    ).toBeVisible();
    await expect(toBox(page)).toHaveAttribute("aria-invalid", "true");
    expect(reversed).toEqual([]);

    await typeDate(page, toBox(page), "12/10/2026");
    await expect(page.getByText(range)).toHaveCount(0);

    await typeDate(page, toBox(page), "");
    await expect(toBox(page)).toHaveValue("");
  });

  test("given the Reports filters in Malay and Chinese, then the date is dd/MM/yyyy in both, the pattern hint is in that language, and a month name is still read", async ({
    page,
    signIn,
    shell,
  }) => {
    await signIn("supervisor");
    await goTo(page, "/reports");
    await typeDate(page, fromBox(page), EXAMPLE);

    await shell.setLanguage("Bahasa Melayu");
    const dari = page.getByRole("textbox", { name: "Dari", exact: true });
    await expect(dari).toHaveValue(EXAMPLE);
    await expect(dari).toHaveAttribute("placeholder", "hh/bb/tttt");
    await typeDate(page, dari, "10 Mac 2026");
    await expect(dari).toHaveValue("10/03/2026");

    await shell.setLanguage("中文");
    const cong = page.getByRole("textbox", { name: "从", exact: true });
    await expect(cong).toHaveValue("10/03/2026");
    await expect(cong).toHaveAttribute("placeholder", "日/月/年");
    await typeDate(page, cong, "20/10/2026");
    await expect(cong).toHaveValue("20/10/2026");
    await typeDate(page, cong, "2026年10月15日");
    await expect(cong).toHaveValue("15/10/2026");
  });

  test("given a keyboard user (EA-29), then the calendar button is a 44px tab stop with a name and a visible focus ring, and the picker input is never a tab stop", async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/audit-log");

    await fromBox(page).focus();
    await page.keyboard.press("Tab");
    const calendar = page.getByRole("button", { name: /^Choose date/ }).first();
    await expect(calendar).toBeFocused();
    await expect(calendar).toHaveAccessibleName("Choose date From");

    const ring = await calendar.evaluate((button) => {
      const style = getComputedStyle(button);
      const box = button.getBoundingClientRect();
      return {
        style: style.outlineStyle,
        width: parseFloat(style.outlineWidth),
        height: box.height,
        buttonWidth: box.width,
      };
    });
    expect(ring.style).not.toBe("none");
    expect(ring.width).toBeGreaterThanOrEqual(2);
    expect(Math.min(ring.height, ring.buttonWidth)).toBeGreaterThanOrEqual(44);

    // The next stop is the To box, not a hidden browser date input.
    await page.keyboard.press("Tab");
    await expect(toBox(page)).toBeFocused();
  });
});
