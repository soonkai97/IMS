import { signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { AuthService, PermissionService } from '@ims/core/identity';
import { LoadingService } from '@ims/core/http';
import { ThemeService } from '@ims/core/theme';
import { NavigationService } from '../services/navigation.service';
import { PageTitleService } from '../services/page-title.service';
import { NavEntry, NavGroup } from '../types/menu.types';
import { ShellComponent } from './shell.component';

@Component({ template: '' })
class BlankPage {}

function entry(id: string, route: string, group: NavEntry['group'], badge = 0): NavEntry {
  return {
    id,
    labelKey: id === 'home' ? 'nav.home' : 'nav.stock',
    icon: 'home',
    route,
    permissions: [],
    group,
    badge
  };
}

describe('ShellComponent structure and accessibility', () => {
  let spectator: Spectator<ShellComponent>;
  const home = entry('home', '/', 'main');
  const stock = entry('stock', '/stock', 'stock', 3);
  const loading = signal(false);
  const groups: NavGroup[] = [
    { group: { id: 'main', labelKey: 'nav.group.main' }, entries: [home] },
    { group: { id: 'stock', labelKey: 'nav.group.stock' }, entries: [stock] }
  ];
  const createComponent = createComponentFactory({
    component: ShellComponent,
    detectChanges: false,
    providers: [
      provideRouter([
        { path: '', component: BlankPage },
        { path: 'stock', component: BlankPage },
        { path: 'account/password', component: BlankPage }
      ]),
      {
        provide: NavigationService,
        useValue: {
          $groups: signal(groups),
          $phoneTabs: signal([home, stock]),
          $moreEntries: signal([stock])
        }
      },
      {
        provide: PermissionService,
        useValue: { $displayName: signal('Staff'), $role: signal('WarehouseStaff') }
      },
      { provide: AuthService, useValue: { logout: jest.fn() } },
      { provide: LoadingService, useValue: { $isLoading: loading } },
      { provide: ThemeService, useValue: { $mode: signal('system'), setMode: jest.fn() } }
    ]
  });

  async function render(url: string): Promise<void> {
    spectator = createComponent();
    await spectator.inject(Router).navigateByUrl(url);
    spectator.detectChanges();
  }

  it('EA-08: the link of the current page says so (aria-current)', async () => {
    await render('/stock');

    const current = spectator.queryAll('a[aria-current="page"]');
    expect(current.length).toBeGreaterThan(0);
    expect(current.every(link => link.getAttribute('href') === '/stock')).toBe(true);
  });

  it('EA-09: the menu groups are labels, not headings: the page h1 comes first', async () => {
    await render('/');

    expect(spectator.queryAll('h2')).toHaveLength(0);
    const labelled = spectator.queryAll('ul[aria-labelledby]');
    expect(labelled).toHaveLength(2);
    const label = spectator.query(`#${labelled[0].getAttribute('aria-labelledby')}`);
    expect(label?.textContent).toContain('Main');
  });

  it('EA-29: the account button announces that it opens a dialog', async () => {
    await render('/');

    expect(spectator.query('header button[aria-haspopup="dialog"]')).toBeTruthy();
  });

  it('ER-09: the account button name starts with "Account" and contains the visible user name', async () => {
    await render('/');

    expect(spectator.query('header button')?.getAttribute('aria-label')).toBe('Account, Staff');
  });

  it('the Account dialog has a Change password link to /account/password, and following it closes the dialog', async () => {
    await render('/');
    spectator.component.onOpenAccount();
    spectator.detectChanges();

    const link = spectator.query<HTMLAnchorElement>('a[href="/account/password"]');
    expect(link?.textContent?.trim()).toBe('Change password');

    link?.click();
    spectator.detectChanges();

    expect(spectator.component.$accountOpen()).toBe(false);
  });

  it('R10 landmarks: a skip link first, one banner, one main, and the tab bar is links, not a tablist', async () => {
    await render('/');

    const first = spectator.query('a');
    expect(first?.classList.contains('shell-skip-link')).toBe(true);
    expect(first?.getAttribute('href')).toBe('#main-content');
    expect(spectator.queryAll('header')).toHaveLength(1);
    expect(spectator.queryAll('main#main-content')).toHaveLength(1);
    const tabbar = spectator.query('nav.shell-tabbar');
    expect(tabbar?.getAttribute('aria-label')).toBeTruthy();
    expect(tabbar?.querySelector('[role="tab"], [role="tablist"]')).toBeNull();
    expect(tabbar?.querySelectorAll('a')).toHaveLength(2);
  });

  it('R10: the skip link moves focus to the content and does not navigate (base href would reload "/")', async () => {
    await render('/stock');
    const link = spectator.query<HTMLAnchorElement>('a.shell-skip-link');

    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    link?.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(spectator.query('main#main-content'));
    expect(spectator.inject(Router).url).toBe('/stock');
  });

  describe('top progress bar', () => {
    const bar = (): Element | null => spectator.query('.shell-progress');

    beforeEach(() => {
      jest.useFakeTimers();
      loading.set(false);
    });
    afterEach(() => jest.useRealTimers());

    /** Sets the pending-calls flag, then lets `ms` pass. */
    function wait(isLoading: boolean, ms: number): void {
      loading.set(isLoading);
      spectator.detectChanges();
      jest.advanceTimersByTime(ms);
      spectator.detectChanges();
    }

    it('does not show for a call that answers within 300ms (no flicker)', async () => {
      await render('/');

      wait(true, 250);
      wait(false, 500);

      expect(bar()).toBeNull();
    });

    it('shows after 300ms as an indeterminate, named progressbar and stays for at least 400ms', async () => {
      await render('/');

      wait(true, 300);

      expect(bar()?.getAttribute('role')).toBe('progressbar');
      expect(bar()?.getAttribute('aria-label')).toBe('Loading');
      expect(bar()?.hasAttribute('aria-valuenow')).toBe(false);

      wait(false, 300);
      expect(bar()).not.toBeNull();
      wait(false, 100);
      expect(bar()).toBeNull();
    });
  });

  it('EA-03: a route change is announced to screen readers through a live region', async () => {
    await render('/');
    const region = spectator.query('.shell-announcer');
    expect(region?.getAttribute('aria-live')).toBe('polite');

    const title = spectator.inject(PageTitleService);
    title.setScreen('stock.title', true);
    spectator.detectChanges();

    expect(region?.textContent).toContain('Stock - IMS');
  });
});
