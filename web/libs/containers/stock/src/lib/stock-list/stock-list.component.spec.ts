import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { PagedResultOfStockRow, StockRow, StockSort } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { CatalogStore } from '@ims/features/catalog';
import { ReportsStore } from '@ims/features/reports';
import { StockQuery, StockStore } from '@ims/features/stock';
import { WarehousesStore } from '@ims/features/warehousing';
import { PERMISSION } from '@ims/models';
import { StockListPageComponent } from './stock-list.component';

/** A stock row as the API sends it: avgCost and value are null without valuation.view. */
function row(withValue: boolean): StockRow {
  return {
    productId: 5,
    sku: 'DEMO-002',
    productName: 'Demo bolt',
    categoryName: undefined,
    warehouseId: 3,
    warehouseCode: 'WH03',
    warehouseName: 'Store B',
    qtyOnHand: 12,
    uomCode: 'PCS',
    safetyStock: 10,
    avgCost: withValue ? 2.5 : (null as unknown as undefined),
    value: withValue ? 30 : (null as unknown as undefined),
    isLow: false,
    isNegative: false
  } as StockRow;
}

describe('StockListPageComponent (valuation.view, docs/design/mvp-ux.md 1.4)', () => {
  let spectator: Spectator<StockListPageComponent>;
  const granted = new Set<string>();
  const items = signal<StockRow[]>([]);
  const load = jest.fn<void, [StockQuery]>();
  const createComponent = createComponentFactory({
    component: StockListPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      {
        provide: PermissionService,
        useValue: { has: (permission: string) => granted.has(permission) }
      },
      {
        provide: StockStore,
        useValue: {
          list: {
            $data: signal(undefined),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reload: jest.fn()
          }
        }
      },
      { provide: ReportsStore, useValue: { exportStockSummary: { $isRunning: signal(false) } } },
      { provide: CatalogStore, useValue: { ensureLoaded: jest.fn(), $categories: signal([]) } },
      {
        provide: WarehousesStore,
        useValue: { ensureLoaded: jest.fn(), $activeWarehouses: signal([]) }
      }
    ]
  });

  const headers = (): string[] =>
    spectator.queryAll('th').map(header => (header.textContent ?? '').trim());
  const bodyText = (): string =>
    (spectator.query('tbody')?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function render(withValue: boolean, ...permissions: string[]): void {
    granted.clear();
    permissions.forEach(permission => granted.add(permission));
    items.set([row(withValue)]);
    spectator = createComponent({
      providers: [
        {
          provide: StockStore,
          useValue: {
            list: {
              $data: signal({
                items: items(),
                page: 1,
                pageSize: 25,
                total: 1
              } as PagedResultOfStockRow),
              $isLoading: signal(false),
              $isFailed: signal(false),
              load,
              reload: jest.fn()
            }
          }
        }
      ]
    });
    spectator.detectChanges();
  }

  beforeEach(() => load.mockClear());

  it('Warehouse staff: no Value column, no value in the row, nothing to sort by value', () => {
    render(false, PERMISSION.StockRead, PERMISSION.DocumentCreate);

    expect(headers()).not.toContain('Value (RM)');
    expect(headers()).toContain('On hand');
    expect(bodyText()).toContain('12 PCS');
    expect(bodyText()).not.toMatch(/\d\.\d{2}/);
    expect(spectator.queryAll('tbody tr:first-child td')).toHaveLength(headers().length);
    expect(spectator.queryAll('th button').map(button => button.textContent?.trim())).not.toContain(
      'Value (RM)'
    );
  });

  it('Clerk: cost.view is not valuation.view, so still no Value column', () => {
    render(false, PERMISSION.StockRead, PERMISSION.DocumentCreate, PERMISSION.CostView);

    expect(headers()).not.toContain('Value (RM)');
  });

  it('Supervisor: the Value column shows the value and can be sorted', () => {
    render(
      true,
      PERMISSION.StockRead,
      PERMISSION.CostView,
      PERMISSION.ValuationView,
      PERMISSION.ReportsRead
    );

    expect(headers()).toContain('Value (RM)');
    expect(bodyText()).toContain('30.00');
    expect(spectator.queryAll('th button').map(button => button.textContent?.trim())).toContain(
      'Value (RM)'
    );

    spectator.component.onSortChange({ sortBy: StockSort.Value, desc: true });
    spectator.detectChanges();
    expect(load.mock.calls[load.mock.calls.length - 1][0].sortBy).toBe(StockSort.Value);
  });

  it('never asks the API for the value order without valuation.view (it answers 400)', () => {
    render(false, PERMISSION.StockRead);

    spectator.component.onSortChange({ sortBy: StockSort.Value, desc: false });
    spectator.detectChanges();

    expect(load.mock.calls[load.mock.calls.length - 1][0].sortBy).toBeUndefined();
  });

  it('the Excel export button is not offered to Staff and Clerk, who get 403 on it', () => {
    render(false, PERMISSION.StockRead, PERMISSION.DocumentCreate, PERMISSION.CostView);

    expect(spectator.query('app-page-header button')).toBeNull();
  });

  it('the Excel export button is offered to a role with reports.read', () => {
    render(
      true,
      PERMISSION.StockRead,
      PERMISSION.CostView,
      PERMISSION.ValuationView,
      PERMISSION.ReportsRead
    );
    expect(spectator.query('app-page-header button')?.textContent).toContain('Export');
  });
});
