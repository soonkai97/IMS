import type { Locator, Page } from '@playwright/test';
import { passwordFor } from '../src/config';
import { expect, test } from '../src/fixtures';
import { announcedBy } from '../src/feedback';
import { LoginPage } from '../src/pages/shell';
import { spendLoginBudget } from '../src/rate-limit';

// R25 (docs/design/2026-refresh-login.md, lean). Failures are answered by a stub, so no case spends one of the API's real
// attempts (20 a minute per address, shared with token refresh); 08-session keeps the one real wrong password.

const LOGIN = /\/api\/auth\/login$/;
const WRONG = 'Email, username or password is wrong.';
const NOT_A_PASSWORD = 'not-a-real-password-1';

/** Answers POST /api/auth/login with a problem response after `delayMs`; returns how many calls it got. */
async function stubLogin(page: Page, status: number, body = {}, delayMs = 0): Promise<() => number> {
  let calls = 0;
  await page.route(LOGIN, async route => {
    if (route.request().method() !== 'POST') {
      return route.fallback();
    }
    calls++;
    await new Promise(resolve => setTimeout(resolve, delayMs));
    return route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Stub', status, detail: 'The API sentence, in English.', ...body })
    });
  });
  return () => calls;
}

async function openLogin(login: LoginPage): Promise<void> {
  await spendLoginBudget(1);
  await login.open();
}

async function typeCredentials(login: LoginPage): Promise<void> {
  await login.username.fill('staff');
  await login.passwordBox.fill(NOT_A_PASSWORD);
}

/** The controls found by what they are, not by their translated names (for the cases that change the language). */
function fieldsOf(page: Page): Record<'username' | 'password' | 'submit', Locator> {
  return {
    username: page.locator('input[autocomplete="username"]'),
    password: page.locator('input[autocomplete="current-password"]'),
    submit: page.locator('form button[type="submit"]')
  };
}

async function noSidewaysScroll(page: Page): Promise<void> {
  const { scroll, client } = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth
  }));
  expect(scroll).toBeLessThanOrEqual(client);
}

const animations = (page: Page): Promise<number> =>
  page.evaluate(() => document.getAnimations().length);

