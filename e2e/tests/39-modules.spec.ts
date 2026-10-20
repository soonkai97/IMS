import AxeBuilder from '@axe-core/playwright';
import type { Page, Route } from '@playwright/test';
// Types only (erased at run time): the generated client of the web app, so a contract change fails `npm run typecheck`.
import type {
  ModuleDto,
  ModuleSwitchRequest,
  ModuleSwitchResult
} from '../../web/libs/shared/api/src/lib/api-client';
import { expect, test } from '../src/fixtures';
import { announcedBy } from '../src/feedback';
import { goTo, loadPage } from '../src/pages/shell';

// Settings > Modules (PL1.4): which parts of the system are on for the installation. This file never changes the state of the
// installation: the scenarios on the real API only read (the list, the forbidden pages, the layout), and the switch itself (reason
// dialog, result, errors, the stale-screen 404) is driven against a stubbed /api/modules that offers a second, made-up module.
// The real flip of `accounting` (on with a reason, forms and export, off again, the data put back) is `40-accounting.spec.ts`.
const ACCOUNTING = 'Accounting';
/** The web's own sentence under the reason box (the API says the same thing, so a stub must not reuse it for a 400). */
const REASON_SENTENCE = 'Enter a reason of at least 5 characters.';
/** A sentence only the API has (Messages.ModuleReasonRequired), so the 400 scenario proves the API's words reach the screen. */
const API_REASON_SENTENCE = 'A reason is required to turn a module on or off.';

/** The error body of the API (RFC 7807 ProblemDetails; the generated client has no type for it). */
interface Problem {
  title: string;
  status: number;
  detail: string;
}

/** The stubbed server: `batch` is made up, accounting stays off, and every request is counted. */
class ModulesStub {
  batch = false;
  regaining = 2;
  lists = 0;
  switches: { code: string; body: ModuleSwitchRequest }[] = [];
  /** When set, the next PUT answers this status instead of changing anything. */
  failNext: { status: number; detail: string } | undefined;

  rows(): ModuleDto[] {
    return [
      {
        code: 'accounting',
        name: ACCOUNTING,
        description: 'Tax-ready fields and the export file.',
        enabled: false,
        requires: []
      },
      {
        code: 'batch',
        name: 'Lots and expiry',
        description: 'Lot codes and expiry dates on stock.',
        enabled: this.batch,
        requires: []
      }
    ];
  }

  async install(page: Page): Promise<void> {
    await page.route(/\/api\/modules(\/[a-z]+)?$/, async (route: Route) => {
      const request = route.request();
      if (request.method() === 'GET') {
        this.lists++;
        await route.fulfill({ json: this.rows() });
        return;
      }
      if (request.method() !== 'PUT') {
        await route.fallback();
        return;
      }
      const code = new URL(request.url()).pathname.split('/').pop() ?? '';
      const body = request.postDataJSON() as ModuleSwitchRequest;
      this.switches.push({ code, body });
      if (this.failNext) {
        const { status, detail } = this.failNext;
        this.failNext = undefined;
        await route.fulfill({
          status,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Rejected', status, detail } satisfies Problem)
        });
        return;
      }
      const wanted = body.enabled === true;
      const turnedOn = wanted && !this.batch;
      this.batch = wanted;
      const row = this.rows().find(item => item.code === code) as ModuleDto;
      const result: ModuleSwitchResult = {
        ...row,
        rolesRegainingGrants: turnedOn ? this.regaining : 0
      };
      await route.fulfill({ json: result });
    });
  }
}

const moduleSwitch = (page: Page, name: string) => page.getByRole('switch', { name });

