import { expect, test } from '../src/fixtures';
import { goTo, loadPage } from '../src/pages/shell';

// docs/design/roles-permissions-ux.md 1.2 (menu and gate), 2 (the list), 8.1 RPN-01..03 and RPL-01; lean scope 6.1.
const STANDARD_ROLES = ['Admin', 'Owner', 'Supervisor', 'Warehouse staff', 'Clerk'];

test.describe('RP01 Roles list', () => {
  test('given the Admin opens Roles from the menu, then the five standard roles are marked by words, the Administrator as locked, and the page says a change takes about 5 minutes', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Roles' }).click();

    await expect(page).toHaveURL(/\/roles$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Roles' })).toBeVisible();
    await expect(page.getByText('within about 5 minutes')).toBeVisible();
    await expect(page.getByText('View only')).toHaveCount(0);

    for (const name of STANDARD_ROLES) {
      await expect(page.getByRole('row', { name: new RegExp(`^${name}\\b`) })).toBeVisible();
    }
    await expect(page.getByRole('row', { name: /^Admin\b/ })).toContainText('System, locked');
    await expect(page.getByRole('row', { name: /^Owner\b/ })).toContainText('System');
    await expect(page.getByRole('row', { name: /^Owner\b/ })).not.toContainText('locked');
    await expect(page.getByRole('row', { name: /^Clerk\b/ })).toContainText('Active');
  });

  test('given the Admin searches, then only the matching roles stay and an unknown word says so', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Roles' }).click();
    const search = page.getByRole('searchbox', { name: 'Search roles' });

    await search.fill('Warehouse');
    await expect(page.getByRole('row', { name: /^Warehouse staff\b/ })).toBeVisible();
    await expect(page.getByRole('row', { name: /^Owner\b/ })).toHaveCount(0);

    await search.fill('zzzz-no-such-role');
    await expect(page.getByText('No role matches "zzzz-no-such-role".')).toBeVisible();
  });

  for (const role of ['clerk', 'owner'] as const) {
    test(`given a ${role} signs in, then Roles is not in the menu and a typed /roles shows the forbidden page`, async ({
      page,
      signIn,
      shell
    }) => {
      await signIn(role);
      expect(await shell.menu()).not.toContain('Roles');

      await loadPage(page, '/roles');
      await expect(page).toHaveURL(/\/forbidden$/);
      await expect(page.getByText("You don't have access to this.").first()).toBeVisible();
    });
  }

  test('given a 360 px phone, then the Admin sees the roles as cards with no sideways scroll', async ({
    page,
    signIn
  }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await signIn('admin');
    await goTo(page, '/roles');

    await expect(page.getByRole('heading', { level: 1, name: 'Roles' })).toBeVisible();
    await expect(page.getByText('Warehouse staff').first()).toBeVisible();
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth > window.innerWidth ||
        document.body.scrollWidth > window.innerWidth
    );
    expect(overflow).toBe(false);
  });
});
