import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import {
  DocumentStatus,
  PurchaseOrderCloseKind,
  PurchaseOrderDto,
  PurchaseOrderStatus
} from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { PurchaseOrdersStore } from '@ims/features/purchasing';
import {
  API_ERROR_KIND,
  ApiError,
  ApiOperation,
  IDLE_OPERATION,
  PERMISSION,
  failureOperation
} from '@ims/models';
import { PurchaseOrderDetailPageComponent } from './purchase-order-detail.component';

const ID = '00000000-0000-4000-8000-000000000001';

function order(overrides: Partial<PurchaseOrderDto> = {}): PurchaseOrderDto {
  return {
    id: ID,
    no: 'PO-MAIN-2610-0001',
    status: PurchaseOrderStatus.Approved,
    warehouseId: 'w1',
    warehouseName: 'Main warehouse',
    supplierId: 's1',
    supplierName: 'Acme Supplies',
    remark: 'Deliver to the back door',
    createdById: 'u1',
    createdBy: 'clerk',
    createdByName: 'Clerk One',
    createdAt: '2026-10-09T01:00:00Z',
    requiresApproval: true,
    isLarge: false,
    isReturned: false,
    rejectionReason: undefined,
    cancelReason: undefined,
    submittedAt: '2026-10-09T02:00:00Z',
    approvedAt: '2026-10-09T03:00:00Z',
    returnedAt: undefined,
    closedAt: undefined,
    closeKind: undefined,
    closeReason: undefined,
    totalQty: 10,
    totalAmount: 50,
    lines: [
      {
        lineNo: 1,
        productId: 'p1',
        sku: 'SKU-1',
        productName: 'Widget',
        qty: 10,
        uomId: 'u',
        uomCode: 'PCS',
        baseQty: 10,
        unitPrice: 5,
        amount: 50,
        remark: undefined,
        receivedQty: 6,
        remainingQty: 4,
        receivedBaseQty: 6,
        remainingBaseQty: 4
      }
    ],
    concurrencyToken: 'AAAA',
    allowedActions: [],
    approvalTriggers: [],
    receipts: [
      {
        id: '00000000-0000-4000-8000-0000000000aa',
        no: 'RCV-MAIN-2610-0007',
        status: DocumentStatus.Posted,
        createdAt: '2026-10-10T01:00:00Z',
        postedAt: '2026-10-10T02:00:00Z'
      }
    ],
    ...overrides
  };
}

function apiError(kind: ApiError['kind'], status: number, message: string): ApiError {
  return {
    isApiError: true,
    kind,
    status,
    title: 't',
    detail: message,
    message,
    fieldErrors: {},
    extensions: {}
  };
}

describe('PurchaseOrderDetailPageComponent', () => {
  let spectator: Spectator<PurchaseOrderDetailPageComponent>;
  const granted = new Set<string>();
  const detail = {
    $data: signal<PurchaseOrderDto | undefined>(undefined),
    $isLoading: signal(false),
    $isFailed: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    load: jest.fn(),
    reload: jest.fn(),
    reset: jest.fn()
  };
  const store = { detail };
  const createComponent = createComponentFactory({
    component: PurchaseOrderDetailPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      { provide: PermissionService, useValue: { has: (name: string) => granted.has(name) } },
      { provide: PurchaseOrdersStore, useValue: store }
    ]
  });

  async function render(po: PurchaseOrderDto, id = ID): Promise<void> {
    detail.$data.set(po);
    spectator = createComponent({ props: { id } as never });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  const text = (): string => (spectator.element.textContent ?? '').replace(/\s+/g, ' ');

  beforeEach(() => {
    granted.clear();
    granted.add(PERMISSION.CostView);
    detail.$data.set(undefined);
    detail.$operation.set(IDLE_OPERATION);
    detail.$isFailed.set(false);
    detail.load.mockClear();
    detail.reload.mockClear();
  });

  describe('what is shown', () => {
    it('shows the header, the lines with ordered, received and remaining, and the linked receipt', async () => {
      await render(order());

      expect(text()).toContain('Acme Supplies');
      expect(text()).toContain('Main warehouse');
      expect(text()).toContain('Deliver to the back door');
      const headers = spectator.queryAll('th').map(th => (th.textContent ?? '').trim());
      expect(headers).toEqual(
        expect.arrayContaining(['SKU', 'Ordered', 'Received', 'Remaining', 'Amount (RM)'])
      );
      const row = (spectator.query('tbody tr')?.textContent ?? '').replace(/\s+/g, ' ');
      expect(row).toContain('10 PCS');
      expect(row).toContain('6 PCS');
      expect(row).toContain('4 PCS');
      expect(text()).toContain('RCV-MAIN-2610-0007');
    });

    it('opens a receipt for a person who reads stock and only names it for anybody else', async () => {
      await render(order());
      expect(
        spectator.query('a[href="/receipts/00000000-0000-4000-8000-0000000000aa"]')
      ).toBeNull();

      granted.add(PERMISSION.StockRead);
      await render(order());
      expect(
        spectator.query('a[href="/receipts/00000000-0000-4000-8000-0000000000aa"]')
      ).not.toBeNull();
    });

    it('shows no price, amount or total, and no empty box for them, when the API withholds them (no cost.view)', async () => {
      granted.clear();
      const withheld = order();
      await render({
        ...withheld,
        totalAmount: undefined,
        lines: withheld.lines.map(line => ({ ...line, unitPrice: undefined, amount: undefined }))
      });

      const headers = spectator.queryAll('th').map(th => (th.textContent ?? '').trim());
      expect(headers).not.toContain('Amount (RM)');
      expect(headers).not.toContain('Unit price (RM)');
      expect(text()).not.toMatch(/\d\.\d{2}/);
      expect(spectator.queryAll('dt').map(dt => dt.textContent)).not.toContain('Amount (RM)');
    });

    it('says why a closed order is closed and a cancelled one is cancelled', async () => {
      await render(
        order({
          status: PurchaseOrderStatus.Closed,
          closeKind: PurchaseOrderCloseKind.Short,
          closeReason: 'supplier stopped making it',
          closedAt: '2026-10-12T01:00:00Z'
        })
      );
      expect(text()).toContain('Closed short');
      expect(text()).toContain('supplier stopped making it');

      await render(
        order({ status: PurchaseOrderStatus.Cancelled, cancelReason: 'wrong supplier' })
      );
      expect(text()).toContain('wrong supplier');
    });

    it('is the not-found page for an id that is not a GUID, with no request', async () => {
      detail.$data.set(undefined);
      spectator = createComponent({ props: { id: '31' } as never });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(text()).toContain("This page or document doesn't exist or was removed.");
      expect(detail.load).not.toHaveBeenCalled();
    });

    it('shows a banner with Retry when the load fails, and retries', async () => {
      detail.$data.set(undefined);
      detail.$isFailed.set(true);
      detail.$operation.set(failureOperation(apiError(API_ERROR_KIND.Server, 503, 'Down.')));
      spectator = createComponent({ props: { id: ID } as never });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(text()).toContain('Down.');
      spectator.click(spectator.query('app-banner app-button button') as HTMLElement);
      expect(detail.reload).toHaveBeenCalled();
    });
  });
});
