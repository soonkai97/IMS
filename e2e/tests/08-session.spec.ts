import { expect, test } from '../src/fixtures';
import { loadPage, signInThroughForm } from '../src/pages/shell';
import { spendLoginBudget } from '../src/rate-limit';

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

  test('given a failed sign-in, when either field is edited, then the banner goes away, and an empty resubmit shows only the two field messages (D3, NR-06)', async ({
    login,
    page
  }) => {
    // The four sign-in attempts count against the API's sign-in limit of 20 a minute; the page boot (a refresh) is counted too, to stay careful.
    await spendLoginBudget(5);
    let attempts = 0;
    page.on('request', request => {
      if (request.method() === 'POST' && request.url().endsWith('/api/auth/login')) {
        attempts++;
      }
    });
    await login.open();

    // A failed attempt with both fields filled shows the banner and nothing else.
    await login.fill('nobody-here', 'whatever-123');
    await expect(login.error).toHaveCount(1);
    await expect(login.fieldMessages).toHaveCount(0);

    // One character in the username removes it.
    await login.username.pressSequentially('x');
    await expect(login.error).toHaveCount(0);

    // Same for the password.
    await login.submit.click();
    await expect(login.error).toHaveCount(1);
    await login.passwordBox.pressSequentially('y');
    await expect(login.error).toHaveCount(0);

    // Emptying both fields and submitting: two field messages, no banner, and nothing is sent.
    await login.submit.click();
    await expect(login.error).toHaveCount(1);
    await login.username.fill('');
    await login.passwordBox.fill('');
    await expect(login.error).toHaveCount(0);
    const sent = attempts;
    await login.submit.click();
    await expect(login.fieldMessages).toHaveCount(2);
    await expect(login.error).toHaveCount(0);
    expect(attempts).toBe(sent);

    // A reload shows neither the banner nor the field messages.
    await login.fill('nobody-here', 'whatever-123');
    await expect(login.error).toHaveCount(1);
    await loadPage(page, '/login');
    await expect(login.username).toBeVisible();
    await expect(login.error).toHaveCount(0);
    await expect(login.fieldMessages).toHaveCount(0);
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
    shell,
    login
  }) => {
    await loadPage(page, '/receipts');
    await expect(page).toHaveURL(/\/login/);
    await expect(login.username).toBeVisible();

    // Through the form on purpose: a kept session would restore on the home page, not on the return URL.
    await signInThroughForm(page, 'clerk');
    await expect(page).toHaveURL(/\/receipts$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Receipts' })).toBeVisible();

    await shell.signOut();
    await loadPage(page, '/stock');
    await expect(page).toHaveURL(/\/login/);
    await expect(shell.account).toHaveCount(0);
  });
});
