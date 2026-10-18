import { type Browser, type Page } from '@playwright/test';
import { API_URL, WEB_URL } from '../src/config';
import { unique, type ApiRole, type ImsApi } from '../src/api';
import { expect, test } from '../src/fixtures';
import { LoginPage, Shell, goTo } from '../src/pages/shell';

// docs/design/roles-permissions-ux.md 3 (the editor), 5 (safety), 8.1 RPE; lean scope RP02/RP03; ADR roles-permissions 2.9.
const box = (page: Page, code: string) => page.locator(`[data-permission="${code}"] input`);
const saveButton = (page: Page) => page.getByRole('button', { name: 'Save', exact: true });
const nameBox = (page: Page) => page.getByRole('textbox', { name: /^Name/ });

/** A throw-away user with a made-up password signs in through the real form, in a browser context of its own. */
async function signInAsUser(
  browser: Browser,
  user: { username: string; password: string }
): Promise<{ page: Page; close: () => Promise<void> }> {
  const context = await browser.newContext({
    baseURL: WEB_URL,
    locale: 'en-US',
    timezoneId: 'Asia/Kuala_Lumpur',
    serviceWorkers: 'block'
  });
  await context.route(/\/assets\/config\.json/, route =>
    route.fulfill({ json: { apiBaseUrl: API_URL } })
  );
  const page = await context.newPage();
  const login = new LoginPage(page);
  await login.open();
  await login.fill(user.username, user.password);
  await expect(new Shell(page).account).toBeVisible();
  return { page, close: () => context.close() };
}

