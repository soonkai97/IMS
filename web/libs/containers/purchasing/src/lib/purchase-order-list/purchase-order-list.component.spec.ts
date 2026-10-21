import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import {
  PagedResultOfPurchaseOrderListItem,
  PurchaseOrderListItem,
  PurchaseOrderStatus
} from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { PurchaseOrderQuery, PurchaseOrdersStore } from '@ims/features/purchasing';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import { PERMISSION } from '@ims/models';
import { PurchaseOrderListPageComponent } from './purchase-order-list.component';

const ITEM: PurchaseOrderListItem = {
  id: '00000000-0000-4000-8000-000000000001',
  no: 'PO-MAIN-2610-0001',
  status: PurchaseOrderStatus.Submitted,
  warehouseName: 'Main warehouse',
  supplierName: 'Acme Supplies',
  createdAt: '2026-10-09T01:00:00Z',
  createdBy: 'clerk',
  lineCount: 2,
  totalAmount: 1234.5,
  requiresApproval: true,
  isLarge: false,
  concurrencyToken: 'AAAA',
  canAct: true
};

describe('PurchaseOrderListPageComponent', () => {
  let spectator: Spectator<PurchaseOrderListPageComponent>;
  const granted = new Set<string>();
  const items = signal<PurchaseOrderListItem[]>([ITEM]);
  const load = jest.fn<void, [PurchaseOrderQuery]>();
  const reset = jest.fn();
  const createComponent = createComponentFactory({
    component: PurchaseOrderListPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      { provide: PermissionService, useValue: { has: (name: string) => granted.has(name) } },
      {
        provide: PurchaseOrdersStore,
        useValue: {
          list: {
            $data: signal(null as unknown as PagedResultOfPurchaseOrderListItem | undefined),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reset,
            reload: jest.fn()
          }
        }
      },
      { provide: PartnersStore, useValue: { $partners: signal([]), loadActive: jest.fn() } },
      {
        provide: WarehousesStore,
        useValue: { $activeWarehouses: signal([]), ensureLoaded: jest.fn() }
      }
    ]
  });

  async function render(): Promise<void> {
    spectator = createComponent();
    const store = spectator.inject(PurchaseOrdersStore) as unknown as {
      list: { $data: { set(value: unknown): void } };
    };
    store.list.$data.set({ items: items(), page: 1, pageSize: 25, total: items().length });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  const headers = (): string[] =>
    spectator.queryAll('th').map(header => (header.textContent ?? '').trim());
  const firstRow = (): string =>
    (spectator.query('tbody tr')?.textContent ?? '').replace(/\s+/g, ' ');
  const lastQuery = (): PurchaseOrderQuery => load.mock.calls[load.mock.calls.length - 1][0];

  beforeEach(() => {
    load.mockClear();
    reset.mockClear();
    granted.clear();
    items.set([ITEM]);
  });

  it('shows number, supplier, warehouse, status and the total of a person with cost.view', async () => {
    granted.add(PERMISSION.CostView);
    await render();

    expect(headers()).toEqual(
      expect.arrayContaining(['No.', 'Supplier', 'Warehouse', 'Amount (RM)', 'Status'])
    );
    expect(firstRow()).toContain('PO-MAIN-2610-0001');
    expect(firstRow()).toContain('Acme Supplies');
    expect(firstRow()).toContain('1,234.50');
  });

  it('shows no Amount column and no number to a person without cost.view (the API sends null)', async () => {
    items.set([{ ...ITEM, totalAmount: null as unknown as undefined }]);
    await render();

    expect(headers()).not.toContain('Amount (RM)');
    expect(firstRow()).not.toMatch(/\d\.\d{2}/);
    expect(spectator.queryAll('tbody tr:first-child td')).toHaveLength(headers().length);
  });

  it('marks the rows the server says the caller can act on, and only those', async () => {
    items.set([ITEM, { ...ITEM, id: '00000000-0000-4000-8000-000000000002', canAct: false }]);
    await render();

    const rows = spectator.queryAll('tbody tr');
    expect(rows[0].textContent).toContain('Waiting for you');
    expect(rows[1].textContent).not.toContain('Waiting for you');
  });

  it('forgets the orders of the last user before it loads, and has no row link until the detail page ships', async () => {
    await render();

    expect(reset.mock.invocationCallOrder[0]).toBeLessThan(load.mock.invocationCallOrder[0]);
    expect(spectator.query('tbody a')).toBeNull();
  });

  it('sends the chosen filters to the API, and Submitted is one of the statuses (approvers)', async () => {
    await render();
    const labels = spectator
      .queryAll('app-select')
      .map(select => select.closest('app-form-field')?.textContent ?? '');
    expect(labels.join(' ')).toContain('Status');

    spectator.component.statusControl.setValue(PurchaseOrderStatus.Submitted);
    spectator.component.supplierControl.setValue('s1');
    spectator.component.warehouseControl.setValue('w1');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(lastQuery()).toMatchObject({
      status: PurchaseOrderStatus.Submitted,
      supplierId: 's1',
      warehouseId: 'w1',
      page: 1
    });
    expect(spectator.component.$statusOptions().map(option => option.value)).toContain(
      PurchaseOrderStatus.Submitted
    );
  });

  it('sends no request for a From date after the To date and says so under To', async () => {
    await render();
    const before = load.mock.calls.length;

    spectator.component.fromControl.setValue('2026-10-20');
    spectator.component.toControl.setValue('2026-10-01');
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(load.mock.calls.length).toBe(before);
    expect(spectator.component.$toError()).toBe('From must be on or before To.');
  });

  it('tells an empty list from an empty result and offers to clear the filters', async () => {
    items.set([]);
    await render();
    expect(spectator.element.textContent).toContain('No purchase orders yet.');

    spectator.component.statusControl.setValue(PurchaseOrderStatus.Closed);
    spectator.detectChanges();

    expect(spectator.element.textContent).toContain('No purchase orders match these filters.');
    spectator.click(spectator.query('app-empty-state app-button button') as HTMLElement);
    expect(spectator.component.statusControl.value).toBeNull();
  });
});
