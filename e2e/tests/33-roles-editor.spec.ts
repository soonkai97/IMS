import { type Browser, type Page } from "@playwright/test";
import { API_URL, WEB_URL } from "../src/config";
import { unique, type ApiRole, type ImsApi } from "../src/api";
import { expect, test } from "../src/fixtures";
import { LoginPage, Shell, goTo } from "../src/pages/shell";

// docs/design/roles-permissions-ux.md 3.2 (the checklist), 3.12 (special roles, view only), 6.2 (accessibility); lean scope RP02.
const box = (page: Page, code: string) =>
  page.locator(`[data-permission="${code}"] input`);

/** A throw-away user with a made-up password signs in through the real form, in a browser context of its own. */
async function signInAsUser(
  browser: Browser,
  user: { username: string; password: string },
): Promise<{ page: Page; close: () => Promise<void> }> {
  const context = await browser.newContext({
    baseURL: WEB_URL,
    locale: "en-US",
    timezoneId: "Asia/Kuala_Lumpur",
    serviceWorkers: "block",
  });
  await context.route(/\/assets\/config\.json/, (route) =>
    route.fulfill({ json: { apiBaseUrl: API_URL } }),
  );
  const page = await context.newPage();
  const login = new LoginPage(page);
  await login.open();
  await login.fill(user.username, user.password);
  await expect(new Shell(page).account).toBeVisible();
  return { page, close: () => context.close() };
}

test.describe("RP02 Role page (view only)", () => {
  const created: ApiRole[] = [];
  const users: string[] = [];

  async function makeRole(
    api: ImsApi,
    permissions: string[],
    label: string,
  ): Promise<ApiRole> {
    const role = await api.createRole(`E2E ${label} ${unique()}`, permissions);
    created.push(role);
    return role;
  }

  test.afterEach(async ({ api }) => {
    const admin = api("admin");
    // A user cannot be deleted, so a throw-away user goes back to Clerk and is turned off, then its role can go.
    for (const username of users.splice(0)) {
      const found = await admin.call<{
        items: { id: string; displayName: string }[];
      }>("GET", `/api/users?search=${username}`);
      for (const user of found.items) {
        await admin.raw("PUT", `/api/users/${user.id}`, {
          username,
          email: `${username}@example.test`,
          displayName: user.displayName,
          role: "Clerk",
          isActive: false,
          warehouseIds: [],
        });
      }
    }
    for (const role of created.splice(0)) {
      await admin.raw("DELETE", `/api/roles/${role.id}`);
    }
  });

  test('given a person who may only read roles, then a role opens as "View only" with its permissions ticked by section and no box or button to change them', async ({
    browser,
    api,
  }) => {
    const admin = api("admin");
    const viewer = await makeRole(admin, ["roles.read"], "viewer");
    const target = await makeRole(
      admin,
      ["roles.read", "stock.read"],
      "target",
    );
    const user = await admin.createUser({ role: viewer.code });
    users.push(user.username);
    const session = await signInAsUser(browser, user);
    const { page } = session;

    try {
      await goTo(page, "/roles");
      await page
        .getByRole("row", { name: new RegExp(target.name) })
        .getByRole("link")
        .first()
        .click();

      await expect(
        page.getByRole("heading", { level: 1, name: target.name }),
      ).toBeVisible();
      await expect(page.getByText("View only")).toBeVisible();
      await expect(box(page, "stock.read")).toBeChecked();
      await expect(box(page, "stock.read")).toBeDisabled();
      await expect(box(page, "document.post")).not.toBeChecked();
      await expect(page.locator("fieldset > legend h2").first()).toBeVisible();
      await expect(
        page.getByRole("button", { name: /Save|Delete/ }),
      ).toHaveCount(0);
    } finally {
      await session.close();
    }
  });

  test("given the Administrator role, then it says it always holds every permission and cannot be changed", async ({
    page,
    signIn,
    shell,
  }) => {
    await signIn("admin");
    await shell.nav.getByRole("link", { name: "Roles" }).click();
    await page
      .getByRole("row", { name: /^Admin\b/ })
      .getByRole("link")
      .first()
      .click();

    await expect(
      page.getByText("always holds every permission and cannot be changed"),
    ).toBeVisible();
    await expect(box(page, "roles.manage")).toBeChecked();
    await expect(box(page, "roles.manage")).toBeDisabled();
  });

  test('given an address that names no role, then the page is "Page not found" and shows no permissions', async ({
    page,
    signIn,
  }) => {
    await signIn("admin");
    await goTo(page, "/roles/00000000-0000-4000-8000-0000000000aa");
    await expect(
      page.getByRole("heading", { level: 1, name: "Page not found" }),
    ).toBeVisible();

    await goTo(page, "/roles/31");
    await expect(
      page.getByRole("heading", { level: 1, name: "Page not found" }),
    ).toBeVisible();
    await expect(page.locator("fieldset")).toHaveCount(0);
  });

  test("given a 360 px phone, then the role page has no sideways scroll and every box has a name", async ({
    page,
    signIn,
    api,
  }) => {
    const role = await makeRole(api("admin"), ["stock.read"], "phone");
    await page.setViewportSize({ width: 360, height: 740 });
    await signIn("admin");
    await goTo(page, `/roles/${role.id}`);

    await expect(page.locator("fieldset").first()).toBeVisible();
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth > window.innerWidth ||
        document.body.scrollWidth > window.innerWidth,
    );
    expect(overflow).toBe(false);
    const unnamed = await page
      .locator('fieldset input[type="checkbox"]')
      .evaluateAll(
        (inputs) =>
          inputs.filter(
            (input) =>
              !(input as HTMLInputElement).labels?.[0]?.textContent?.trim(),
          ).length,
      );
    expect(unnamed).toBe(0);
  });
});
