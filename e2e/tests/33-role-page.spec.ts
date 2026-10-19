import AxeBuilder from '@axe-core/playwright';
import { type Browser, type Page } from '@playwright/test';
import { API_URL, WEB_URL } from '../src/config';
import { unique, type ApiRole, type ImsApi } from '../src/api';
import { expect, test } from '../src/fixtures';
import { LoginPage, Shell, goTo } from '../src/pages/shell';

// docs/design/roles-permissions-ux.md 3.2 (the permission list), 3.12 (special roles, view only), 6.2 (accessibility); lean scope RP02.
const checkbox = (page: Page, code: string) => page.locator(`[data-permission="${code}"] input`);
const saveButton = (page: Page) => page.getByRole('button', { name: 'Save', exact: true }).first();
const nameBox = (page: Page) => page.getByRole('textbox', { name: /^Name/ });
const state = (page: Page, code: string) =>
  page.locator(`[data-permission="${code}"] .permission-list-state`);

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

test.describe('RP02 Role page', () => {
  const created: ApiRole[] = [];
  const users: string[] = [];

  async function makeRole(api: ImsApi, permissions: string[], label = 'role'): Promise<ApiRole> {
    const role = await api.createRole(`E2E ${label} ${unique()}`, permissions);
    created.push(role);
    return role;
  }

  /** A throw-away user holding the role; it goes back to Clerk after the scenario. */
  async function makeUser(api: ImsApi, role: ApiRole) {
    const user = await api.createUser({ role: role.code });
    users.push(user.username);
    return user;
  }

  test.afterEach(async ({ api }) => {
    const admin = api('admin');
    // A user cannot be deleted, so a throw-away user goes back to Clerk and is turned off, then its role can go.
    for (const username of users.splice(0)) {
      const found = await admin.call<{ items: { id: string; displayName: string }[] }>(
        'GET',
        `/api/users?search=${username}`
      );
      for (const user of found.items) {
        await admin.raw('PUT', `/api/users/${user.id}`, {
          username,
          email: `${username}@example.test`,
          displayName: user.displayName,
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

  test('given a person who may only read roles, then a role opens as "View only" with each permission Allowed or Not allowed by section and nothing to change', async ({
    browser,
    api
  }) => {
    const admin = api('admin');
    const viewer = await makeRole(admin, ['roles.read'], 'viewer');
    const target = await makeRole(admin, ['roles.read', 'stock.read'], 'target');
    const user = await admin.createUser({ role: viewer.code });
    users.push(user.username);
    const session = await signInAsUser(browser, user);
    const { page } = session;

    try {
      await goTo(page, '/roles');
      await page
        .getByRole('row', { name: new RegExp(target.name) })
        .getByRole('link')
        .first()
        .click();

      await expect(page.getByRole('heading', { level: 1, name: target.name })).toBeVisible();
      await expect(page.getByText('View only')).toBeVisible();
      await expect(state(page, 'stock.read')).toHaveText('Allowed');
      await expect(state(page, 'document.post')).toHaveText('Not allowed');
      await expect(page.locator('section h3').first()).toBeVisible();
      await expect(page.getByRole('checkbox')).toHaveCount(0);
      await expect(page.getByRole('button', { name: /Save|Delete/ })).toHaveCount(0);

      // Reading is all this person may do: no New role, and the new-role address is the forbidden page.
      await goTo(page, '/roles');
      await expect(page.getByRole('link', { name: 'New role' })).toHaveCount(0);
      await goTo(page, '/roles/new');
      await expect(page).toHaveURL(/\/forbidden$/);
    } finally {
      await session.close();
    }
  });

  test('given the Administrator role, then it says it always holds every permission and cannot be changed', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    await page
      .getByRole('row', { name: /^Admin\b/ })
      .getByRole('link')
      .first()
      .click();

    await expect(
      page.getByText('always holds every permission and cannot be changed')
    ).toBeVisible();
    await expect(state(page, 'roles.manage')).toHaveText('Allowed');
  });

  test('given an address that names no role, then the page is "Page not found" and shows no permissions', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await goTo(page, '/roles/00000000-0000-4000-8000-0000000000aa');
    await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible();

    await goTo(page, '/roles/31');
    await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible();
    await expect(page.locator('section h3')).toHaveCount(0);
  });

  test('given a 360 px phone, then a role form and the read-only list have no sideways scroll and every permission is named', async ({
    page,
    signIn,
    api
  }) => {
    const role = await makeRole(api('admin'), ['stock.read'], 'phone');
    await page.setViewportSize({ width: 360, height: 740 });
    await signIn('admin');
    const noOverflow = () =>
      page.evaluate(
        () =>
          document.documentElement.scrollWidth <= window.innerWidth &&
          document.body.scrollWidth <= window.innerWidth
      );

    await goTo(page, `/roles/${role.id}`);
    await expect(page.locator('fieldset').first()).toBeVisible();
    expect(await noOverflow()).toBe(true);

    await goTo(page, '/roles');
    await page
      .getByRole('row', { name: /^Admin\b/ })
      .getByRole('link')
      .first()
      .click();
    await expect(page.locator('section h3').first()).toBeVisible();
    expect(await noOverflow()).toBe(true);
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
    await expect(
      page.getByRole('checkbox', { name: 'Post approved documents', exact: true })
    ).toHaveAccessibleDescription(/Post a document that needed approval\..*Needs: Post documents/);
    const { violations } = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(violations.filter(v => v.impact === 'serious' || v.impact === 'critical')).toEqual([]);
    await checkbox(page, 'reports.read').uncheck();
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
    await expect(checkbox(page, 'stock.read')).toBeChecked();

    await checkbox(page, 'document.post').check();
    await api('admin').setRolePermissions(role.id, ['stock.read', 'reports.read']);
    await saveButton(page).click();

    const alert = page.getByRole('alert');
    await expect(alert).toContainText('Someone changed this role while you were editing');
    expect((await api('admin').getRole(role.id)).permissions.sort()).toEqual([
      'reports.read',
      'stock.read'
    ]);

    await alert.getByRole('button', { name: 'Reload the role' }).click();
    await expect(checkbox(page, 'reports.read')).toBeChecked();
    await expect(checkbox(page, 'document.post')).not.toBeChecked();
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
      await goTo(page, `/roles/${editable.id}`);
      await expect(checkbox(page, 'document.post')).toBeDisabled();
      await expect(
        page.locator('[data-permission="document.post"]').getByText("You can't give a permission")
      ).toBeVisible();
      await expect(checkbox(page, 'stock.read')).toBeEnabled();

      await goTo(page, `/roles/${bigger.id}`);
      await expect(page.getByText('holds permissions you do not have')).toBeVisible();
      await expect(saveButton(page)).toHaveCount(0);

      await goTo(page, `/roles/${editable.id}`);

      await expect(checkbox(page, 'stock.read')).toBeEnabled();
      await checkbox(page, 'stock.read').uncheck();
      // Meanwhile the role grows beyond what the manager holds: the server refuses and the page says why.
      await admin.setRolePermissions(editable.id, ['stock.read', 'document.post']);
      await saveButton(page).click();

      await expect(page.getByRole('alert')).toContainText(
        'You cannot change a role that holds more permissions than you.'
      );
    } finally {
      await session.close();
    }
  });

  test('given the Admin adds a role and ticks Post approved documents, then Post documents comes with it, the role is created and the API holds both', async ({
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
    await checkbox(page, 'document.post.approved').check();
    await expect(checkbox(page, 'document.post')).toBeChecked();
    await expect(page.getByRole('status').filter({ hasText: 'Also turned on' })).toContainText(
      'Post documents'
    );
    await saveButton(page).click();

    await expect(shell.toast(`Role "${name}" created.`)).toBeVisible();
    await expect(page).toHaveURL(/\/roles\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
    const id = page.url().split('/').pop() as string;
    const role = await api('admin').getRole(id);
    created.push(role);
    expect([...role.permissions].sort()).toEqual(['document.post', 'document.post.approved']);
  });

  test('given a standard role, then its name is fixed, its permissions are editable and there is no Delete', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    await page
      .getByRole('row', { name: /^Clerk\b/ })
      .getByRole('link')
      .first()
      .click();

    await expect(page.getByText('A standard role keeps its name')).toBeVisible();
    await expect(nameBox(page)).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Delete role' })).toHaveCount(0);
    await expect(checkbox(page, 'stock.read')).toBeEnabled();
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
    await shell
      .dialog(`Delete "${role.name}"?`)
      .getByRole('button', { name: 'Delete role' })
      .click();
    await expect(page.getByRole('alert')).toContainText('1 person has this role');
    await expect(page).toHaveURL(new RegExp(`/roles/${role.id}$`));

    // The throw-away user goes back to Clerk, then the same Delete works.
    const [found] = (
      await admin.call<{ items: { id: string; username: string }[] }>(
        'GET',
        `/api/users?search=${users[users.length - 1]}`
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
    await shell
      .dialog(`Delete "${role.name}"?`)
      .getByRole('button', { name: 'Delete role' })
      .click();

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
    await checkbox(page, 'reports.read').check();

    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    const dialog = shell.dialog('Discard changes?');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Keep editing' }).click();
    await expect(page).toHaveURL(new RegExp(`/roles/${role.id}$`));
    await expect(checkbox(page, 'reports.read')).toBeChecked();

    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    await shell.dialog('Discard changes?').getByRole('button', { name: 'Discard' }).click();
    await expect(page).toHaveURL(/\/roles$/);
  });
});
