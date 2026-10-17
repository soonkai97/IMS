import type { Locator, Page } from '@playwright/test';
import { passwordFor } from '../src/config';
import { expect, test } from '../src/fixtures';
import { announcedBy } from '../src/feedback';
import { LoginPage } from '../src/pages/shell';
import { spendLoginBudget } from '../src/rate-limit';

// R25 (docs/design/2026-refresh-login.md, lean). Failures are answered by a stub, so no case spends one of the API's real
// attempts (20 a minute per address; token refresh has its own limit); 08-session keeps the one real wrong password.

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

  for (const mode of ['prefers-reduced-motion', 'data-motion=off']) {
    test(`given ${mode}, then the entrance and the shake collapse and the message still shows the failure`, async ({
      page,
      login
    }) => {
      await stubLogin(page, 401);
      if (mode === 'data-motion=off') {
        await page.addInitScript(() => {
          // Runs before the page has a root element: mark it as soon as it exists.
          const observer = new MutationObserver(() => {
            document.documentElement.setAttribute('data-motion', 'off');
            observer.disconnect();
          });
          observer.observe(document, { childList: true });
        });
      } else {
        await page.emulateMedia({ reducedMotion: 'reduce' });
      }
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
  }

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

  test('given the browser goes offline, then a notice says so and a failed attempt shows the network sentence alone', async ({
    context,
    login
  }) => {
    await openLogin(login);
    await typeCredentials(login);

    await context.setOffline(true);
    await expect(login.notice).toHaveText(/You're offline\. Connect to the internet to sign in\./);
    await login.submit.click();

    await expect(login.error).toHaveText("Can't reach the server. Check your connection and try again.");
    await expect(login.notice).toHaveCount(0);
    await context.setOffline(false);
  });

  test('given the page, then password managers find the fields, storage stays clean, no credentials are printed', async ({
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
    expect([panelBox?.x, panelBox?.y, panelBox?.height]).toEqual([0, 0, 800]);
    await noSidewaysScroll(page);

    await page.setViewportSize({ width: 1023, height: 768 });
    await expect(page.getByRole('complementary')).toHaveCount(0);
    // The tagline moves under the lockup (600 to 1023px).
    await expect(page.locator('.login-page-strapline')).toBeVisible();
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
    const box = await login.submit.boundingBox();
    expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(640);
    await expect(page.locator('.login-page-strapline')).toBeHidden();
    expect((await login.submit.boundingBox())?.height).toBeGreaterThanOrEqual(56);
    await noSidewaysScroll(page);
  });

  test('given a long failure and the text size raised to 150% and 200%, in English, Chinese and Malay, then the page does not scroll sideways at 320 and 360px and no language name is split', async ({
    page,
    login
  }) => {
    await stubLogin(page, 429, { retryAfterSeconds: 3600 });
    await openLogin(login);
    const fields = fieldsOf(page);
    await fields.username.fill('staff');
    await fields.password.fill(NOT_A_PASSWORD);
    await fields.submit.click();
    await expect(login.error).toHaveCount(1);

    for (const language of ['English', '中文', 'Bahasa Melayu']) {
      await login.language.getByText(language).click();
      for (const width of [320, 360]) {
        for (const size of ['100%', '150%', '200%']) {
          await page.setViewportSize({ width, height: 700 });
          await page.evaluate(percent => (document.documentElement.style.fontSize = percent), size);
          await noSidewaysScroll(page);
          for (const name of await login.language.locator('label span').all()) {
            const lineHeight = await name.evaluate(n => parseFloat(getComputedStyle(n).lineHeight));
            expect((await name.boundingBox())?.height).toBeLessThan(lineHeight * 1.5);
          }
        }
      }
      await fields.submit.scrollIntoViewIfNeeded();
      await expect(fields.submit).toBeInViewport();
    }
  });
});