test.describe('RP02 Role editor', () => {
  const created: ApiRole[] = [];
  const users: { username: string; api: ImsApi }[] = [];

  /** A custom role made through the API, removed again after the scenario. */
  async function makeRole(api: ImsApi, permissions: string[], label = 'role'): Promise<ApiRole> {
    const role = await api.createRole(`E2E ${label} ${unique()}`, permissions);
    created.push(role);
    return role;
  }

  async function makeUser(api: ImsApi, role: ApiRole) {
    const user = await api.createUser({ role: role.code });
    users.push({ username: user.username, api });
    return user;
  }

  test.afterEach(async ({ api }) => {
    const admin = api('admin');
    // A user cannot be deleted, so the role of a throw-away user goes back to Clerk and the account is turned off.
    for (const { username } of users.splice(0)) {
      const page = await admin.call<{ items: { id: string; displayName: string }[] }>(
        'GET',
        `/api/users?search=${username}`
      );
      for (const found of page.items) {
        await admin.raw('PUT', `/api/users/${found.id}`, {
          username,
          email: `${username}@example.test`,
          displayName: found.displayName,
          role: 'Clerk',
          isActive: false,
          warehouseIds: []
        });
      }
    }
    for (const role of created.splice(0)) {
      await admin.raw('DELETE', `/api/roles/${role.id}`);
    }
  });

  test('given the Admin adds a role and ticks Post documents, then See stock comes with it, the role is created and the API holds both', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const name = `E2E new ${unique()}`;
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    await page.getByRole('link', { name: 'New role' }).click();
    await expect(page).toHaveURL(/\/roles\/new$/);

    await nameBox(page).fill(name);
    await box(page, 'document.post').check();
    await expect(box(page, 'stock.read')).toBeChecked();
    await expect(page.getByRole('status').filter({ hasText: 'Also turned on' })).toContainText(
      'See stock'
    );
    await saveButton(page).click();

    await expect(shell.toast(`Role "${name}" created.`)).toBeVisible();
    await expect(page).toHaveURL(/\/roles\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
    const id = page.url().split('/').pop() as string;
    const role = await api('admin').getRole(id);
    created.push(role);
    expect([...role.permissions].sort()).toEqual(['document.post', 'stock.read']);
  });

  test('given the Admin opens a role, unticks a permission and saves, then the toast says what changed and the API agrees', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const role = await makeRole(api('admin'), ['stock.read', 'reports.read']);
    await signIn('admin');
    await goTo(page, `/roles/${role.id}`);

    await expect(page.getByRole('heading', { level: 1, name: role.name })).toBeVisible();
    await box(page, 'reports.read').uncheck();
    await saveButton(page).click();

    await expect(shell.toast('1 permission removed')).toBeVisible();
    expect((await api('admin').getRole(role.id)).permissions).toEqual(['stock.read']);
  });

  test('given someone else changed the role meanwhile, then Save says so, nothing is saved, and Reload shows their version', async ({
    page,
    signIn,
    api
  }) => {
    const role = await makeRole(api('admin'), ['stock.read']);
    await signIn('admin');
    await goTo(page, `/roles/${role.id}`);
    await expect(box(page, 'stock.read')).toBeChecked();

    await box(page, 'document.post').check();
    await api('admin').setRolePermissions(role.id, ['stock.read', 'reports.read']);
    await saveButton(page).click();

    const alert = page.getByRole('alert');
    await expect(alert).toContainText('Someone changed this role while you were editing');
    expect((await api('admin').getRole(role.id)).permissions.sort()).toEqual([
      'reports.read',
      'stock.read'
    ]);

    await alert.getByRole('button', { name: 'Reload the role' }).click();
    await expect(box(page, 'reports.read')).toBeChecked();
    await expect(box(page, 'document.post')).not.toBeChecked();
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  test('given a delegated manager, then a permission they lack is disabled with its reason, a role that holds more is view-only, and a save the server refuses is explained', async ({
    browser,
    api
  }) => {
    const admin = api('admin');
    const manager = await makeRole(admin, ['roles.read', 'roles.manage', 'stock.read'], 'manager');
    const bigger = await makeRole(admin, ['stock.read', 'document.post'], 'bigger');
    const editable = await makeRole(admin, ['stock.read'], 'editable');
    const user = await makeUser(admin, manager);
    const session = await signInAsUser(browser, user);
    const { page } = session;

    try {
      await goTo(page, '/roles/new');
      await expect(box(page, 'document.post')).toBeDisabled();
      await expect(
        page.locator('[data-permission="document.post"]').getByText("You can't give a permission")
      ).toBeVisible();
      await expect(box(page, 'stock.read')).toBeEnabled();

      await goTo(page, `/roles/${bigger.id}`);
      await expect(page.getByText('holds permissions you do not have')).toBeVisible();
      await expect(saveButton(page)).toHaveCount(0);

      await goTo(page, `/roles/${editable.id}`);
      await expect(box(page, 'stock.read')).toBeEnabled();
      await box(page, 'stock.read').uncheck();
      // Meanwhile the role grows beyond what the manager holds: the server refuses and the page says why.
      await admin.setRolePermissions(editable.id, ['stock.read', 'document.post']);
      await saveButton(page).click();

      await expect(page.getByRole('alert')).toContainText('You can only give permissions you hold');
    } finally {
      await session.close();
    }
  });

  test('given a person who may only read roles, then Roles has no New role, a role opens as "View only" with disabled boxes and no Save', async ({
    browser,
    api
  }) => {
    const admin = api('admin');
    const viewer = await makeRole(admin, ['roles.read'], 'viewer');
    const target = await makeRole(admin, ['roles.read'], 'target');
    const session = await signInAsUser(browser, await makeUser(admin, viewer));
    const { page } = session;

    try {
      await goTo(page, '/roles');
      await expect(page.getByRole('heading', { level: 1, name: 'Roles' })).toBeVisible();
      await expect(page.getByText('View only')).toBeVisible();
      await expect(page.getByRole('link', { name: 'New role' })).toHaveCount(0);

      await goTo(page, `/roles/${target.id}`);
      await expect(page.getByText('You can see this role but not change it')).toBeVisible();
      await expect(saveButton(page)).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Delete role' })).toHaveCount(0);
      await expect(box(page, 'roles.read')).toBeChecked();
      await expect(box(page, 'roles.read')).toBeDisabled();

      await goTo(page, '/roles/new');
      await expect(page).toHaveURL(/\/forbidden$/);
    } finally {
      await session.close();
    }
  });

  test('given the Administrator role, then it opens view-only and says it always holds every permission', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    await page.getByRole('row', { name: /^Admin\b/ }).getByRole('link').first().click();

    await expect(page.getByText('always holds every permission and cannot be changed')).toBeVisible();
    await expect(saveButton(page)).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Delete role' })).toHaveCount(0);
    await expect(box(page, 'roles.manage')).toBeDisabled();
  });

  test('given a standard role, then its name is fixed, its permissions are editable and there is no Delete', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    await page.getByRole('row', { name: /^Clerk\b/ }).getByRole('link').first().click();

    await expect(page.getByText('A standard role keeps its name')).toBeVisible();
    await expect(nameBox(page)).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Delete role' })).toHaveCount(0);
    await expect(box(page, 'stock.read')).toBeEnabled();
  });

  test('given a role that people still have, then Delete asks first and the refusal counts the people; once they are moved, it deletes', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const admin = api('admin');
    const role = await makeRole(admin, ['stock.read'], 'busy');
    await makeUser(admin, role);
    await signIn('admin');
    await goTo(page, `/roles/${role.id}`);

    await page.getByRole('button', { name: 'Delete role' }).click();
    await shell.dialog(`Delete "${role.name}"?`).getByRole('button', { name: 'Delete role' }).click();
    await expect(page.getByRole('alert')).toContainText('1 person has this role');
    await expect(page).toHaveURL(new RegExp(`/roles/${role.id}$`));

    // The throw-away user goes back to Clerk, then the same Delete works.
    const [found] = (
      await admin.call<{ items: { id: string; username: string }[] }>(
        'GET',
        `/api/users?search=${users[users.length - 1].username}`
      )
    ).items;
    await admin.call('PUT', `/api/users/${found.id}`, {
      username: found.username,
      email: `${found.username}@example.test`,
      displayName: found.username,
      role: 'Clerk',
      isActive: false,
      warehouseIds: []
    });
    await page.getByRole('button', { name: 'Delete role' }).click();
    await shell.dialog(`Delete "${role.name}"?`).getByRole('button', { name: 'Delete role' }).click();

    await expect(page).toHaveURL(/\/roles$/);
    await expect(shell.toast(`Role "${role.name}" deleted.`)).toBeVisible();
  });

  test('given unsaved ticks, then leaving asks first: Keep editing stays, Discard leaves', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const role = await makeRole(api('admin'), ['stock.read']);
    await signIn('admin');
    await goTo(page, `/roles/${role.id}`);
    await box(page, 'reports.read').check();

    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    const dialog = shell.dialog('Discard changes?');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Keep editing' }).click();
    await expect(page).toHaveURL(new RegExp(`/roles/${role.id}$`));
    await expect(box(page, 'reports.read')).toBeChecked();

    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    await shell.dialog('Discard changes?').getByRole('button', { name: 'Discard' }).click();
    await expect(page).toHaveURL(/\/roles$/);
  });

  test('given a 360 px phone, then the editor has no sideways scroll and every permission has a labelled box in a group', async ({
    page,
    signIn,
    api
  }) => {
    const role = await makeRole(api('admin'), ['stock.read']);
    await page.setViewportSize({ width: 360, height: 740 });
    await signIn('admin');

    for (const path of ['/roles/new', `/roles/${role.id}`]) {
      await goTo(page, path);
      await expect(page.locator('fieldset').first()).toBeVisible();
      const overflow = await page.evaluate(
        () =>
          document.documentElement.scrollWidth > window.innerWidth ||
          document.body.scrollWidth > window.innerWidth
      );
      expect(overflow).toBe(false);
    }
    await expect(page.locator('fieldset > legend h2').first()).toBeVisible();
    const unnamed = await page
      .locator('fieldset input[type="checkbox"]')
      .evaluateAll(
        inputs =>
          inputs.filter(input => !(input as HTMLInputElement).labels?.[0]?.textContent?.trim())
            .length
      );
    expect(unnamed).toBe(0);
  });
});
