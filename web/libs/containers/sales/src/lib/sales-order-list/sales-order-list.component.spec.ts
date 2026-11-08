import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { PagedResultOfSalesOrderListItem, SalesOrderListItem, SalesOrderStatus } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { SalesOrderQuery, SalesOrdersStore } from '@ims/features/sales';
import { PartnersStore } from '@ims/features/warehousing';
import { PERMISSION } from '@ims/models';
import { SalesOrderListPageComponent } from './sales-order-list.component';

const ITEM: SalesOrderListItem = {
  id: '00000000-0000-4000-8000-000000000001',
  no: 'SO-MAIN-2611-0001',
  status: SalesOrderStatus.Submitted,
  warehouseName: 'Main warehouse',
  customerName: 'Acme Trading',
  createdAt: '2026-11-09T01:00:00Z',
  createdBy: 'clerk',
  lineCount: 2,
  totalAmount: 1234.5,
  requiresApproval: true,
  isLarge: false,
  concurrencyToken: 'AAAA',
  canAct: true
};
const OTHER: SalesOrderListItem = {
  ...ITEM,
  id: '00000000-0000-4000-8000-000000000002',
  no: 'SO-MAIN-2611-0002',
  canAct: false
};

describe('SalesOrderListPageComponent', () => {
  let spectator: Spectator<SalesOrderListPageComponent>;
  const granted = new Set<string>();
  const load = jest.fn<void, [SalesOrderQuery]>();
  const createComponent = createComponentFactory({
    component: SalesOrderListPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      { provide: PermissionService, useValue: { has: (name: string) => granted.has(name) } },
      {
        provide: SalesOrdersStore,
        useValue: {
          list: {
            $data: signal(undefined as PagedResultOfSalesOrderListItem | undefined),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reset: jest.fn(),
            reload: jest.fn()
          }
        }
      },
      { provide: PartnersStore, useValue: { $partners: signal([]), loadActive: jest.fn() } }
    ]
  });

  async function render(items: SalesOrderListItem[]): Promise<void> {
    spectator = createComponent();
    const store = spectator.inject(SalesOrdersStore) as unknown as {
      list: { $data: { set(value: unknown): void } };
    };
    store.list.$data.set({ items, page: 1, pageSize: 25, total: items.length });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  const headers = (): string[] =>
    spectator.queryAll('th').map(header => (header.textContent ?? '').trim());
  const rows = (): string[] =>
    spectator.queryAll('tbody tr').map(row => (row.textContent ?? '').replace(/\s+/g, ' '));
  const lastQuery = (): SalesOrderQuery => load.mock.calls[load.mock.calls.length - 1][0];

  beforeEach(() => {
    load.mockClear();
    granted.clear();
  });

  it('shows number, customer, warehouse, status and the total to a holder of so.manage', async () => {
    granted.add(PERMISSION.SoManage);
    await render([ITEM]);

    expect(headers()).toEqual(
      expect.arrayContaining(['No.', 'Customer', 'Warehouse', 'Amount (RM)', 'Status'])
    );
    expect(rows()[0]).toContain('SO-MAIN-2611-0001');
    expect(rows()[0]).toContain('Acme Trading');
    expect(rows()[0]).toContain('1,234.50');
  });

  it('shows no Amount column and no number with a decimal point to a holder of so.read only (the key is absent)', async () => {
    const { totalAmount: _total, ...withoutTotal } = ITEM;
    await render([withoutTotal as SalesOrderListItem]);

    expect(headers()).not.toContain('Amount (RM)');
    expect(rows()[0]).not.toMatch(/\d\.\d{2}/);
    expect(spectator.queryAll('tbody tr:first-child td')).toHaveLength(headers().length);
  });

  it('follows the money keys, not any permission: a holder of cost.view alone gets rows without them and so no column', async () => {
    granted.add(PERMISSION.CostView);
    const { totalAmount: _total, ...withoutTotal } = ITEM;
    await render([withoutTotal as SalesOrderListItem]);

    expect(headers()).not.toContain('Amount (RM)');
  });

  it('marks the rows the server says the caller can act on, and only those', async () => {
    await render([ITEM, OTHER]);

    expect(rows()[0]).toContain('Waiting for you');
    expect(rows()[1]).not.toContain('Waiting for you');
  });

  it('asks the server for the Submitted orders of "waiting for my approval" and keeps the rows the caller can act on', async () => {
    await render([ITEM, OTHER]);

    spectator.click(spectator.query('app-checkbox input') as HTMLElement);
    await spectator.fixture.whenStable();

    expect(lastQuery().status).toBe(SalesOrderStatus.Submitted);
    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('SO-MAIN-2611-0001');
  });

  describe('"waiting for my approval" keeps this page only', () => {
    const tick = async (): Promise<void> => {
      spectator.click(spectator.query('app-checkbox input') as HTMLElement);
      await spectator.fixture.whenStable();
    };
    const firstOfMany = (items: SalesOrderListItem[]): Promise<void> => {
      spectator = createComponent();
      const store = spectator.inject(SalesOrdersStore) as unknown as {
        list: { $data: { set(value: unknown): void } };
      };
      store.list.$data.set({ items, page: 1, pageSize: 25, total: 60 });
      spectator.detectChanges();
      return spectator.fixture.whenStable();
    };

    it('disables the status select, says "This page only" and hides the x-y of n total, while Next still works', async () => {
      await firstOfMany([ITEM, OTHER]);
      expect(spectator.query('.ui-pager-range')?.textContent).toContain('60');

      await tick();

      expect(spectator.query('app-select select, app-select [role="combobox"]')).toBeTruthy();
      expect(spectator.component.statusControl.disabled).toBe(true);
      expect(spectator.element.textContent).toContain('This page only');
      expect(spectator.query('.ui-pager-range')).toBeNull();
      expect(spectator.query('.ui-pager-nav')).not.toBeNull();
      spectator.component.onPageChange(2);
      spectator.detectChanges();
      await spectator.fixture.whenStable();
      expect(lastQuery().page).toBe(2);

      await tick();
      expect(spectator.component.statusControl.enabled).toBe(true);
    });

    it('an empty page with more pages behind it says so instead of "no orders match"', async () => {
      await firstOfMany([OTHER]);

      await tick();

      expect(rows()).toHaveLength(0);
      expect(spectator.element.textContent).toContain('There may be more on the next page.');
      expect(spectator.element.textContent).not.toContain('No sales orders match');
    });

    it('an empty last page says there are no matches', async () => {
      await render([OTHER]);

      await tick();

      expect(spectator.element.textContent).toContain('No sales orders match these filters.');
    });
  });

  it('opens the order page from a row', async () => {
    await render([ITEM]);

    expect(spectator.query('tbody a')?.getAttribute('href')).toBe(`/sales-orders/${ITEM.id}`);
  });

  it('says there are no orders yet, and that none match when filters are set', async () => {
    await render([]);
    expect(spectator.element.textContent).toContain('No sales orders yet.');

    spectator.click(spectator.query('app-checkbox input') as HTMLElement);
    await spectator.fixture.whenStable();
    expect(spectator.element.textContent).toContain('No sales orders match these filters.');
  });
});
