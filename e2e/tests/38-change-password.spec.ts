import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { API_URL } from '../src/config';
import { unique, type ImsApi } from '../src/api';
import type { Role } from '../src/config';
import { expect, test } from '../src/fixtures';
import { LoginPage, Shell, goTo } from '../src/pages/shell';
import { spendLoginBudget } from '../src/rate-limit';

// The Change password screen (/account/password): entered from the Account dialog, two fields with an eye button each and no repeat
// field, the checks run before anything is sent, a wrong current password is said at its field, and a good change ends every
// session and opens sign-in with a notice. Every scenario that sends a change uses a throw-away user created through the API
// (its password is made up), so no shared QA or demo user ever has its password changed.
const WRONG_SENTENCE = 'The current password is not correct.';
const NOTICE = 'Your password was changed. Sign in again with your new password.';
const CHANGE_PATH = '/api/auth/change-password';

interface ThrowAway {
  username: string;
  password: string;
}

/** A password of 12 or more characters that no one else has. */
const freshPassword = (): string => `Nn1!${unique()}-pwd`;

test.describe('Change password', () => {
  const usernames: string[] = [];

  test.afterEach(async ({ api }) => {
    const admin = api('admin');
    // A user cannot be deleted: a throw-away user is turned off (it keeps a made-up password nobody knows).
    for (const username of usernames.splice(0)) {
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
          allWarehouses: false,
          warehouseIds: []
        });
      }
    }
  });

  async function newUser(api: (role?: Role) => ImsApi): Promise<ThrowAway> {
    const user = await api('admin').createUser();
    usernames.push(user.username);
    return user;
  }

  /** Signs the throw-away user in through the real form (it spends from the paced sign-in budget). */
  async function signInAs(page: Page, user: ThrowAway): Promise<void> {
    await spendLoginBudget(1);
    const login = new LoginPage(page);
    await login.open();
    await login.fill(user.username, user.password);
    await expect(new Shell(page).account).toBeVisible();
  }

  const currentBox = (page: Page) => page.getByRole('textbox', { name: /^Current password/ });
  const newBox = (page: Page) => page.getByRole('textbox', { name: /^New password/ });
  const submit = (page: Page) => page.getByRole('button', { name: 'Change password' });
  const messages = (page: Page) => page.locator('.ui-form-field-error');

  test('given a signed-in user, when they open Change password from the Account dialog and change it, then every session ends, sign-in shows the notice, the new password works and the old one does not', async ({
    page,
    shell,
    api
  }) => {
    const user = await newUser(api);
    const next = freshPassword();
    await signInAs(page, user);

    const dialog = await shell.openAccount();
    await dialog.getByRole('link', { name: 'Change password' }).click();
    await expect(page).toHaveURL(/\/account\/password$/);
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('heading', { level: 1, name: 'Change password' })).toBeVisible();
    await expect(page).toHaveTitle(/Change password/);
    // Two fields and no repeat field; the first one has the focus.
    await expect(page.getByRole('main').locator('input')).toHaveCount(2);
    await expect(currentBox(page)).toBeFocused();
    await expect(page.locator('input[autocomplete="current-password"]')).toHaveCount(1);
    await expect(page.locator('input[autocomplete="new-password"]')).toHaveCount(1);

    // The eye button of each field has one constant name and aria-pressed for its state.
    await currentBox(page).fill(user.password);
    await newBox(page).fill(next);
    const eyes = page.getByRole('button', { name: 'Show password' });
    await expect(eyes).toHaveCount(2);
    await eyes.nth(1).click();
    await expect(eyes.nth(1)).toHaveAttribute('aria-pressed', 'true');
    await expect(newBox(page)).toHaveAttribute('type', 'text');
    await expect(currentBox(page)).toHaveAttribute('type', 'password');

    await submit(page).click();

    await expect(page).toHaveURL(/\/login\?reason=password-changed$/);
    await expect(new LoginPage(page).notice).toHaveText(NOTICE);
    await expect(shell.account).toHaveCount(0);
    // Not in the address, not in web storage, not in a toast.
    expect(page.url()).not.toContain(next);
    const stored = await page.evaluate(() =>
      JSON.stringify([{ ...localStorage }, { ...sessionStorage }])
    );
    expect(stored).not.toContain(next);
    expect(stored).not.toContain(user.password);
    await expect(shell.toast(/./)).toHaveCount(0);

    // The new password signs in; the old one is refused by the API.
    await spendLoginBudget(1);
    const old = await fetch(`${API_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: user.username, password: user.password })
    });
    expect(old.status).toBe(401);
    await signInAs(page, { username: user.username, password: next });
  });

  test('given a wrong current password, when the user sends the change, then the API sentence is under Current password (described by it, focused), no banner or toast shows, nothing is lost and the user is still signed in', async ({
    page,
    shell,
    api
  }) => {
    const user = await newUser(api);
    const next = freshPassword();
    await signInAs(page, user);
    await goTo(page, '/account/password');
    await expect(currentBox(page)).toBeFocused();

    await currentBox(page).fill('not-my-password-1');
    await newBox(page).fill(next);
    await submit(page).click();

    await expect(messages(page)).toHaveCount(1);
    await expect(messages(page)).toHaveText(WRONG_SENTENCE);
    await expect(currentBox(page)).toHaveAttribute('aria-invalid', 'true');
    const id = await messages(page).getAttribute('id');
    await expect(currentBox(page)).toHaveAttribute('aria-describedby', new RegExp(`\\b${id}\\b`));
    await expect(currentBox(page)).toBeFocused();
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(shell.toast(/./)).toHaveCount(0);
    await expect(page).toHaveURL(/\/account\/password$/);
    await expect(shell.account).toBeVisible();
    await expect(newBox(page)).toHaveValue(next);

    // Correcting the field clears the sentence at once, and the right password goes through.
    await currentBox(page).fill(user.password);
    await expect(messages(page)).toHaveCount(0);
    await submit(page).click();
    await expect(page).toHaveURL(/\/login\?reason=password-changed$/);
  });

  test('given the checks, when the form is wrong, then each sentence is under its field, the focus goes to the first wrong field, and nothing is sent', async ({
    page,
    signIn
  }) => {
    let sent = 0;
    page.on('request', request => {
      if (request.url().endsWith(CHANGE_PATH)) {
        sent++;
      }
    });
    await signIn('clerk');
    await goTo(page, '/account/password');
    await expect(currentBox(page)).toBeFocused();

    // Nothing typed.
    await submit(page).click();
    await expect(messages(page)).toHaveText(['Enter your current password.', 'Enter a new password.']);
    await expect(currentBox(page)).toBeFocused();
    await expect(page.getByRole('alert')).toHaveCount(0);

    // A new password of 11 characters is too short; 12 is enough.
    await currentBox(page).fill('some-current-pw');
    await newBox(page).fill('x'.repeat(11));
    await submit(page).click();
    await expect(messages(page)).toHaveText(['Enter at least 12 characters.']);
    await expect(newBox(page)).toBeFocused();
    await expect(newBox(page)).toHaveAttribute('aria-invalid', 'true');
    await newBox(page).fill('x'.repeat(12));
    await expect(messages(page)).toHaveCount(0);

    // The same password as the current one is refused, and judged again when the current one changes.
    await newBox(page).fill('some-current-pw');
    await submit(page).click();
    await expect(messages(page)).toHaveText(['Choose a password different from the current one.']);
    await expect(newBox(page)).toBeFocused();
    await currentBox(page).fill('another-current-pw');
    await expect(messages(page)).toHaveCount(0);

    // The password boxes take at most 128 characters.
    await expect(newBox(page)).toHaveAttribute('maxlength', '128');
    expect(sent).toBe(0);
  });

  test('given the Users form, then its password hint points to Account, Change password', async ({
    page,
    signIn
  }) => {
    await signIn('admin');
    await goTo(page, '/users');
    await page.getByRole('button', { name: 'New user' }).click();
    const dialog = page.getByRole('dialog', { name: 'New user' });
    await expect(dialog).toContainText('Account > Change password');
  });

  test.describe('on a phone', () => {
    test.use({ viewport: { width: 360, height: 740 } });

    test('given a 360 px phone, then both buttons and both fields stay inside the window, and the errors do not scroll the page sideways', async ({
      page,
      signIn
    }) => {
      await signIn('clerk');
      await goTo(page, '/account/password');
      await expect(page.getByRole('heading', { level: 1, name: 'Change password' })).toBeVisible();
      await submit(page).click();
      await expect(messages(page)).toHaveCount(2);

      const sideways = await page.evaluate(
        () => document.documentElement.scrollWidth - window.innerWidth
      );
      expect(sideways).toBeLessThanOrEqual(0);
      for (const box of await Promise.all([
        currentBox(page).boundingBox(),
        newBox(page).boundingBox(),
        submit(page).boundingBox(),
        page.getByRole('button', { name: 'Cancel' }).boundingBox()
      ])) {
        expect(box).not.toBeNull();
        expect(box!.x).toBeGreaterThanOrEqual(0);
        expect(box!.x + box!.width).toBeLessThanOrEqual(360);
      }
      // 44 px touch targets, the eye buttons included.
      for (const eye of await page.getByRole('button', { name: 'Show password' }).all()) {
        const box = await eye.boundingBox();
        expect(box!.width).toBeGreaterThanOrEqual(44);
        expect(box!.height).toBeGreaterThanOrEqual(44);
      }
    });
  });

  test.describe('320 px with the text at 200%', () => {
    test.use({ viewport: { width: 320, height: 640 } });

    for (const [language, label] of [
      ['English', 'English'],
      ['Bahasa Melayu', 'Malay'],
      ['中文', 'Chinese']
    ] as const) {
      test(`given ${label}, then nothing is clipped and nothing scrolls sideways, errors showing`, async ({
        page,
        signIn,
        shell
      }) => {
        await signIn('clerk');
        if (language !== 'English') {
          await shell.setLanguage(language);
        }
        await goTo(page, '/account/password');
        await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
        await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
        await page.getByRole('main').locator('button[type="submit"]').click();
        await expect(messages(page)).toHaveCount(2);

        const sideways = await page.evaluate(
          () => document.documentElement.scrollWidth - window.innerWidth
        );
        expect(sideways).toBeLessThanOrEqual(0);
        const edges = await page
          .getByRole('main')
          .locator('input, button')
          .evaluateAll(items => items.map(item => item.getBoundingClientRect().right));
        expect(edges.length).toBeGreaterThanOrEqual(4);
        for (const right of edges) {
          expect(right).toBeLessThanOrEqual(320);
        }
        // The text of the two sentences is not cut by its box.
        const cut = await messages(page).evaluateAll(items =>
          items.filter(item => item.scrollWidth > item.clientWidth + 1).length
        );
        expect(cut).toBe(0);
        await page.evaluate(() => (document.documentElement.style.fontSize = ''));
        if (language !== 'English') {
          await shell.setLanguage('English');
        }
      });
    }
  });

  for (const scheme of ['light', 'dark'] as const) {
    test.describe(`axe, ${scheme}`, () => {
      test.use({ colorScheme: scheme, viewport: { width: 360, height: 740 } });

      test(`given the screen with its field messages showing, then axe finds no serious or critical violation (${scheme})`, async ({
        page,
        signIn
      }) => {
        await signIn('clerk');
        await goTo(page, '/account/password');
        await submit(page).click();
        await expect(messages(page)).toHaveCount(2);
        await page.waitForLoadState('networkidle');
        await page.waitForFunction(() => document.getAnimations().length === 0);
        const { violations } = await new AxeBuilder({ page })
          .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
          .analyze();
        const serious = violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
        expect(serious.map(v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(' | ')}`)).toEqual(
          []
        );
      });
    });
  }
});
