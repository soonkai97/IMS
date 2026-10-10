import {
  test as base,
  expect,
  type Browser,
  type BrowserContext,
  type Page
} from '@playwright/test';
import { API_URL, WEB_URL, type Role } from './config';
import { apiAs, type ImsApi } from './api';
import { Shell, LoginPage, signInThroughForm } from './pages/shell';

/** Points the app at IMS_API_URL, whatever API address the build was made with (assets/config.json is runtime config). */
async function pointAtApi(context: BrowserContext): Promise<void> {
  await context.route(/\/assets\/config\.json/, route =>
    route.fulfill({ json: { apiBaseUrl: API_URL } })
  );
}

/** A signed-in browser session: its own context (own cookies and storage) so two users can act at the same time. */
export interface Session {
  context: BrowserContext;
  page: Page;
  shell: Shell;
}

export async function openSession(
  browser: Browser,
  role: Role,
  options: { viewport?: { width: number; height: number } } = {}
): Promise<Session> {
  const context = await browser.newContext({
    baseURL: WEB_URL,
    locale: 'en-US',
    timezoneId: 'Asia/Kuala_Lumpur',
    serviceWorkers: 'block',
    ...(options.viewport ? { viewport: options.viewport } : {})
  });
  await pointAtApi(context);
  const page = await context.newPage();
  await signInThroughForm(page, role);
  return { context, page, shell: new Shell(page) };
}

interface Fixtures {
  /** Signs the default page in as a role through the login form. */
  signIn: (role: Role) => Promise<void>;
  shell: Shell;
  login: LoginPage;
  /** Test data through the API, as a role (default admin). */
  api: (role?: Role) => ImsApi;
}

export const test = base.extend<Fixtures>({
  context: async ({ context }, use) => {
    await pointAtApi(context);
    await use(context);
  },
  signIn: async ({ page }, use) => {
    await use(role => signInThroughForm(page, role));
  },
  shell: async ({ page }, use) => use(new Shell(page)),
  login: async ({ page }, use) => use(new LoginPage(page)),
  api: async ({}, use) => use((role: Role = 'admin') => apiAs(role))
});

export { expect };