test.describe('Settings > Modules, real API (nothing is changed)', () => {
  test('given the Admin opens Modules from the menu, then the list matches the API and accounting is a switch like any other', async ({
    page,
    signIn,
    shell,
    api
  }) => {
    await signIn('admin');
    await shell.nav.getByRole('link', { name: 'Modules' }).click();

    await expect(page).toHaveURL(/\/settings\/modules$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Settings' }).first()).toBeVisible();

    const listed = await api('admin').call<ModuleDto[]>('GET', '/api/modules');
    expect(listed.map(item => item.code)).toContain('accounting');
    for (const item of listed) {
      await expect(page.getByRole('heading', { level: 2, name: item.name })).toBeVisible();
    }

    const accounting = moduleSwitch(page, ACCOUNTING);
    // The switch shows what the API says, in words, and nothing blocks it (the PL1.4 gate is gone; 40-accounting flips it).
    const state = listed.find(item => item.code === 'accounting')?.enabled === true;
    await expect(accounting).toHaveAttribute('aria-checked', String(state));
    await expect(accounting).toContainText(state ? 'On' : 'Off');
    await expect(accounting).not.toHaveAttribute('aria-disabled', 'true');
    await expect(page.getByText('Available soon')).toHaveCount(0);
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  for (const role of ['clerk', 'owner'] as const) {
    test(`given a ${role} signs in, then Modules is not in the menu and a typed /settings/modules shows the forbidden page`, async ({
      page,
      signIn,
      shell
    }) => {
      await signIn(role);
      expect(await shell.menu()).not.toContain('Modules');

      await loadPage(page, '/settings/modules');
      await expect(page).toHaveURL(/\/forbidden$/);
      await expect(page.getByText("You don't have access to this.").first()).toBeVisible();
    });
  }

  test('given the Admin opens Modules, then Settings is still the active menu item of /settings only', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('admin');
    await goTo(page, '/settings/modules');
    await expect(page.getByRole('heading', { level: 1, name: 'Modules' })).toBeVisible();

    await expect(shell.nav.getByRole('link', { name: 'Modules' })).toHaveAttribute(
      'aria-current',
      'page'
    );
    await expect(shell.nav.getByRole('link', { name: 'Settings' })).not.toHaveAttribute(
      'aria-current',
      'page'
    );
  });
});

test.describe('Settings > Modules, a stubbed /api/modules with a made-up second module', () => {
  test('given the Admin turns a module on and off, then each asks for a reason of 5+ characters, the result is announced once and the list reloads', async ({
    page,
    signIn
  }) => {
    const stub = new ModulesStub();
    await stub.install(page);
    await signIn('admin');
    await goTo(page, '/settings/modules');
    const lots = moduleSwitch(page, 'Lots and expiry');
    await expect(lots).toHaveAttribute('aria-checked', 'false');
    const listsBefore = stub.lists;

    await lots.click();
    const dialog = page.getByRole('dialog', { name: 'Turn on Lots and expiry?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Turn on' }).click();
    await expect(dialog.getByText(REASON_SENTENCE)).toBeVisible();
    await dialog.getByRole('textbox').fill('abcd');
    await dialog.getByRole('button', { name: 'Turn on' }).click();
    await expect(dialog.getByText(REASON_SENTENCE)).toBeVisible();
    expect(stub.switches).toHaveLength(0);

    await dialog.getByRole('textbox').fill('Needed for the lot pilot');
    await dialog.getByRole('button', { name: 'Turn on' }).click();

    await expect(dialog).toBeHidden();
    await expect(lots).toHaveAttribute('aria-checked', 'true');
    expect(stub.switches).toEqual([
      { code: 'batch', body: { enabled: true, reason: 'Needed for the lot pilot' } }
    ]);
    await expect(page.locator('app-toast')).toHaveCount(1);
    await expect(
      page
        .locator('app-toast')
        .filter({ hasText: 'Lots and expiry is now on. 2 roles regain their saved permissions.' })
    ).toHaveCount(1);
    expect(stub.lists).toBe(listsBefore + 1);
    // Focus returns to the switch that was used, and the new state is read out with it.
    await expect(lots).toBeFocused();

    await lots.click();
    await page.getByRole('dialog', { name: 'Turn off Lots and expiry?' }).getByRole('textbox').fill('Pilot over');
    await page.getByRole('dialog').getByRole('button', { name: 'Turn off' }).click();
    await expect(lots).toHaveAttribute('aria-checked', 'false');
    await expect(
      page.locator('app-toast').filter({ hasText: 'Lots and expiry is now off.' })
    ).toHaveCount(1);
    expect(stub.switches.at(-1)).toEqual({
      code: 'batch',
      body: { enabled: false, reason: 'Pilot over' }
    });
    await expect(lots).toBeFocused();
  });

  test('given the Admin cancels the reason dialog, then nothing is sent', async ({ page, signIn }) => {
    const stub = new ModulesStub();
    await stub.install(page);
    await signIn('admin');
    await goTo(page, '/settings/modules');

    await moduleSwitch(page, 'Lots and expiry').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();

    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(moduleSwitch(page, 'Lots and expiry')).toBeFocused();
    expect(stub.switches).toHaveLength(0);
  });

  for (const [status, detail, sentence] of [
    [409, 'Busy.', 'Another change to the modules is in progress.'],
    [403, 'Forbidden.', 'You are not allowed to turn modules on or off.'],
    [404, 'Module not found.', 'That module is not available any more. The list was refreshed.'],
    [400, API_REASON_SENTENCE, API_REASON_SENTENCE]
  ] as const) {
    test(`given the API answers ${status}, then one alert says it in words, no toast is raised and the switch keeps its state`, async ({
      page,
      signIn
    }) => {
      const stub = new ModulesStub();
      stub.failNext = { status, detail };
      await stub.install(page);
      await signIn('admin');
      await goTo(page, '/settings/modules');
      const lots = moduleSwitch(page, 'Lots and expiry');
      const listsBefore = stub.lists;

      await lots.click();
      await page.getByRole('dialog').getByRole('textbox').fill('A fine reason');
      await page.getByRole('dialog').getByRole('button', { name: 'Turn on' }).click();

      await expect(page.getByRole('alert').filter({ hasText: sentence })).toHaveCount(1);
      expect(await announcedBy(page, sentence)).toBe(1);
      await expect(page.locator('app-toast')).toHaveCount(0);
      await expect(lots).toHaveAttribute('aria-checked', 'false');
      if (status === 404 || status === 409) {
        await expect.poll(() => stub.lists).toBeGreaterThan(listsBefore);
      }
    });
  }

  test('given a screen that is out of date calls a feature that was turned off, then the 404 is explained in words, once, and the list is read again', async ({
    page,
    signIn
  }) => {
    const stub = new ModulesStub();
    await stub.install(page);
    // The session says accounting is on (what a stale screen believes); the server's list says it is off.
    await page.route('**/api/auth/refresh', async route => {
      const response = await route.fetch();
      const body = await response.json();
      body.user.modules = ['accounting'];
      await route.fulfill({ response, json: body });
    });
    await page.route(/\/api\/products(\?|$)/, route =>
      route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        // The body of an address that names no endpoint, which is also what the gate of a module that is off writes.
        body: JSON.stringify({
          title: 'Not Found',
          status: 404,
          detail: 'The requested resource was not found.'
        } satisfies Problem)
      })
    );
    await signIn('admin');
    const sentence = 'This feature was turned off: Accounting. The menu was updated.';
    const listsBefore = stub.lists;

    await goTo(page, '/products');

    await expect(page.locator('app-toast').filter({ hasText: sentence })).toHaveCount(1);
    expect(await announcedBy(page, sentence)).toBe(1);
    await expect(page.getByText('The requested resource was not found.')).toHaveCount(0);
    expect(stub.lists).toBeGreaterThan(listsBefore);
  });
});

