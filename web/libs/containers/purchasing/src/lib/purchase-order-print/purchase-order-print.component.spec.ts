import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { PurchaseOrderDto, PurchaseOrderStatus } from '@ims/api';
import { PurchaseOrdersStore } from '@ims/features/purchasing';
import { ApiOperation, IDLE_OPERATION } from '@ims/models';
import { PurchaseOrderPrintPageComponent } from './purchase-order-print.component';

const ID = '00000000-0000-4000-8000-000000000001';

/** An approved order as the API sends it: what it does not have is JSON null; the internal facts are all there to be left out. */
function order(over: Partial<PurchaseOrderDto> = {}): PurchaseOrderDto {
  return {
    id: ID,
    no: 'PO-MAIN-2610-0001',
    status: PurchaseOrderStatus.Approved,
    supplierName: 'Acme Supplies',
    warehouseName: 'Main warehouse',
    remark: 'Deliver to bay 3',
    createdAt: '2026-10-09T01:00:00Z',
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
  } as unknown as PurchaseOrderDto;
}

describe('PurchaseOrderPrintPageComponent', () => {
  let spectator: Spectator<PurchaseOrderPrintPageComponent>;
  const detail = {
    $data: signal<PurchaseOrderDto | undefined>(undefined),
    $isLoading: signal(false),
    $isFailed: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    load: jest.fn(),
    reload: jest.fn(),
    reset: jest.fn()
  };
  const createComponent = createComponentFactory({
    component: PurchaseOrderPrintPageComponent,
    detectChanges: false,
    providers: [provideRouter([]), { provide: PurchaseOrdersStore, useValue: { detail } }]
  });

  async function render(po: PurchaseOrderDto | undefined, id = ID): Promise<void> {
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

  it('shows the number, the date as dd/MM/yyyy, supplier, ship-to, the lines, the total and the remark', async () => {
    await render(order());

    expect(text()).toContain('Purchase order PO-MAIN-2610-0001');
    expect(text()).toContain('09/10/2026');
    expect(text()).toContain('Acme Supplies');
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

  it('a price the API withholds (null) is an empty cell, never 0.00', async () => {
    await render(
      order({
        totalAmount: null as never,
        lines: [{ ...order().lines[0], unitPrice: null as never, amount: null as never }]
      })
    );

    expect(text()).not.toMatch(/\d\.\d{2}/);
  });

  it('an id that is not a GUID is the not-found page and asks the API for nothing', async () => {
    await render(undefined, '31');

    expect(text()).toContain("This page or document doesn't exist or was removed.");
    expect(detail.load).not.toHaveBeenCalled();
  });

  it('marks the page as a print view while it is open (the app print stylesheet hides the navigation) and prints on request', async () => {
    await render(order());
    expect(document.body.classList.contains('po-print-view')).toBe(true);
    const print = jest.spyOn(window, 'print').mockImplementation(() => undefined);

    spectator.click(spectator.query('.po-print-actions button') as HTMLElement);

    expect(print).toHaveBeenCalledTimes(1);
    spectator.fixture.destroy();
    expect(document.body.classList.contains('po-print-view')).toBe(false);
  });
});
