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
import {
  API_ERROR_KIND,
  ApiError,
  ApiOperation,
  IDLE_OPERATION,
  PERMISSION,
  failureOperation
} from '@ims/models';
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
const OTHER_ID = '00000000-0000-4000-8000-000000000002';

function apiError(
  kind: ApiError['kind'],
  status: number,
  message = 'x',
  extensions: Record<string, unknown> = {}
): ApiError {
  return {
    isApiError: true,
    kind,
    status,
    title: 't',
    detail: message,
    message,
    fieldErrors: {},
    extensions
  };
}

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
    // Like the real store: forgetting a command empties its outcome.
    resetCommands: jest.fn(() => {
      for (const name of COMMANDS) {
        store[name].$operation.set(IDLE_OPERATION);
      }
    }),
    resetFailed: jest.fn((refused: (error: ApiError) => boolean) => {
      for (const name of COMMANDS) {
        const error = store[name].$operation().error;
        if (error && refused(error)) {
          store[name].$operation.set(IDLE_OPERATION);
        }
      }
    })
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
    detail.$operation.set(IDLE_OPERATION);
    detail.$isFailed.set(false);
    detail.reload.mockClear();
    store.resetCommands.mockClear();
    for (const name of COMMANDS) {
      store[name].run.mockReset();
      store[name].run.mockReturnValue(of(order()));
      store[name].$operation.set(IDLE_OPERATION);
      store[name].$isRunning.set(false);
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

    it('renders Edit as a link to the editor, but not Deliver yet: its screen is PL3.10', async () => {
      await render(order({ allowedActions: ['edit', 'deliver', 'cancel'] }));

      expect(buttons()).toEqual(['Cancel order', 'Edit']);
      expect(spectator.query('.so-detail-bar a')?.getAttribute('href')).toBe(
        `/sales-orders/${ID}/edit`
      );
    });

    it('Print (the customer copy, with selling prices) is offered when the API sent prices and the order is Approved or Closed', async () => {
      const printLink = (): Element | null => spectator.query('a[href$="/print"]');
      await render(order({ status: SalesOrderStatus.Approved }));
      expect(printLink()?.getAttribute('href')).toBe(`/sales-orders/${ID}/print`);

      for (const status of [SalesOrderStatus.Draft, SalesOrderStatus.Cancelled]) {
        await render(order({ status }));
        expect(printLink()).toBeNull();
      }

      await render(withoutMoney(order({ status: SalesOrderStatus.Approved })));
      expect(printLink()).toBeNull();
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
      expect(spectator.queryAll('ol li').map(li => li.textContent)).toEqual(
        expect.arrayContaining([expect.stringContaining('Order closed')])
      );
    });

    it('does not repeat the Lines heading as the table caption, and a returned order without a name still has a heading', async () => {
      await render(order({ isReturned: true, rejectionReason: 'price', returnedBy: undefined }));

      expect(spectator.query('caption')?.textContent).toContain('Lines of SO-MAIN-2611-0001');
      expect(spectator.query('h2')?.textContent).not.toBe(spectator.query('caption')?.textContent);
      expect(spectator.query('app-banner')?.textContent).toContain('Returned');
    });

    it('sorts the history oldest first, an event without a time last', async () => {
      await render(order({ status: SalesOrderStatus.Cancelled, cancelReason: 'gone' }));

      const labels = spectator.queryAll('ol li strong').map(li => li.textContent);
      expect(labels[0]).toBe('Created');
      expect(labels[labels.length - 1]).toBe('Cancelled');
    });

    it('a failed load (403) is one alert banner with a way out, and the focus moves to it', async () => {
      detail.$isFailed.set(true);
      detail.$operation.set(failureOperation(apiError(API_ERROR_KIND.Forbidden, 403, 'No.')));
      detail.$data.set(undefined);
      spectator = createComponent({ props: { id: ID } as never });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(text()).toContain('No.');
      expect(spectator.query('app-banner[role="alert"] a')?.getAttribute('href')).toBe(
        '/sales-orders'
      );
      expect(spectator.query('app-banner app-button')).toBeNull();
      expect(document.activeElement?.tagName).toBe('APP-BANNER');
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

  describe('refusals', () => {
    it('a 409 that names document_changed locks the buttons behind a Reload banner, and Reload reads the order again', async () => {
      await render(order({ allowedActions: ['cancel'] }));
      store.cancel.$operation.set(
        failureOperation(
          apiError(API_ERROR_KIND.Conflict, 409, 'x', {
            code: 'document_changed',
            changedBy: 'Sam',
            changedAt: '2026-11-21T01:00:00Z'
          })
        )
      );
      spectator.detectChanges();

      expect(text()).toContain('SO-MAIN-2611-0001 changed since you opened it');
      expect(text()).toContain('Sam changed this order');
      expect(button('Cancel order').disabled).toBe(true);

      spectator.click(spectator.query('app-banner app-button button') as HTMLElement);
      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reload).toHaveBeenCalled();
    });

    it('any other 409 shows the API sentence as an alert and does not claim the order changed', async () => {
      await render(order({ allowedActions: ['cancel'] }));
      store.cancel.$operation.set(
        failureOperation(apiError(API_ERROR_KIND.Conflict, 409, 'It has an open delivery.', {}))
      );
      spectator.detectChanges();

      expect(spectator.query('app-banner[role="alert"]')?.textContent).toContain(
        'It has an open delivery.'
      );
      expect(text()).not.toContain('changed since you opened it');
      expect(button('Cancel order').disabled).toBe(false);
    });

    it('a plain 400 or a 403 reads the order again once and forgets the failed command', async () => {
      await render(order({ allowedActions: ['cancel'] }));
      store.cancel.$operation.set(failureOperation(apiError(API_ERROR_KIND.Validation, 400)));
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(detail.reload).toHaveBeenCalledTimes(1);
      expect(store.cancel.$operation().error).toBeUndefined();

      store.submit.$operation.set(failureOperation(apiError(API_ERROR_KIND.Forbidden, 403)));
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(detail.reload).toHaveBeenCalledTimes(2);
      expect(store.submit.$operation().error).toBeUndefined();
    });

    it('a stale banner of order A does not appear on order B opened next, and what a former user left is gone', async () => {
      await render(order({ allowedActions: ['cancel'] }));
      store.cancel.$operation.set(
        failureOperation(apiError(API_ERROR_KIND.Conflict, 409, 'x', { code: 'document_changed' }))
      );
      spectator.detectChanges();
      expect(text()).toContain('changed since you opened it');

      detail.$data.set(order({ id: OTHER_ID, no: 'SO-B', allowedActions: ['cancel'] }));
      spectator.setInput('id', OTHER_ID);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(store.resetCommands).toHaveBeenCalled();
      expect(text()).toContain('SO-B');
      expect(text()).not.toContain('changed since you opened it');
      expect(button('Cancel order').disabled).toBe(false);
    });
  });

  describe('commands', () => {
    it('a second tap while the dialog is open opens no second dialog and disables the bar', async () => {
      const confirm = TestBed.inject(ConfirmService);
      const ask = jest.spyOn(confirm, 'ask');
      await render(order({ allowedActions: ['cancel', 'close'] }));

      spectator.click(button('Cancel order'));
      spectator.click(button('Close short'));
      spectator.click(button('Cancel order'));
      spectator.detectChanges();

      expect(ask).toHaveBeenCalledTimes(1);
      expect(button('Close short').disabled).toBe(true);

      confirm.cancel();
      spectator.detectChanges();
      expect(button('Close short').disabled).toBe(false);
    });

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

      spectator.detectChanges();

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
