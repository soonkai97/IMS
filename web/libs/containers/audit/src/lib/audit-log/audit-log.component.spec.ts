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

  it('ER-10: each Show button names its entry (action, object, time, place in the list), so 25 of them are told apart, even two that read the same', async () => {
    const at = '2026-10-12T08:30:00Z';
    page.set({
      items: [
        {
          at,
          actor: 'admin',
          action: 'product.updated',
          entityType: 'Product',
          entityId: '7'
        },
        {
          at,
          actor: 'admin',
          action: 'product.updated',
          entityType: 'Product',
          entityId: '8'
        },
        {
          at,
          actor: 'admin',
          action: 'product.updated',
          entityType: 'Product',
          entityId: '8'
        }
      ],
      page: 1,
      pageSize: 25,
      total: 3
    } as unknown as PagedResultOfAuditLogDto);
    await render();

    const names = spectator
      .queryAll('app-button button')
      .map(button => button.getAttribute('aria-label') ?? '')
      .filter(label => label.startsWith('Show details'));
    expect(names).toHaveLength(3);
    expect(new Set(names).size).toBe(3);
    expect(names[0]).toContain('product.updated on Product #7 at');
    expect(names[0]).toContain('entry 1');
    expect(names[2]).toContain('entry 3');
    page.set({ items: [], page: 1, pageSize: 25, total: 0 } as PagedResultOfAuditLogDto);
  });

  it('R23: From after To says so under To and sends no request; fixing the range loads again', async () => {
    await render();
    load.mockClear();

    spectator.component.fromControl.setValue('2026-10-12');
    spectator.component.toControl.setValue('2026-10-10');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    const error = spectator.query(
      '.audit-log-page-filters app-form-field:last-child .ui-form-field-error'
    );
    expect(error?.textContent?.trim()).toBe('From must be on or before To.');
    expect(load).not.toHaveBeenCalled();
    // The empty area says the same, not that no entry matches (no search ran).
    expect(spectator.query('app-empty-state')?.textContent).toContain(
      'From must be on or before To.'
    );
    expect(spectator.query('app-empty-state')?.textContent).not.toContain('No activity');

    spectator.component.toControl.setValue('2026-10-12');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(spectator.query('.audit-log-page-filters .ui-form-field-error')).toBeNull();
    expect(lastQuery().from).toBe('2026-10-12');
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
