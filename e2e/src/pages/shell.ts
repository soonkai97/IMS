import { expect, type Locator, type Page } from '@playwright/test';
import { passwordFor, type Role } from '../config';
import { spendLoginBudget } from '../rate-limit';
import { takeSession } from '../session-cache';

/** The sign-in screen (S01). */
export class LoginPage {
  readonly username: Locator;
  readonly passwordBox: Locator;
  readonly submit: Locator;
  /** The red banner above the button (E7). */
  readonly error: Locator;
  /** The sentences under an empty field. They are not alerts, so they never count as the banner (D3). */
  readonly fieldMessages: Locator;
  /** "* means required" (D4). It is hidden from assistive technology, so it is found by its text. */
  readonly legend: Locator;
  readonly heading: Locator;
  readonly showPassword: Locator;
  readonly form: Locator;
  readonly language: Locator;
  readonly notice: Locator;

  constructor(private readonly page: Page) {
    // "required" is part of the accessible name (D4): the patterns stop at the label, so they hold with or without it.
    this.username = page.getByRole('textbox', { name: /^Email or username/ });
    this.passwordBox = page.getByRole('textbox', { name: /^Password/ });
    this.submit = page.getByRole('button', { name: 'Sign in' });
    this.error = page.getByRole('alert');
    // The error paragraph of a form field has no role of its own; its class is the stable hook.
    this.fieldMessages = page.locator('.ui-form-field-error');
    this.legend = page.getByText('* means required');
    this.heading = page.getByRole('heading', { level: 1 });
    this.showPassword = page.getByRole('button', { name: 'Show password' });
    this.form = page.locator('form.login-page-form');
    // Found by the component, not by its name: the name ("Language") is translated with the page.
    this.language = page.locator('app-language-select');
    this.notice = page.locator('app-banner[role="status"]');
  }

  async open(): Promise<void> {
    if (!/\/login(\?|$)/.test(this.page.url())) {
      await this.page.goto('/login');
    }
    await expect(this.username).toBeVisible();
  }

  async fill(username: string, password: string): Promise<void> {
    await this.username.fill(username);
    await this.passwordBox.fill(password);
    await this.submit.click();
  }
}

/** The chrome around every screen: menu, account dialog, toasts and confirm dialogs. */
export class Shell {
  readonly account: Locator;
  /** The visible main navigation (sidebar on desktop, tab bar on a phone). */
  readonly nav: Locator;

  constructor(private readonly page: Page) {
    // The only button in the top bar; found by position so it works in every language.
    this.account = page.getByRole('banner').getByRole('button');
    // Its accessible name is translated, so it is found by the shell's own classes (sidebar or tab bar, whichever shows).
    this.nav = page.locator('nav.shell-nav, nav.shell-tabbar').filter({ visible: true });
  }

  /** Menu labels as a user reads them (the unread-count badge is dropped). */
  async menu(): Promise<string[]> {
    const texts = await this.nav.getByRole('link').allInnerTexts();
    return texts.map(text => text.replace(/\s*\d+\s*$/, '').trim());
  }

  toast(text: string | RegExp): Locator {
    return this.page.locator('app-toast').filter({ hasText: text });
  }

  /** A modal dialog by its title. */
  dialog(title: string | RegExp): Locator {
    return this.page.getByRole('dialog', { name: title });
  }

  /** The account dialog (name, language, theme, sign out). */
  async openAccount(): Promise<Locator> {
    await this.account.click();
    const dialog = this.page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    return dialog;
  }

  async signOut(): Promise<void> {
    const dialog = await this.openAccount();
    await dialog.getByRole('button', { name: 'Sign out' }).click();
    await expect(this.page).toHaveURL(/\/login/);
  }

  /** Picks a language (English, 中文 or Bahasa Melayu) in the account dialog and closes the dialog. */
  async setLanguage(label: string): Promise<void> {
    const dialog = await this.openAccount();
    await dialog.getByRole('combobox').selectOption({ label });
    await this.page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
  }
}

/**
 * Opens a screen the way a user clicking a link does: inside the running app, without a page load. A full load of a
 * signed-in app calls POST /api/auth/refresh (its own limit of 300 a minute per address; sign-in is limited to 20).
 * Falls back to a normal navigation when the app is not running yet. Use page.goto only for the reload scenario.
 */
export async function goTo(page: Page, path: string): Promise<void> {
  const running = await page
    .evaluate(() => document.querySelector('app-root') !== null)
    .catch(() => false);
  if (!running) {
    await page.goto(path);
    return;
  }
  await page.evaluate(target => {
    window.history.pushState(null, '', target);
    window.dispatchEvent(new PopStateEvent('popstate', { state: null }));
  }, path);
}

/** A full page load (typed address, reload): the app refreshes its session (not a sign-in, so it spends no sign-in budget). */
export async function loadPage(page: Page, path: string): Promise<void> {
  await page.goto(path);
}

const RATE_LIMITED = /too many (attempts|requests)/i;

/**
 * Signs a role in without spending a sign-in when a session of that role was kept by an earlier scenario (session-cache.ts):
 * the refresh cookie is put in the browser context and the app restores the session on load. When none was kept, or the
 * API no longer knows it (a new database), it signs in through the form.
 */
export async function signIn(page: Page, role: Role): Promise<void> {
  const cookies = takeSession(role);
  if (cookies !== undefined) {
    await page.context().addCookies(cookies);
    await page.goto('/');
    const login = new LoginPage(page);
    const shell = new Shell(page);
    const outcome = await Promise.race([
      shell.account.waitFor().then(() => 'in' as const),
      login.username.waitFor().then(() => 'login' as const)
    ]);
    if (outcome === 'in') {
      await expect(page.getByRole('link', { name: 'Home' }).first()).toBeAttached();
      return;
    }
    await page.context().clearCookies();
  }
  await signInThroughForm(page, role);
}

/**
 * Signs in through the real login form. The API allows 20 sign-ins a minute per address, so a rate-limit answer waits and
 * tries again instead of failing a scenario that did nothing wrong.
 */
export async function signInThroughForm(page: Page, role: Role): Promise<void> {
  const login = new LoginPage(page);
  const shell = new Shell(page);
  for (let attempt = 1; ; attempt++) {
    await spendLoginBudget(1);
    await login.open();
    await login.fill(role, passwordFor(role));
    const outcome = await Promise.race([
      shell.account.waitFor().then(() => 'in' as const),
      login.error
        .first()
        .waitFor()
        .then(() => 'error' as const)
    ]);
    if (outcome === 'in') {
      await expect(page.getByRole('link', { name: 'Home' }).first()).toBeAttached();
      return;
    }
    const message = (await login.error.first().innerText()).trim();
    if (!RATE_LIMITED.test(message) || attempt >= 4) {
      throw new Error(`Sign-in as ${role} failed: ${message}`);
    }
    await page.waitForTimeout(15_000);
  }
}
