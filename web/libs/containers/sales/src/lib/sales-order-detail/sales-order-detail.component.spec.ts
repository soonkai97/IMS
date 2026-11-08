import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import {
  ApprovalMetric,
  ApproverTier,
  DocumentStatus,
  SalesOrderCloseKind,
  SalesOrderDto,
  SalesOrderStatus
} from '@ims/api';
import { Observable, of } from 'rxjs';
import { PermissionService } from '@ims/core/identity';
import { ConfirmService } from '@ims/core/notification';
import { SalesOrdersStore } from '@ims/features/sales';
import { ApiOperation, IDLE_OPERATION, PERMISSION } from '@ims/models';
import { SalesOrderDetailPageComponent } from './sales-order-detail.component';

const ID = '00000000-0000-4000-8000-000000000001';
const DELIVERY_ID = '00000000-0000-4000-8000-0000000000aa';

function command() {
  return {
    run: jest.fn((..._args: unknown[]): Observable<unknown> => of(order())),
    $isRunning: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    reset: jest.fn()
  };
}

/** An order as the API sends it to a holder of so.manage; without it the money keys are absent. */
function order(overrides: Partial<SalesOrderDto> = {}): SalesOrderDto {
  return {
    id: ID,
    no: 'SO-MAIN-2611-0001',
    status: SalesOrderStatus.Approved,
    warehouseId: 'w1',
    warehouseName: 'Main warehouse',
    customerId: 'c1',
    customerName: 'Acme Trading',
    remark: 'Deliver to the back door',
    createdById: 'u1',
    createdBy: 'clerk',
    createdByName: 'Clerk One',
    createdAt: '2026-11-09T01:00:00Z',
    requiresApproval: true,
    isLarge: false,
    isReturned: false,
    rejectionReason: undefined,
    cancelReason: undefined,
    submittedAt: '2026-11-09T02:00:00Z',
    approvedAt: '2026-11-09T03:00:00Z',
    returnedAt: undefined,
    closedAt: undefined,
    closeKind: undefined,
    closeReason: undefined,
    totalQty: 10,
    totalAmount: 5000,
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
        unitPrice: 500,
        amount: 5000,
        remark: undefined,
        deliveredQty: 6,
        remainingQty: 4,
        deliveredBaseQty: 6,
        remainingBaseQty: 4
      }
    ],
    concurrencyToken: 'AAAA',
    allowedActions: [],
    approvalTriggers: [],
    deliveries: [
      {
        id: DELIVERY_ID,
        no: 'ISS-MAIN-2611-0007',
        status: DocumentStatus.Posted,
        createdAt: '2026-11-10T01:00:00Z',
        postedAt: '2026-11-10T02:00:00Z'
      }
    ],
    ...overrides
  };
}

/** What a holder of so.read only is sent: no total, no price, no amount (the keys are absent, not null). */
function withoutMoney(source: SalesOrderDto): SalesOrderDto {
  const { totalAmount: _total, ...header } = source;
  return {
    ...header,
    lines: source.lines.map(({ unitPrice: _price, amount: _amount, ...line }) => line)
  } as SalesOrderDto;
}

const COMMANDS = ['submit', 'recall', 'approve', 'reject', 'cancel', 'close'] as const;

