import { ROLE_LABEL, type Role } from '../src/config';
import { unique } from '../src/api';
import { expect, test } from '../src/fixtures';
import { goTo, loadPage } from '../src/pages/shell';
import { PartnersPage } from '../src/pages/screens';

// docs/design/mvp-ux.md 1.2, with the labels the app uses.
const READ_MASTER_DATA = ['Products', 'Categories & units', 'Warehouses', 'Partners'];
const CORE = ['Home', 'Receipts', 'Issues'];
const STOCK = ['Stock', 'Low stock'];
const MENU: Record<Role, string[]> = {
  admin: [
    ...CORE,
    'Approvals',
    ...STOCK,
    ...READ_MASTER_DATA,
    'Reports',
    'Users',
    'Roles',
    'Settings',
    'Modules',
    'Audit log'
  ],
  owner: [...CORE, 'Approvals', ...STOCK, ...READ_MASTER_DATA, 'Reports', 'Audit log'],
  supervisor: [...CORE, 'Approvals', ...STOCK, ...READ_MASTER_DATA, 'Reports'],
  staff: [...CORE, ...STOCK, ...READ_MASTER_DATA],
  clerk: [...CORE, ...STOCK, ...READ_MASTER_DATA]
};
/** A screen each role must not open (null: the Admin may open everything). */
const FORBIDDEN: Record<Role, string | null> = {
  admin: null,
  owner: '/users',
  supervisor: '/settings',
  staff: '/approvals',
  clerk: '/reports'
};

test.describe('AUTH-02 Role matrix smoke', () => {
  for (const role of Object.keys(MENU) as Role[]) {
    test(`given ${ROLE_LABEL[role]} signs in, then the menu shows exactly its items and a forbidden address shows the polite forbidden page`, async ({
      page,
      signIn,
      shell
    }) => {
      await signIn(role);
      expect(await shell.menu()).toEqual(MENU[role]);

      const forbidden = FORBIDDEN[role];
      if (forbidden === null) {
        await goTo(page, '/nonexistent-screen');
        await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible();
        return;
      }
      await goTo(page, forbidden);
      await expect(page).toHaveURL(/\/forbidden$/);
      await expect(page.getByText("You don't have access to this.").first()).toBeVisible();
      await expect(
        page.getByText(
          `Your role (${ROLE_LABEL[role]}) can't open this. Ask an Admin if you need it.`
        )
      ).toBeVisible();
      await page.getByRole('link', { name: 'Go to Home' }).click();
      await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    });
  }

  test('given a deep link typed into the address bar, when Warehouse staff opens /users, then the forbidden page shows', async ({
    page,
    signIn
  }) => {
    await signIn('staff');
    await loadPage(page, '/users');
    await expect(page).toHaveURL(/\/forbidden$/);
    await expect(page.getByText("You don't have access to this.").first()).toBeVisible();
  });

  test('given the Clerk role, when a clerk creates a partner twice, then it is saved once, the duplicate is explained, and no row can be edited', async ({
    page,
    signIn
  }) => {
    await signIn('clerk');
    const partners = new PartnersPage(page);
    await partners.open();
    await expect(page.getByRole('button', { name: 'Edit' })).toHaveCount(0);

    const name = `E2E Supplier ${unique()}`;
    const dialog = await partners.create('Supplier', name);
    await expect(dialog).toBeHidden();
    await expect(partners.row(name)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit' })).toHaveCount(0);

    // A second partner of the same name and type answers 409; the sentence is shown on the form and the dialog stays open.
    const again = await partners.create('Supplier', name);
    await expect(again.getByText(`A supplier named '${name}' already exists.`)).toBeVisible();
    await expect(again.getByRole('textbox', { name: /^Name/ })).toHaveAttribute(
      'aria-invalid',
      'true'
    );
  });

  test('given the Supervisor and Warehouse staff roles, then a supervisor can edit partners and staff cannot even create one', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    const partner = await api().createPartner('Customer');
    await signIn('staff');
    const partners = new PartnersPage(page);
    await partners.open();
    await expect(partners.row(partner.name)).toBeVisible();
    await expect(partners.newButton).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Edit' })).toHaveCount(0);
    await shell.signOut();

    await signIn('supervisor');
    await partners.open();
    await expect(partners.row(partner.name).getByRole('button', { name: 'Edit' })).toBeVisible();
    await expect(partners.newButton).toBeVisible();
  });
});
