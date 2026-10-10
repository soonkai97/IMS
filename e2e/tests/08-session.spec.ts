import { expect, test } from '../src/fixtures';
import { loadPage } from '../src/pages/shell';

test.describe('AUTH-01 Sign in, sign out and session restore (UX error E7, SEC-34)', () => {
  test('given a wrong password, when a user signs in, then one generic sentence shows and the form stays', async ({
    login,
    page,
    api
  }) => {
    const user = await api().createUser();
    await login.open();
    await login.fill(user.username, `${user.password}-wrong`);
    await expect(login.error).toContainText('Email, username or password is wrong.');
    await expect(page).toHaveURL(/\/login$/);

    await login.fill('nobody-here', 'whatever-123');
    await expect(login.error).toContainText('Email, username or password is wrong.');
  });

  test('given a signed-in user, when the page is reloaded, then the session is restored from the cookie and no token is kept in web storage', async ({
    page,
    context,
    signIn,
    shell
  }) => {
    await signIn('staff');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();

    await loadPage(page, '/receipts');
    await expect(page).toHaveURL(/\/receipts$/);
    await expect(shell.account).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: 'Receipts' })).toBeVisible();

    // The refresh token is an HttpOnly cookie the page cannot read; nothing token-like is in localStorage or sessionStorage.
    const cookies = await context.cookies();
    expect(cookies.some(cookie => cookie.httpOnly)).toBe(true);
    const visible = await page.evaluate(() => ({
      cookie: document.cookie,
      stored: [localStorage, sessionStorage].flatMap(storage =>
        Object.keys(storage).map(key => `${key}=${storage.getItem(key) ?? ''}`)
      )
    }));
    for (const httpOnly of cookies.filter(cookie => cookie.httpOnly)) {
      expect(visible.cookie).not.toContain(httpOnly.name);
      for (const entry of visible.stored) {
        expect(entry).not.toContain(httpOnly.value);
      }
    }
    expect(visible.stored.filter(entry => /token|jwt|eyJ/i.test(entry))).toEqual([]);
  });

  test('given a signed-out visitor opens a screen, when they sign in, then they land on that screen; after sign out a reload stays on the login page', async ({
    page,
    signIn,
    shell,
    login
  }) => {
    await loadPage(page, '/receipts');
    await expect(page).toHaveURL(/\/login/);
    await expect(login.username).toBeVisible();

    await signIn('clerk');
    await expect(page).toHaveURL(/\/receipts$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Receipts' })).toBeVisible();

    await shell.signOut();
    await loadPage(page, '/stock');
    await expect(page).toHaveURL(/\/login/);
    await expect(shell.account).toHaveCount(0);
  });
});
