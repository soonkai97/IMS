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
        { path: 'stock', component: BlankPage }
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
      { provide: LoadingService, useValue: { $isLoading: signal(false) } },
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