test.describe('A 404 that a handler wrote is not a turned-off module', () => {
  test('given a record is not there, then its own sentence is shown and the module list is not read again', async ({
    page,
    signIn
  }) => {
    const stub = new ModulesStub();
    await stub.install(page);
    await page.route(/\/api\/products(\?|$)/, route =>
      route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          title: 'Not Found',
          status: 404,
          detail: 'Product not found.'
        } satisfies Problem)
      })
    );
    await signIn('admin');
    const listsBefore = stub.lists;

    await goTo(page, '/products');

    await expect(page.getByText('Product not found.').first()).toBeVisible();
    await expect(page.getByText(/turned off/)).toHaveCount(0);
    expect(stub.lists).toBe(listsBefore);
  });
});

test.describe('Settings > Modules, layout and accessibility', () => {
  test.describe('360 px', () => {
    test.use({ viewport: { width: 360, height: 740 } });

    test('given a phone, then the row has no sideways scroll and the switch is a 44px target', async ({
      page,
      signIn
    }) => {
      await signIn('admin');
      await goTo(page, '/settings/modules');
      const accounting = moduleSwitch(page, ACCOUNTING);
      await expect(accounting).toBeVisible();

      const sideways = await page.evaluate(
        () => document.documentElement.scrollWidth - window.innerWidth
      );
      expect(sideways).toBeLessThanOrEqual(0);
      const box = await accounting.boundingBox();
      expect(box!.height).toBeGreaterThanOrEqual(44);
      expect(box!.width).toBeGreaterThanOrEqual(44);
    });
  });

  test.describe('320 px with the text at 200%', () => {
    test.use({ viewport: { width: 320, height: 640 } });

    for (const [language, label] of [
      ['English', 'English'],
      ['Bahasa Melayu', 'Malay'],
      ['中文', 'Chinese']
    ] as const) {
      test(`given ${label}, then nothing is clipped and nothing scrolls sideways`, async ({
        page,
        signIn,
        shell
      }) => {
        await signIn('admin');
        if (language !== 'English') {
          await shell.setLanguage(language);
        }
        await goTo(page, '/settings/modules');
        await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
        await expect(page.getByRole('switch').first()).toBeVisible();
        await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));

        const sideways = await page.evaluate(
          () => document.documentElement.scrollWidth - window.innerWidth
        );
        expect(sideways).toBeLessThanOrEqual(0);
        const edges = await page
          .getByRole('main')
          .locator('button[role="switch"]')
          .evaluateAll(items => items.map(item => item.getBoundingClientRect().right));
        expect(edges.length).toBeGreaterThanOrEqual(1);
        for (const right of edges) {
          expect(right).toBeLessThanOrEqual(320);
        }
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

      test(`given the screen, then axe finds no serious or critical violation (${scheme})`, async ({
        page,
        signIn
      }) => {
        await signIn('admin');
        await goTo(page, '/settings/modules');
        await expect(page.getByRole('switch').first()).toBeVisible();
        await page.waitForLoadState('networkidle');
        await page.waitForFunction(() => document.getAnimations().length === 0);
        const { violations } = await new AxeBuilder({ page })
          .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
          .analyze();
        const serious = violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
        expect(
          serious.map(v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(' | ')}`)
        ).toEqual([]);
      });
    });
  }
});
