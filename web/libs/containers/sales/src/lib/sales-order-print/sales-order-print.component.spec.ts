import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { SalesOrderDto, SalesOrderStatus } from '@ims/api';
import { SalesOrdersStore } from '@ims/features/sales';
import { ApiOperation, IDLE_OPERATION } from '@ims/models';
import { SalesOrderPrintPageComponent } from './sales-order-print.component';

const ID = '00000000-0000-4000-8000-000000000001';

/** An approved order as the API sends it: what it does not have is JSON null; the internal facts are all there to be left out. */
function order(over: Partial<SalesOrderDto> = {}): SalesOrderDto {
  return {
    id: ID,
    no: 'SO-MAIN-2611-0001',
    status: SalesOrderStatus.Approved,
    customerName: 'Acme Trading',
    warehouseName: 'Main warehouse',
    remark: 'Deliver to bay 3',
    createdAt: '2026-10-09T01:00:00Z',
    approvedAt: '2026-10-10T01:00:00Z',
    createdByName: 'Clerk One',
    approvedByName: 'Super Visor',
    cancelReason: null,
    rejectionReason: 'Price was too high',
    closeReason: null,
    totalAmount: 1234.5,
    lines: [
      {
        lineNo: 1,
        sku: 'SKU-1',
        productName: 'Widget',
        qty: 12,
        uomCode: 'CTN',
        unitPrice: 102.875,
        amount: 1234.5,
        remark: 'internal note'
      }
    ],
    ...over
  } as unknown as SalesOrderDto;
}

describe('SalesOrderPrintPageComponent', () => {
  let spectator: Spectator<SalesOrderPrintPageComponent>;
  const detail = {
    $data: signal<SalesOrderDto | undefined>(undefined),
    $isLoading: signal(false),
    $isFailed: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    load: jest.fn(),
    reload: jest.fn(),
    reset: jest.fn()
  };
  const createComponent = createComponentFactory({
    component: SalesOrderPrintPageComponent,
    detectChanges: false,
    providers: [provideRouter([]), { provide: SalesOrdersStore, useValue: { detail } }]
  });

  async function render(po: SalesOrderDto | undefined, id = ID): Promise<void> {
    detail.$data.set(po);
    spectator = createComponent({ props: { id } as never });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  const text = (): string => (spectator.element.textContent ?? '').replace(/\s+/g, ' ');

  beforeEach(() => {
    detail.load.mockClear();
    detail.reset.mockClear();
  });

  it('shows the number, the date as dd/MM/yyyy, customer, ship-from, the lines, the total and the remark', async () => {
    await render(order());

    expect(text()).toContain('Sales order SO-MAIN-2611-0001');
    // The date is the approval (a copy is sent after it), labelled; the creation date only when there is none.
    expect(text()).toContain('Date: 10/10/2026');
    expect(text()).toContain('Acme Trading');
    expect(text()).toContain('Main warehouse');
    const row = (spectator.query('tbody tr')?.textContent ?? '').replace(/\s+/g, ' ');
    expect(row).toContain('SKU-1');
    expect(row).toContain('Widget');
    expect(row).toContain('12');
    expect(row).toContain('CTN');
    expect(row).toContain('102.88');
    expect(row).toContain('1,234.50');
    expect(spectator.query('tfoot')?.textContent).toContain('1,234.50');
    expect(text()).toContain('Deliver to bay 3');
  });

  it('prints only an approved or closed order: a Draft and a Cancelled one are not a customer copy', async () => {
    for (const status of [SalesOrderStatus.Draft, SalesOrderStatus.Cancelled]) {
      await render(order({ status }));

      expect(text()).toContain('Only an approved or closed order can be printed.');
      expect(text()).not.toContain('Acme Trading');
      expect(spectator.query('.so-print-actions button')?.hasAttribute('disabled')).toBe(true);
    }
    await render(order({ status: SalesOrderStatus.Closed, approvedAt: null as never }));
    expect(text()).toContain('Date: 09/10/2026');
  });

  it('shows nothing internal: no people, no reasons, no history, no line remark', async () => {
    await render(order());

    for (const internal of [
      'Clerk One',
      'Super Visor',
      'Price was too high',
      'internal note',
      'History'
    ]) {
      expect(text()).not.toContain(internal);
    }
  });

  it('a price the API leaves out (so.read only) is an empty cell, never 0.00', async () => {
    const { totalAmount: _total, ...header } = order();
    await render({
      ...header,
      lines: [{ ...order().lines[0], unitPrice: undefined, amount: undefined }]
    } as unknown as SalesOrderDto);

    expect(text()).not.toMatch(/\d\.\d{2}/);
  });

  it('an id that is not a GUID is the not-found page and asks the API for nothing', async () => {
    await render(undefined, '31');

    expect(text()).toContain("This page or document doesn't exist or was removed.");
    expect(detail.load).not.toHaveBeenCalled();
  });

  it('marks the page as a print view while it is open (the app print stylesheet hides the navigation) and prints on request', async () => {
    await render(order());
    expect(document.body.classList.contains('so-print-view')).toBe(true);
    const print = jest.spyOn(window, 'print').mockImplementation(() => undefined);

    spectator.click(spectator.query('.so-print-actions button') as HTMLElement);

    expect(print).toHaveBeenCalledTimes(1);
    spectator.fixture.destroy();
    expect(document.body.classList.contains('so-print-view')).toBe(false);
  });
});
