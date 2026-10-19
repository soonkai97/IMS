import { unique, type ApiRole } from "../src/api";
import { expect, test } from "../src/fixtures";
import { goTo } from "../src/pages/shell";

// QA round finding 1 (the Users form offered five fixed roles and the list showed the role code) and finding 5 (a user with no
// warehouse and not "sees every warehouse" saved and then saw an empty app). docs/architecture/adr-roles-permissions.md 2.6, 10 (RP-4b).
test.describe("Users: custom roles and the warehouse scope", () => {
  const roles: ApiRole[] = [];
  const usernames: string[] = [];

  test.afterEach(async ({ api }) => {
    const admin = api("admin");
    // A user cannot be deleted, so a throw-away user goes back to Clerk and is turned off, then its role can go.
    for (const username of usernames.splice(0)) {
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
          allWarehouses: false,
          warehouseIds: [],
        });
      }
    }
    for (const role of roles.splice(0)) {
      await admin.raw("DELETE", `/api/roles/${role.id}`);
    }
  });

  test("given a QA custom role, when the Admin assigns it from the Users form and saves the user again, then the list shows the role name and the role survives the re-save", async ({
    page,
    signIn,
    api,
  }) => {
    const token = unique().toLowerCase();
    const role = await api("admin").createRole(`QA Role ${token}`, [
      "stock.read",
    ]);
    roles.push(role);
    const username = `e2e-${token}`;
    usernames.push(username);
    await signIn("admin");
    await goTo(page, "/users");
    await expect(
      page.getByRole("heading", { level: 1, name: "Users" }),
    ).toBeVisible();

    await page.getByRole("button", { name: "New user" }).click();
    const dialog = page.getByRole("dialog", { name: "New user" });
    await dialog.getByRole("textbox", { name: /^Username/ }).fill(username);
    await dialog.getByRole("textbox", { name: /^Name/ }).fill(`QA ${token}`);
    await dialog
      .getByRole("textbox", { name: /^Email/ })
      .fill(`${username}@example.test`);
    await dialog
      .getByRole("combobox", { name: /^Role/ })
      .selectOption({ label: role.name });
    await dialog
      .getByRole("textbox", { name: /^Password/ })
      .fill(`Aa1!${token}-xYz`);
    await dialog
      .getByRole("checkbox", { name: "Sees every warehouse" })
      .check();
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dialog).toBeHidden();

    // The list shows the name people read, never the code.
    await page.getByRole("searchbox", { name: "Search users" }).fill(username);
    const row = page.getByRole("row", { name: new RegExp(username) });
    await expect(row).toContainText(role.name);
    await expect(row).not.toContainText(role.code);

    // Saving the same user again keeps the custom role (the form used to have no such option, so the save changed it).
    await row.getByRole("button", { name: /^Edit/ }).click();
    const edit = page.getByRole("dialog", { name: "Edit user" });
    await expect(
      edit.getByRole("combobox", { name: /^Role/ }).locator("option:checked"),
    ).toHaveText(role.name);
    await edit
      .getByRole("textbox", { name: /^Name/ })
      .fill(`QA renamed ${token}`);
    await edit.getByRole("button", { name: "Save", exact: true }).click();
    await expect(edit).toBeHidden();

    const saved = await api("admin").call<{
      items: { role: string; displayName: string }[];
    }>("GET", `/api/users?search=${username}`);
    expect(saved.items).toHaveLength(1);
    expect(saved.items[0].role).toBe(role.code);
    expect(saved.items[0].displayName).toBe(`QA renamed ${token}`);
    await expect(
      page.getByRole("row", { name: new RegExp(username) }),
    ).toContainText(role.name);
  });

  test('given a new user with no warehouse ticked, when the Admin saves, then a sentence says the user would see no warehouse, nothing is sent, and ticking "Sees every warehouse" lets the save through', async ({
    page,
    signIn,
  }) => {
    const token = unique().toLowerCase();
    const username = `e2e-${token}`;
    usernames.push(username);
    await signIn("admin");
    await goTo(page, "/users");
    await page.getByRole("button", { name: "New user" }).click();
    const dialog = page.getByRole("dialog", { name: "New user" });
    await dialog.getByRole("textbox", { name: /^Username/ }).fill(username);
    await dialog.getByRole("textbox", { name: /^Name/ }).fill(`QA ${token}`);
    await dialog
      .getByRole("textbox", { name: /^Email/ })
      .fill(`${username}@example.test`);
    await dialog
      .getByRole("combobox", { name: /^Role/ })
      .selectOption({ label: "Clerk" });
    await dialog
      .getByRole("textbox", { name: /^Password/ })
      .fill(`Aa1!${token}-xYz`);
    let saves = 0;
    page.on("request", (request) => {
      if (request.method() === "POST" && request.url().endsWith("/api/users")) {
        saves += 1;
      }
    });

    await dialog.getByRole("button", { name: "Save", exact: true }).click();

    await expect(dialog.getByRole("alert")).toContainText(
      "This user will see no warehouse. Pick at least one or tick Sees every warehouse.",
    );
    expect(saves).toBe(0);

    await dialog
      .getByRole("checkbox", { name: "Sees every warehouse" })
      .check();
    await expect(dialog.getByRole("alert")).toHaveCount(0);
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dialog).toBeHidden();
    expect(saves).toBe(1);
  });
});