test.describe('R25 The sign-in page', () => {
  test('given the page is still entering, when the user types at once, then every key lands and Enter signs in', async ({
    page,
    login
  }) => {
    const calls = await stubLogin(page, 401);
    await spendLoginBudget(1);
    // The document clock runs at a tenth of its speed, so the 200 ms entrance lasts two seconds: long enough to type in it.
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Animation.enable');
    await cdp.send('Animation.setPlaybackRate', { playbackRate: 0.1 });
    await page.goto('/login');
    await expect(login.username).toBeFocused();
    expect(await animations(page)).toBeGreaterThan(0);

    await page.keyboard.type('typed-while-entering');
    await page.keyboard.press('Tab');
    await page.keyboard.type(NOT_A_PASSWORD);
    expect(await animations(page)).toBeGreaterThan(0);
    await page.keyboard.press('Enter');

    await expect(login.username).toHaveValue('typed-while-entering');
    await expect(login.error).toHaveCount(1);
    expect(calls()).toBe(1);
  });

  test('given reduced motion, then nothing animates and a failed sign-in is still shown by its message', async ({
    page,
    login
  }) => {
    await stubLogin(page, 401);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await openLogin(login);
    await expect.poll(() => animations(page)).toBe(0);

    await typeCredentials(login);
    await login.submit.click();

    await expect(login.error).toHaveText(WRONG);
    await expect.poll(() => animations(page)).toBe(0);
    // The shake is still declared, but it lasts a hundredth of a millisecond and moves nothing.
    const seconds = await login.form.evaluate(f => parseFloat(getComputedStyle(f).animationDuration));
    expect(seconds).toBeLessThan(0.001);
  });

  test('given a wrong password, when Enter is pressed, then the form shakes, one alert says so, nothing else announces it and the retry is ready', async ({
    page,
    login
  }) => {
    await stubLogin(page, 401);
    await openLogin(login);
    await typeCredentials(login);
    await login.passwordBox.press('Enter');

    await expect(login.error).toHaveCount(1);
    await expect(login.error).toHaveText(WRONG);
    await expect(login.form).toHaveClass(/--shake/);
    expect(await login.form.evaluate(f => getComputedStyle(f).animationName)).toContain('login-shake');
    expect(await announcedBy(page, WRONG)).toBe(1);
    await expect(page.locator('app-toast')).toHaveCount(0);
    await expect(page).toHaveURL(/\/login$/);
    await expect(login.username).toHaveValue('staff');
    // The focus did not move (that would cut the announcement short) and the password is selected: typing replaces it.
    await expect(login.passwordBox).toBeFocused();
    const selection = await login.passwordBox.evaluate((i: HTMLInputElement) => [i.selectionStart, i.selectionEnd]);
    expect(selection).toEqual([0, NOT_A_PASSWORD.length]);

    await page.keyboard.press('x');
    await expect(login.error).toHaveCount(0);
    await expect(login.form).not.toHaveClass(/--shake/);
  });

  test('given a lockout, a rate limit or an outage, then one alert says what to do and the button stays enabled', async ({
    page,
    login
  }) => {
    await openLogin(login);
    await typeCredentials(login);
    const cases: [number, object, string][] = [
      [401, { code: 'account_locked', retryAfterMinutes: 15 }, 'Too many failed attempts. Try again in 15 minutes.'],
      [401, { code: 'account_locked', retryAfterMinutes: 1 }, 'Too many failed attempts. Try again in 1 minute.'],
      [429, { retryAfterSeconds: 42 }, 'Too many sign-in attempts from this network. Try again in 42 seconds.'],
      [429, { retryAfterSeconds: 120 }, 'Too many sign-in attempts from this network. Try again in 2 minutes.'],
      [500, {}, 'Sign-in is not working right now. Try again in a moment.']
    ];
    for (const [status, body, sentence] of cases) {
      await page.unroute(LOGIN);
      await stubLogin(page, status, body);
      await login.submit.click();
      await expect(login.error).toHaveText(sentence);
      await expect(login.error).toHaveCount(1);
      expect(await announcedBy(page, sentence)).toBe(1);
      await expect(page.locator('app-toast')).toHaveCount(0);
      await expect(login.submit).toBeEnabled();
    }
  });

  test('given the browser goes offline, then a notice says so and a failed attempt shows the network sentence alone', async ({
    context,
    login
  }) => {
    await openLogin(login);
    await typeCredentials(login);

    await context.setOffline(true);
    await expect(login.notice).toHaveText(/You're offline\. Connect to the internet to sign in\./);
    await expect(login.submit).toBeEnabled();
    await login.submit.click();

    await expect(login.error).toHaveText("Can't reach the server. Check your connection and try again.");
    await expect(login.error).toHaveCount(1);
    await expect(login.notice).toHaveCount(0);
    await context.setOffline(false);
  });

  test('given the page, then password managers find the fields, storage stays clean, no credentials are printed and the eye button works', async ({
    page,
    login
  }) => {
    await stubLogin(page, 401);
    await openLogin(login);
    await typeCredentials(login);
    await login.submit.click();
    await expect(login.error).toHaveCount(1);

    const info = await page.evaluate(() => {
      const names = ['name', 'autocomplete', 'autocapitalize', 'autocorrect', 'spellcheck', 'type'];
      const attributes = (selector: string): (string | null)[] => {
        const input = document.querySelector(selector) as HTMLInputElement;
        return names.map(name => input.getAttribute(name));
      };
      return {
        username: attributes('input[autocomplete="username"]'),
        password: attributes('input[autocomplete="current-password"]'),
        forms: document.querySelectorAll('form').length,
        autocompleteOff: document.querySelectorAll('[autocomplete="off"]').length,
        local: Object.keys(localStorage),
        session: Object.keys(sessionStorage),
        text: document.body.innerText
      };
    });
    expect(info.username).toEqual(['username', 'username', 'none', 'off', 'false', 'text']);
    expect(info.password).toEqual(['password', 'current-password', 'none', 'off', 'false', 'password']);
    expect([info.forms, info.autocompleteOff, info.session]).toEqual([1, 0, []]);
    expect(info.local.filter(key => !['ims.language', 'ims.theme'].includes(key))).toEqual([]);
    // Compared as booleans, so a failure never prints a value.
    expect(info.text.includes(passwordFor('admin')) || info.text.includes(NOT_A_PASSWORD)).toBe(false);

    // The eye button keeps its name, shows the value, and leaves the focus in the field.
    await login.passwordBox.focus();
    await login.showPassword.click();
    await expect(login.passwordBox).toHaveAttribute('type', 'text');
    await expect(login.showPassword).toHaveAttribute('aria-pressed', 'true');
    await expect(login.showPassword).toHaveAccessibleName('Show password');
    await expect(login.passwordBox).toBeFocused();
    await expect(login.passwordBox).toHaveValue(NOT_A_PASSWORD);
  });

  test('given a desktop window, then the page is two regions with the right landmarks, and the panel is gone below 1024px', async ({
    page,
    login
  }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openLogin(login);

    await expect(page.getByRole('banner')).toHaveCount(1);
    await expect(page.getByRole('main')).toHaveCount(1);
    await expect(page.getByRole('contentinfo')).toHaveCount(0);
    await expect(page.getByRole('group', { name: 'Language' })).toHaveCount(1);
    await expect(login.heading).toHaveText('Sign in');
    const panel = page.getByRole('complementary', { name: 'About IMS' });
    await expect(panel.getByText('Know what is on every shelf')).toBeVisible();
    await expect(panel.getByRole('listitem')).toHaveCount(3);
    const [panelBox, formBox] = await Promise.all([panel.boundingBox(), login.form.boundingBox()]);
    expect(panelBox && formBox && panelBox.x + panelBox.width <= formBox.x).toBe(true);
    expect([panelBox?.x, panelBox?.y]).toEqual([0, 0]);
    expect(panelBox?.height).toBeGreaterThanOrEqual(800);
    await noSidewaysScroll(page);

    await page.setViewportSize({ width: 1023, height: 768 });
    await expect(page.getByRole('complementary')).toHaveCount(0);
    await noSidewaysScroll(page);
  });

  test('given a 360px phone, then every control is at least 44px, the form is in the first screen and nothing scrolls sideways', async ({
    page,
    login
  }) => {
    await page.setViewportSize({ width: 360, height: 640 });
    await openLogin(login);

    const controls = [login.username, login.passwordBox, login.showPassword, login.submit, ...(await login.language.locator('label').all())];
    for (const control of controls) {
      const box = await control.boundingBox();
      expect([box?.height, box?.width].every(size => (size ?? 0) >= 44)).toBe(true);
    }
    for (const visible of [login.heading, login.username, login.passwordBox, login.submit]) {
      const box = await visible.boundingBox();
      expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(640);
    }
    await expect(page.getByRole('complementary')).toHaveCount(0);
    await noSidewaysScroll(page);
  });

  test('given a 320px phone and a long failure, when the language changes, then the text is rebuilt, nothing typed is lost and the page does not scroll sideways', async ({
    page,
    login
  }) => {
    await page.setViewportSize({ width: 320, height: 568 });
    await stubLogin(page, 429, { retryAfterSeconds: 3600 });
    await openLogin(login);
    const fields = fieldsOf(page);
    await fields.username.fill('staff');
    await fields.password.fill(NOT_A_PASSWORD);
    await fields.submit.click();
    await expect(login.error).toHaveCount(1);

    const languages = [
      ['English', 'Sign in'],
      ['中文', '登录'],
      ['Bahasa Melayu', 'Log masuk']
    ] as const;
    for (const [language, heading] of languages) {
      await login.language.getByText(language).click();
      await expect(login.heading).toHaveText(heading);
      await expect(fields.username).toHaveValue('staff');
      await noSidewaysScroll(page);
      await fields.submit.scrollIntoViewIfNeeded();
      await expect(fields.submit).toBeInViewport();
    }
  });
});
