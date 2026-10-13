import type { Locator, Page } from "@playwright/test";

// Helpers for the R11 specs: a stubbed outage with a known sentence, and the measurements that prove nothing is covered.

export const SENTENCE = "The database is not available.";

/** Three lines on a 320px phone: the case where the old toast hid the filters as well as the title. */
export const LONG_SENTENCE =
  "The database is not available. The report could not be loaded because the server is busy right now. " +
  "Wait a few minutes and try again, or ask an administrator to check the service.";

/** Answers the matching calls with a problem response (only the given method, when one is named). */
export async function failCalls(
  page: Page,
  url: RegExp,
  detail = SENTENCE,
  options: { method?: string; status?: number } = {},
): Promise<void> {
  await page.route(url, (route) =>
    options.method && route.request().method() !== options.method
      ? route.fallback()
      : route.fulfill({
          status: options.status ?? 503,
          contentType: "application/problem+json",
          body: JSON.stringify({ title: "Unavailable", detail }),
        }),
  );
}

/** How many live regions (role alert or status, or aria-live) hold the sentence, outermost only. */
export async function announcedBy(page: Page, text = SENTENCE): Promise<number> {
  return page.evaluate((sentence) => {
    const live = '[role="alert"], [role="status"], [aria-live]:not([aria-live="off"])';
    return Array.from(document.querySelectorAll<HTMLElement>(live)).filter(
      (region) =>
        !region.parentElement?.closest(live) && (region.textContent ?? "").includes(sentence),
    ).length;
  }, text);
}

/** True when the two boxes share any area. */
export async function overlap(a: Locator, b: Locator): Promise<boolean> {
  const [x, y] = await Promise.all([a.boundingBox(), b.boundingBox()]);
  return (
    !!x && !!y && x.x < y.x + y.width && y.x < x.x + x.width && x.y < y.y + y.height && y.y < x.y + x.height
  );
}

/** A tap at the centre of the control reaches the control itself: nothing lies on top of it. */
export async function isReachable(control: Locator): Promise<boolean> {
  await control.scrollIntoViewIfNeeded();
  return control.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
    return hit === element || element.contains(hit);
  });
}
