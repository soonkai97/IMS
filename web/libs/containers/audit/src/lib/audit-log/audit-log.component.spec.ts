import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { AuditFilterOptions, PagedResultOfAuditLogDto } from '@ims/api';
import { AuditQuery, AuditStore } from '@ims/features/audit';
import { AuditLogPageComponent } from './audit-log.component';

describe('AuditLogPageComponent search (PS-06)', () => {
  let spectator: Spectator<AuditLogPageComponent>;
  const load = jest.fn<void, [AuditQuery]>();
  const filtersLoad = jest.fn();
  const page = signal<PagedResultOfAuditLogDto | undefined>({
    items: [],
    page: 1,
    pageSize: 25,
    total: 0
  } as PagedResultOfAuditLogDto);
  const createComponent = createComponentFactory({
    component: AuditLogPageComponent,
    detectChanges: false,
    providers: [
      {
        provide: AuditStore,
        useValue: {
          list: {
            $data: page,
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reload: jest.fn()
          },
          filters: {
            $data: signal({
              entityTypes: ['StockDocument'],
              actions: ['document.posted', 'login.success']
            } as AuditFilterOptions),
            load: filtersLoad
          }
        }
      }
    ]
  });

  const lastQuery = (): AuditQuery => load.mock.calls[load.mock.calls.length - 1][0];

  async function render(): Promise<void> {
    spectator = createComponent();
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  beforeEach(() => {
    load.mockClear();
    filtersLoad.mockClear();
  });

  it('asks the API for the names it can filter by, to offer a dropdown instead of guesses', async () => {
    await render();

    expect(filtersLoad).toHaveBeenCalled();
    const options = spectator.queryAll('select option').map(option => option.textContent?.trim());
    expect(options).toEqual(expect.arrayContaining(['document.posted', 'login.success']));
  });

  it('one search box takes a document number or an object name', async () => {
    await render();

    spectator.component.$searchInput.set('ISS-WH01-2610-0017');
    spectator.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 400));
    spectator.detectChanges();

    expect(lastQuery().search).toBe('ISS-WH01-2610-0017');
  });

  it('ER-10: each Show button names its entry (action, object, time), so 25 of them are told apart', async () => {
    const at = '2026-10-12T08:30:00Z';
    page.set({
      items: [
        {
          id: 1,
          at,
          actor: 'admin',
          action: 'product.updated',
          entityType: 'Product',
          entityId: '7'
        },
        {
          id: 2,
          at,
          actor: 'admin',
          action: 'product.updated',
          entityType: 'Product',
          entityId: '8'
        }
      ],
      page: 1,
      pageSize: 25,
      total: 2
    } as unknown as PagedResultOfAuditLogDto);
    await render();

    const names = spectator
      .queryAll('app-button button')
      .map(button => button.getAttribute('aria-label') ?? '')
      .filter(label => label.startsWith('Show details'));
    expect(names).toHaveLength(2);
    expect(new Set(names).size).toBe(2);
    expect(names[0]).toContain('product.updated on Product #7 at');
    page.set({ items: [], page: 1, pageSize: 25, total: 0 } as PagedResultOfAuditLogDto);
  });

  it('an empty result with filters set offers Clear filters', async () => {
    await render();
    spectator.component.$searchInput.set('nothing');
    spectator.detectChanges();
    await new Promise(resolve => setTimeout(resolve, 400));
    spectator.detectChanges();

    const clear = spectator.queryAll('button').find(b => b.textContent?.includes('Clear filters'));
    expect(clear).toBeTruthy();

    spectator.click(clear as HTMLElement);
    spectator.detectChanges();
    expect(spectator.component.$searchInput()).toBe('');
  });
});