describe('SalesOrderDetailPageComponent', () => {
  let spectator: Spectator<SalesOrderDetailPageComponent>;
  const granted = new Set<string>();
  const detail = {
    $data: signal<SalesOrderDto | undefined>(undefined),
    $isLoading: signal(false),
    $isFailed: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    load: jest.fn(),
    reload: jest.fn(),
    reset: jest.fn()
  };
  const store = {
    detail,
    submit: command(),
    recall: command(),
    approve: command(),
    reject: command(),
    cancel: command(),
    close: command(),
    resetCommands: jest.fn(),
    resetFailed: jest.fn()
  };
  const createComponent = createComponentFactory({
    component: SalesOrderDetailPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      { provide: PermissionService, useValue: { has: (name: string) => granted.has(name) } },
      { provide: SalesOrdersStore, useValue: store }
    ]
  });

  async function render(so: SalesOrderDto): Promise<void> {
    detail.$data.set(so);
    spectator = createComponent({ props: { id: ID } as never });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  const buttons = (): string[] =>
    spectator
      .queryAll('.so-detail-bar button, .so-detail-bar a')
      .map(button => (button.textContent ?? '').replace(/\s+/g, ' ').trim());
  const button = (name: string): HTMLButtonElement =>
    spectator
      .queryAll<HTMLButtonElement>('.so-detail-bar button')
      .find(b => b.textContent?.includes(name))!;
  const text = (): string => (spectator.element.textContent ?? '').replace(/\s+/g, ' ');

  beforeEach(() => {
    granted.clear();
    detail.$data.set(undefined);
    for (const name of COMMANDS) {
      store[name].run.mockReset();
      store[name].run.mockReturnValue(of(order()));
    }
  });

  describe('the action bar is the server allowedActions and nothing else', () => {
    it.each([
      [[], []],
      [
        ['submit', 'cancel'],
        ['Cancel order', 'Submit']
      ],
      [
        ['recall', 'cancel'],
        ['Cancel order', 'Back to draft']
      ],
      [
        ['approve', 'reject'],
        ['Reject', 'Approve']
      ],
      [
        ['close', 'cancel'],
        ['Cancel order', 'Close short']
      ],
      [['teleport'], []]
    ])('%j shows %j', async (allowedActions, expected) => {
      await render(order({ allowedActions }));

      expect(buttons()).toEqual(expected);
    });

    it('does not render Edit or Deliver yet: their screens are PL3.9 and PL3.10, and a dead button is worse than none', async () => {
      await render(order({ allowedActions: ['edit', 'deliver', 'cancel'] }));

      expect(buttons()).toEqual(['Cancel order']);
    });

    it('does not look at what the user holds: a holder of every right sees nothing the server did not list', async () => {
      Object.values(PERMISSION).forEach(permission => granted.add(permission));

      await render(order({ allowedActions: ['cancel'] }));

      expect(buttons()).toEqual(['Cancel order']);
    });
  });

  describe('what is shown', () => {
    it('shows the header, the lines with ordered, delivered and remaining, and the linked delivery', async () => {
      await render(order());

      expect(text()).toContain('Acme Trading');
      expect(text()).toContain('Main warehouse');
      expect(text()).toContain('Deliver to the back door');
      const headers = spectator.queryAll('th').map(th => (th.textContent ?? '').trim());
      expect(headers).toEqual(
        expect.arrayContaining(['SKU', 'Ordered', 'Delivered', 'Remaining', 'Amount (RM)'])
      );
      const row = (spectator.query('tbody tr')?.textContent ?? '').replace(/\s+/g, ' ');
      expect(row).toContain('10 PCS');
      expect(row).toContain('6 PCS');
      expect(row).toContain('4 PCS');
      expect(text()).toContain('ISS-MAIN-2611-0007');
      expect(text()).toContain('5,000.00');
    });

    it('opens a delivery for a person who reads stock and only names it for anybody else', async () => {
      const link = `a[href="/issues/${DELIVERY_ID}"]`;
      await render(order());
      expect(spectator.query(link)).toBeNull();

      granted.add(PERMISSION.StockRead);
      await render(order());
      expect(spectator.query(link)).not.toBeNull();
    });

    it('shows no price, amount, total or number with a decimal point when the API leaves the money out (so.read only)', async () => {
      await render(withoutMoney(order()));

      const headers = spectator.queryAll('th').map(th => (th.textContent ?? '').trim());
      expect(headers).not.toContain('Amount (RM)');
      expect(headers).not.toContain('Unit price (RM)');
      expect(text()).not.toMatch(/\d\.\d{2}/);
      expect(text()).not.toContain('RM');
      expect(spectator.queryAll('dt').map(dt => dt.textContent)).not.toContain('Amount (RM)');
    });

    it('says in words why an approval rule held the order, and gives no number to a caller who has none', async () => {
      const trigger = {
        metric: ApprovalMetric.Amount,
        threshold: 5000,
        actual: 6000,
        approverTier: ApproverTier.Standard,
        roleRequired: 'Supervisor'
      };
      await render(order({ approvalTriggers: [trigger] }));
      expect(text()).toContain('The value 6,000.00 reaches the 5,000.00 limit.');

      await render(
        order({ approvalTriggers: [{ ...trigger, threshold: undefined, actual: undefined }] })
      );
      expect(text()).toContain('Its value reaches an approval limit.');
      expect(text()).not.toMatch(/\d,\d{3}\.\d{2}\s+limit/);
    });

    it('says how a closed order was closed, in the banner and the history', async () => {
      await render(
        order({
          status: SalesOrderStatus.Closed,
          closeKind: SalesOrderCloseKind.Short,
          closeReason: 'customer cancelled the rest',
          closedAt: '2026-11-12T01:00:00Z'
        })
      );
      expect(text()).toContain('Closed short');
      expect(text()).toContain('customer cancelled the rest');

      await render(
        order({
          status: SalesOrderStatus.Closed,
          closeKind: SalesOrderCloseKind.Delivered,
          closedAt: '2026-11-12T01:00:00Z'
        })
      );
      expect(text()).toContain('Fully delivered');
    });

    it('is the not-found page for an id that is not a GUID, with no request', async () => {
      detail.load.mockClear();
      detail.$data.set(undefined);
      spectator = createComponent({ props: { id: '31' } as never });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(text()).toContain("This page or document doesn't exist or was removed.");
      expect(detail.load).not.toHaveBeenCalled();
    });
  });

  describe('commands', () => {
    it('Submit runs at once with the version on screen', async () => {
      await render(order({ status: SalesOrderStatus.Draft, allowedActions: ['submit'] }));

      spectator.click(button('Submit'));

      expect(store.submit.run).toHaveBeenCalledWith({ id: ID, concurrencyToken: 'AAAA' });
    });

    it('Reject asks for a reason of 5 or more characters and sends it with the version', async () => {
      await render(
        order({ status: SalesOrderStatus.Submitted, allowedActions: ['approve', 'reject'] })
      );
      const confirm = TestBed.inject(ConfirmService);

      spectator.click(button('Reject'));

      expect(confirm.$pending()?.request).toMatchObject({
        reasonRequired: true,
        minReasonLength: 5,
        destructive: true
      });
      expect(store.reject.run).not.toHaveBeenCalled();

      confirm.confirm('price too low');
      expect(store.reject.run).toHaveBeenCalledWith({
        id: ID,
        concurrencyToken: 'AAAA',
        reason: 'price too low'
      });
    });

    it('Cancel of an Approved order needs a reason; of a Draft the reason is optional', async () => {
      const confirm = TestBed.inject(ConfirmService);
      await render(order({ allowedActions: ['cancel'] }));
      spectator.click(button('Cancel order'));
      expect(confirm.$pending()?.request).toMatchObject({
        reasonRequired: true,
        minReasonLength: 5
      });
      confirm.cancel();

      await render(order({ status: SalesOrderStatus.Draft, allowedActions: ['cancel'] }));
      spectator.click(button('Cancel order'));
      expect(confirm.$pending()?.request.reasonRequired).toBeUndefined();
      expect(confirm.$pending()?.request.reasonLabel).toBe('Reason (optional)');
      confirm.cancel();
    });

    it('Close short asks for a reason and sends nothing when the dialog is dismissed', async () => {
      const confirm = TestBed.inject(ConfirmService);
      await render(order({ allowedActions: ['close'] }));

      spectator.click(button('Close short'));
      expect(confirm.$pending()?.request).toMatchObject({
        reasonRequired: true,
        minReasonLength: 5
      });
      confirm.cancel();
      expect(store.close.run).not.toHaveBeenCalled();

      spectator.click(button('Close short'));
      confirm.confirm('customer cancelled the rest');
      expect(store.close.run).toHaveBeenCalledWith({
        id: ID,
        concurrencyToken: 'AAAA',
        reason: 'customer cancelled the rest'
      });
    });
  });
});
