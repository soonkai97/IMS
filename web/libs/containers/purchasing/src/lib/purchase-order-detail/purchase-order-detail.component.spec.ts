import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import {
  DocumentDto,
  DocumentStatus,
  PurchaseOrderCloseKind,
  PurchaseOrderDto,
  PurchaseOrderStatus
} from '@ims/api';
import { Observable, Subject, of } from 'rxjs';
import { PermissionService } from '@ims/core/identity';
import { ConfirmService, ToastService } from '@ims/core/notification';
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

function command(result: () => unknown = () => order()) {
  return {
    run: jest.fn((..._args: unknown[]): Observable<unknown> => of(result())),
    $isRunning: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    reset: jest.fn()
  };
}

/** An order as the API sends it: what an order does not have is JSON null, not an absent member. */
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
    rejectionReason: null as never,
    cancelReason: null as never,
    submittedAt: '2026-10-09T02:00:00Z',
    approvedAt: '2026-10-09T03:00:00Z',
    returnedAt: null as never,
    closedAt: null as never,
    closeKind: null as never,
    closeReason: null as never,
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
        remark: null as never,
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

const COMMANDS = ['submit', 'recall', 'approve', 'reject', 'cancel', 'close', 'receive'] as const;
const RECEIPT_ID = '00000000-0000-4000-8000-0000000000bb';
const OTHER_ID = '00000000-0000-4000-8000-000000000002';

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
  const store = {
    detail,
    submit: command(),
    recall: command(),
    approve: command(),
    reject: command(),
    cancel: command(),
    close: command(),
    receive: command(() => ({ id: RECEIPT_ID }) as DocumentDto),
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

  const buttons = (): string[] =>
    spectator
      .queryAll('.po-detail-bar button, .po-detail-bar a')
      .map(button => (button.textContent ?? '').replace(/\s+/g, ' ').trim());
  const button = (name: string): HTMLButtonElement =>
    spectator
      .queryAll<HTMLButtonElement>('.po-detail-bar button')
      .find(b => b.textContent?.includes(name))!;
  const text = (): string => (spectator.element.textContent ?? '').replace(/\s+/g, ' ');

  beforeEach(() => {
    granted.clear();
    granted.add(PERMISSION.CostView);
    detail.$data.set(undefined);
    detail.$operation.set(IDLE_OPERATION);
    detail.$isFailed.set(false);
    detail.load.mockClear();
    detail.reload.mockClear();
    store.resetCommands.mockClear();
    store.resetFailed.mockClear();
    for (const name of COMMANDS) {
      store[name].run.mockReset();
      store[name].run.mockReturnValue(of(name === 'receive' ? { id: RECEIPT_ID } : order()));
      store[name].$operation.set(IDLE_OPERATION);
      store[name].$isRunning.set(false);
    }
  });

  describe('the action bar is the server allowedActions and nothing else', () => {
    it.each([
      [[], []],
      [['edit'], ['Edit']],
      [
        ['edit', 'submit', 'cancel'],
        ['Cancel order', 'Edit', 'Submit']
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
      [['receive', 'teleport'], ['Receive']],
      [
        ['cancel', 'close', 'receive'],
        ['Cancel order', 'Close short', 'Receive']
      ],
      [['teleport'], []]
    ])('%j shows %j', async (allowedActions, expected) => {
      await render(order({ allowedActions }));

      expect(buttons()).toEqual(expected);
    });

    it('Edit links to the editor, and only with cost.view too (the editor saves prices)', async () => {
      await render(order({ allowedActions: ['edit'] }));
      expect(spectator.query('.po-detail-bar a')?.getAttribute('href')).toBe(
        `/purchase-orders/${ID}/edit`
      );

      granted.clear();
      await render(order({ allowedActions: ['edit'] }));
      expect(buttons()).toEqual([]);
    });

    it('Print (the supplier copy, with prices) is offered with cost.view and only once the order is Approved or Closed', async () => {
      const printLink = (): Element | null => spectator.query('a[href$="/print"]');
      await render(order({ status: PurchaseOrderStatus.Approved }));
      expect(printLink()?.getAttribute('href')).toBe(`/purchase-orders/${ID}/print`);

      for (const status of [PurchaseOrderStatus.Draft, PurchaseOrderStatus.Cancelled]) {
        await render(order({ status }));
        expect(printLink()).toBeNull();
      }

      granted.clear();
      await render(order({ status: PurchaseOrderStatus.Approved }));
      expect(printLink()).toBeNull();
    });

    it('does not look at what the user holds: a holder of every right sees nothing the server did not list', async () => {
      Object.values(PERMISSION).forEach(permission => granted.add(permission));

      await render(order({ allowedActions: ['cancel'] }));

      expect(buttons()).toEqual(['Cancel order']);
    });
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
        totalAmount: null as never,
        lines: withheld.lines.map(line => ({
          ...line,
          unitPrice: null as never,
          amount: null as never
        }))
      });

      const headers = spectator.queryAll('th').map(th => (th.textContent ?? '').trim());
      expect(headers).not.toContain('Amount (RM)');
      expect(headers).not.toContain('Unit price (RM)');
      expect(text()).not.toMatch(/\d\.\d{2}/);
      expect(spectator.queryAll('dt').map(dt => dt.textContent)).not.toContain('Amount (RM)');
    });

    it('shows no banner at all on an open order (the API sends null for closeKind and the reasons)', async () => {
      for (const status of [
        PurchaseOrderStatus.Draft,
        PurchaseOrderStatus.Submitted,
        PurchaseOrderStatus.Approved
      ]) {
        await render(order({ status }));

        expect(spectator.queryAll('app-banner')).toHaveLength(0);
      }
    });

    it('a cancelled order without a reason still says Cancelled, in a banner and in the history', async () => {
      await render(order({ status: PurchaseOrderStatus.Cancelled }));

      expect(spectator.query('app-banner')?.textContent).toContain('Cancelled');
      expect(spectator.queryAll('ol li').map(li => li.textContent)).toEqual(
        expect.arrayContaining([expect.stringContaining('Cancelled')])
      );
      expect(text()).not.toContain('Reason:');
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

    it('offers no Retry for a refusal (403): trying again cannot help', async () => {
      detail.$data.set(undefined);
      detail.$isFailed.set(true);
      detail.$operation.set(failureOperation(apiError(API_ERROR_KIND.Forbidden, 403, 'No.')));
      spectator = createComponent({ props: { id: ID } as never });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(text()).toContain('No.');
      expect(spectator.query('app-banner app-button')).toBeNull();
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

  describe('commands', () => {
    it('Submit runs at once with the version on screen and says what happened', async () => {
      const toast = jest.spyOn(TestBed.inject(ToastService), 'success');
      store.submit.run.mockReturnValue(of(order({ status: PurchaseOrderStatus.Submitted })));
      await render(order({ status: PurchaseOrderStatus.Draft, allowedActions: ['submit'] }));

      spectator.click(button('Submit'));

      expect(store.submit.run).toHaveBeenCalledWith({ id: ID, concurrencyToken: 'AAAA' });
      expect(toast).toHaveBeenCalledWith('Submitted for approval.');
    });

    it('Reject asks for a reason of 5 or more characters and sends it with the version', async () => {
      await render(
        order({ status: PurchaseOrderStatus.Submitted, allowedActions: ['approve', 'reject'] })
      );
      const confirm = TestBed.inject(ConfirmService);

      spectator.click(button('Reject'));

      const request = confirm.$pending()?.request;
      expect(request).toMatchObject({
        reasonRequired: true,
        minReasonLength: 5,
        destructive: true
      });
      expect(store.reject.run).not.toHaveBeenCalled();

      confirm.confirm('price too high');
      expect(store.reject.run).toHaveBeenCalledWith({
        id: ID,
        concurrencyToken: 'AAAA',
        reason: 'price too high'
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

      await render(order({ status: PurchaseOrderStatus.Draft, allowedActions: ['cancel'] }));
      spectator.click(button('Cancel order'));
      expect(confirm.$pending()?.request.reasonRequired).toBeUndefined();
      expect(confirm.$pending()?.request.reasonLabel).toBe('Reason (optional)');
      confirm.cancel();
    });

    it('Close short asks for a reason and does nothing when the dialog is dismissed', async () => {
      const confirm = TestBed.inject(ConfirmService);
      await render(order({ allowedActions: ['close'] }));

      spectator.click(button('Close short'));
      expect(confirm.$pending()?.request).toMatchObject({
        reasonRequired: true,
        minReasonLength: 5
      });
      confirm.cancel();

      expect(store.close.run).not.toHaveBeenCalled();
    });

    it('Approve and Back to draft ask first', async () => {
      const confirm = TestBed.inject(ConfirmService);
      await render(
        order({ status: PurchaseOrderStatus.Submitted, allowedActions: ['approve', 'recall'] })
      );

      spectator.click(button('Approve'));
      expect(confirm.$pending()?.request.heading).toContain('PO-MAIN-2610-0001');
      confirm.confirm('');
      expect(store.approve.run).toHaveBeenCalledWith({ id: ID, concurrencyToken: 'AAAA' });

      spectator.click(button('Back to draft'));
      confirm.confirm('');
      expect(store.recall.run).toHaveBeenCalledWith({ id: ID, concurrencyToken: 'AAAA' });
    });

    it('a running command disables every button, marks the bar busy and shows the spinner on the tapped one', async () => {
      const running = new Subject<PurchaseOrderDto>();
      store.submit.run.mockReturnValue(running);
      await render(
        order({ status: PurchaseOrderStatus.Draft, allowedActions: ['submit', 'cancel'] })
      );

      spectator.click(button('Submit'));
      store.submit.$isRunning.set(true);
      spectator.detectChanges();

      expect(
        spectator
          .queryAll<HTMLButtonElement>('.po-detail-bar button')
          .every(b => b.disabled || b.getAttribute('aria-disabled') === 'true')
      ).toBe(true);
      expect(spectator.query('.po-detail-bar')?.getAttribute('aria-busy')).toBe('true');
      running.complete();
    });
  });

  describe('Receive starts a draft receipt from the order', () => {
    let navigate: jest.SpyInstance;
    beforeEach(() => {
      navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    });

    it('opens the new draft in the receipt editor, sending nothing but the order id', async () => {
      await render(order({ allowedActions: ['receive'] }));

      spectator.click(button('Receive'));

      expect(store.receive.run).toHaveBeenCalledWith(ID);
      expect(navigate).toHaveBeenCalledWith(['/', 'receipts', RECEIPT_ID]);
    });

    it('a 409 (the order is no longer open) is one alert with the API sentence, and the order is read again so the button goes', async () => {
      await render(order({ allowedActions: ['receive'] }));
      store.receive.run.mockReturnValue(of());
      store.receive.$operation.set(
        failureOperation(apiError(API_ERROR_KIND.Conflict, 409, 'PO-1 is Cancelled.', {}))
      );
      spectator.click(button('Receive'));
      spectator.detectChanges();

      expect(spectator.queryAll('app-banner[role="alert"]')).toHaveLength(1);
      expect(spectator.query('app-banner[role="alert"]')?.textContent).toContain(
        'PO-1 is Cancelled.'
      );
      expect(text()).not.toContain('changed since you opened it');
      expect(detail.reload).toHaveBeenCalled();
    });

    it('a running Receive disables the bar and shows the spinner on its own button', async () => {
      await render(order({ allowedActions: ['cancel', 'receive'] }));
      store.receive.$isRunning.set(true);
      spectator.click(button('Receive'));
      spectator.detectChanges();

      expect(button('Cancel order').disabled).toBe(true);
      expect(spectator.query('.po-detail-bar')?.getAttribute('aria-busy')).toBe('true');
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
            changedAt: '2026-10-21T01:00:00Z'
          })
        )
      );
      spectator.detectChanges();

      expect(text()).toContain('PO-MAIN-2610-0001 changed since you opened it');
      expect(text()).toContain('Sam changed this order');
      expect(button('Cancel order').disabled).toBe(true);

      spectator.click(spectator.query('app-banner app-button button') as HTMLElement);
      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reload).toHaveBeenCalled();
    });

    it('any other 409 (cancel refused while a receipt is open) shows the API sentence as an alert and does not claim the order changed', async () => {
      await render(order({ allowedActions: ['cancel'] }));
      store.cancel.$operation.set(
        failureOperation(
          apiError(API_ERROR_KIND.Conflict, 409, 'PO-1 has a receipt that is not finished.', {})
        )
      );
      spectator.detectChanges();

      expect(spectator.query('app-banner[role="alert"]')?.textContent).toContain(
        'has a receipt that is not finished.'
      );
      expect(text()).not.toContain('changed since you opened it');
      expect(button('Cancel order').disabled).toBe(false);
    });

    it('a plain 400 (wrong state) or a 403 reads the order again once, and forgets the failed command so a later command does not repeat it', async () => {
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
  });

  describe('what the last order or the last user left in the store', () => {
    const staleError = () =>
      failureOperation(apiError(API_ERROR_KIND.Conflict, 409, 'x', { code: 'document_changed' }));

    it('a stale banner and a refusal of order A do not appear on order B opened next, and its buttons work', async () => {
      await render(order({ allowedActions: ['cancel'] }));
      store.cancel.$operation.set(staleError());
      store.approve.$operation.set(
        failureOperation(apiError(API_ERROR_KIND.Conflict, 409, 'Nope for A.', {}))
      );
      spectator.detectChanges();
      expect(text()).toContain('changed since you opened it');

      detail.$data.set(order({ id: OTHER_ID, no: 'PO-B', allowedActions: ['cancel'] }));
      spectator.setInput('id', OTHER_ID);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(store.resetCommands).toHaveBeenCalled();
      expect(text()).toContain('PO-B');
      expect(text()).not.toContain('changed since you opened it');
      expect(text()).not.toContain('Nope for A.');
      expect(button('Cancel order').disabled).toBe(false);
    });

    it('what another user left behind (the store outlives a sign-out) is gone when the page opens', async () => {
      store.cancel.$operation.set(staleError());

      await render(order({ allowedActions: ['cancel'] }));

      expect(text()).not.toContain('changed since you opened it');
      expect(button('Cancel order').disabled).toBe(false);
    });
  });

  describe('the bar', () => {
    it('is a named group', async () => {
      await render(order({ allowedActions: ['cancel'] }));

      const bar = spectator.query('.po-detail-bar');
      expect(bar?.getAttribute('role')).toBe('group');
      expect(bar?.getAttribute('aria-label')).toBe('Actions for PO-MAIN-2610-0001');
    });
  });
});
