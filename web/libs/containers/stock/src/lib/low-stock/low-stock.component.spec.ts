import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { LowStockAlert, PagedResultOfLowStockAlert } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { ReportsStore } from '@ims/features/reports';
import { StockStore } from '@ims/features/stock';
import { PERMISSION } from '@ims/models';
import { LowStockPageComponent } from './low-stock.component';

const ROW: LowStockAlert = {
  productId: 5,
  sku: 'DEMO-002',
  productName: 'Demo bolt',
  warehouseId: 3,
  warehouseCode: 'WH03',
  warehouseName: 'Store B',
  qtyOnHand: 2,
  safetyStock: 10,
  shortfall: 8
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

    expect(link?.getAttribute('href')).toBe('/receipts/new?sku=DEMO-002&warehouseId=3');
  });

  it('the shortcut is on the phone card too, not hidden with the desktop columns', () => {
    const cell = spectator.query('td[data-label="Actions"]');

    expect(cell?.classList).not.toContain('--phone-hidden');
    expect(cell?.classList).toContain('--phone-inline');
  });
});
