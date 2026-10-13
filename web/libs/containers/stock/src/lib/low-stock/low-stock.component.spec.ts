import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { LowStockAlert, PagedResultOfLowStockAlert } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { ReportsStore } from '@ims/features/reports';
import { StockStore } from '@ims/features/stock';
import { PERMISSION } from '@ims/models';
import { LowStockPageComponent } from './low-stock.component';

const WAREHOUSE_ID = '00000000-0000-4000-8000-000000000003';
const ROW: LowStockAlert = {
  productId: '00000000-0000-4000-8000-000000000005',
  sku: 'DEMO-002',
  productName: 'Demo bolt',
  warehouseId: WAREHOUSE_ID,
  warehouseCode: 'WH03',
  warehouseName: 'Store B',
  qtyOnHand: 2,
  safetyStock: 10,
  shortfall: 8,
  isNegative: false
};

describe('LowStockPageComponent Create receipt shortcut (NV-13)', () => {
  let spectator: Spectator<LowStockPageComponent>;
  const createComponent = createComponentFactory({
    component: LowStockPageComponent,
    providers: [
      provideRouter([]),
      {
        provide: PermissionService,
        useValue: { has: (permission: string) => permission === PERMISSION.DocumentCreate }
      },
      { provide: ReportsStore, useValue: { exportLowStock: { $isRunning: signal(false) } } },
      {
        provide: StockStore,
        useValue: {
          summary: { load: jest.fn() },
          lowStock: {
            $data: signal({
              items: [ROW],
              page: 1,
              pageSize: 25,
              total: 1
            } as PagedResultOfLowStockAlert),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load: jest.fn(),
            reload: jest.fn()
          }
        }
      }
    ]
  });

  beforeEach(() => {
    spectator = createComponent();
  });

  it('a row offers Create receipt with the product and the warehouse already chosen', () => {
    const link = spectator.query('a.low-stock-page-create');

    expect(link?.getAttribute('href')).toBe(
      `/receipts/new?sku=DEMO-002&warehouseId=${WAREHOUSE_ID}`
    );
  });

  it('a never-stocked product has no warehouse: the row is blank there and the shortcut sends only the SKU', () => {
    const rowData = (
      spectator.inject(StockStore) as unknown as {
        lowStock: { $data: ReturnType<typeof signal<PagedResultOfLowStockAlert>> };
      }
    ).lowStock.$data;
    rowData.set({
      items: [
        {
          ...ROW,
          warehouseId: undefined,
          warehouseCode: undefined,
          warehouseName: undefined,
          qtyOnHand: 0,
          shortfall: 10
        }
      ],
      page: 1,
      pageSize: 25,
      total: 1
    } as PagedResultOfLowStockAlert);
    spectator.detectChanges();

    expect(spectator.query('a.low-stock-page-create')?.getAttribute('href')).toBe(
      '/receipts/new?sku=DEMO-002'
    );
    expect(spectator.element.textContent).not.toContain('undefined');
  });

  it('ER-10: the link names its row (SKU and warehouse), so 25 of them are not 25 times "New receipt"', () => {
    const link = () => spectator.query('a.low-stock-page-create');
    const rowData = (
      spectator.inject(StockStore) as unknown as {
        lowStock: { $data: ReturnType<typeof signal<PagedResultOfLowStockAlert>> };
      }
    ).lowStock.$data;
    rowData.set({ items: [ROW], page: 1, pageSize: 25, total: 1 } as PagedResultOfLowStockAlert);
    spectator.detectChanges();
    expect(link()?.getAttribute('aria-label')).toBe('New receipt for DEMO-002 (Store B)');

    rowData.set({
      items: [
        { ...ROW, warehouseId: undefined, warehouseCode: undefined, warehouseName: undefined }
      ],
      page: 1,
      pageSize: 25,
      total: 1
    } as PagedResultOfLowStockAlert);
    spectator.detectChanges();

    expect(link()?.getAttribute('aria-label')).toBe('New receipt for DEMO-002');
  });

  it('A02: the link has no z-index of its own: in a table it would paint over the sticky header', () => {
    const styles = readFileSync(join(__dirname, 'low-stock.component.scss'), 'utf8');
    const rule = /\.low-stock-page-create\s*{[^}]*}/.exec(styles)?.[0] ?? '';

    expect(rule).toContain('position: relative');
    expect(rule).not.toContain('z-index');
  });

  it('PS-04: a negative balance carries its own chip', () => {
    expect(spectator.query('app-status-chip')).toBeNull();

    const rowData = (
      spectator.inject(StockStore) as unknown as {
        lowStock: { $data: ReturnType<typeof signal<PagedResultOfLowStockAlert>> };
      }
    ).lowStock.$data;
    rowData.set({
      items: [{ ...ROW, isNegative: true, qtyOnHand: -4 }],
      page: 1,
      pageSize: 25,
      total: 1
    } as PagedResultOfLowStockAlert);
    spectator.detectChanges();

    expect(spectator.query('app-status-chip')?.textContent).toContain('Negative');
  });

  it('carries no cost or value, and the Excel export is not offered to a role that gets 403 on it', () => {
    expect(spectator.query('app-page-header button')).toBeNull();
    expect(spectator.element.textContent).not.toMatch(/\d\.\d{2}/);
  });

  it('the shortcut is on the phone card too, not hidden with the desktop columns', () => {
    const cell = spectator.query('td[data-label="Actions"]');

    expect(cell?.classList).not.toContain('--phone-hidden');
    expect(cell?.classList).toContain('--phone-inline');
  });
});
