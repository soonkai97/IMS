import { signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { PagedResultOfReorderSuggestion, ReorderSuggestion } from '@ims/api';
import { AuthService, PermissionService } from '@ims/core/identity';
import { MAX_PREFILL_LINES, ReorderQuery, ReorderStore } from '@ims/features/purchasing';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import { PERMISSION } from '@ims/models';
import { ReorderPageComponent } from './reorder.component';

const WAREHOUSE = '00000000-0000-4000-8000-0000000000aa';
const SUPPLIER = '00000000-0000-4000-8000-0000000000bb';

const row = (index: number, patch: Partial<ReorderSuggestion> = {}): ReorderSuggestion => ({
  productId: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
  sku: `SKU-${index}`,
  productName: `Product ${index}`,
  uomCode: 'PCS',
  warehouseId: WAREHOUSE,
  warehouseCode: 'MAIN',
  warehouseName: 'Main warehouse',
  qtyOnHand: 3,
  safetyStock: 10,
  onOrderQty: 0,
  suggestedQty: 17,
  suggestedValue: 34,
  ...patch
});

describe('ReorderPageComponent', () => {
  let spectator: Spectator<ReorderPageComponent>;
  const granted = new Set<string>();
  const data = signal<PagedResultOfReorderSuggestion | undefined>(undefined);
  const load = jest.fn<void, [ReorderQuery]>();
  const reset = jest.fn();
  const remember = jest.fn();
  const partnersReset = jest.fn();
  const warehousesReset = jest.fn();
  let remembered: string | null = null;
  const createComponent = createComponentFactory({
    component: ReorderPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { $user: signal({ id: 'u1' }) } },
      { provide: PermissionService, useValue: { has: (name: string) => granted.has(name) } },
      {
        provide: ReorderStore,
        useValue: {
          suggestions: {
            $data: data,
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reset,
            reload: jest.fn()
          },
          lastWarehouse: () => remembered,
          remember
        }
      },
      {
        provide: PartnersStore,
        useValue: {
          list: { reset: partnersReset },
          $partners: signal([{ id: SUPPLIER, name: 'Acme Supplies' }]),
          loadActive: jest.fn()
        }
      },
      {
        provide: WarehousesStore,
        useValue: {
          list: { reset: warehousesReset, load: jest.fn() },
          $activeWarehouses: signal([
            { id: WAREHOUSE, name: 'Main warehouse', isActive: true },
            { id: '00000000-0000-4000-8000-0000000000ac', name: 'Annex', isActive: true }
          ])
        }
      }
    ]
  });

  async function render(rows: ReorderSuggestion[], chooseWarehouse = true): Promise<void> {
    spectator = createComponent();
    spectator.detectChanges();
    if (chooseWarehouse) {
      spectator.component.warehouseControl.setValue(WAREHOUSE);
    }
    data.set({ items: rows, page: 1, pageSize: 25, total: rows.length });
    await settle();
  }

  async function settle(): Promise<void> {
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  const headers = (): string[] =>
    spectator.queryAll('th').map(header => (header.textContent ?? '').trim());
  const rowText = (index = 0): string =>
    (spectator.queryAll('tbody tr')[index]?.textContent ?? '').replace(/\s+/g, ' ');
  const checkboxes = (): HTMLInputElement[] =>
    spectator.queryAll<HTMLInputElement>('tbody input[type="checkbox"]');
  const qtyBoxes = (): HTMLInputElement[] =>
    spectator.queryAll<HTMLInputElement>('tbody input[type="number"]');
  const lastQuery = (): ReorderQuery => load.mock.calls[load.mock.calls.length - 1][0];

  async function type(box: HTMLInputElement, text: string): Promise<void> {
    box.value = text;
    box.dispatchEvent(new Event('input'));
    await settle();
  }

  async function pick(index: number): Promise<void> {
    checkboxes()[index].click();
    await settle();
  }

  const press = (): void => {
    spectator.click(spectator.query('.reorder-page-create app-button button') as HTMLElement);
    spectator.detectChanges();
  };

  async function chooseSupplier(): Promise<void> {
    spectator.component.supplierControl.setValue(SUPPLIER);
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  beforeEach(() => {
    load.mockClear();
    reset.mockClear();
    remember.mockClear();
    partnersReset.mockClear();
    warehousesReset.mockClear();
    remembered = null;
    granted.clear();
    data.set(undefined);
  });

  it('sends nothing until a warehouse is chosen (the API refuses without one) and says so', async () => {
    granted.add(PERMISSION.PoRead);
    await render([], false);

    expect(load).not.toHaveBeenCalled();
    expect(spectator.element.textContent).toContain('Choose a warehouse to see what to order.');
    expect(spectator.query('table')).toBeNull();
  });

  it('loads the chosen warehouse with its search and remembers the choice', async () => {
    await render([row(1)]);

    expect(lastQuery()).toMatchObject({ warehouseId: WAREHOUSE, page: 1 });
    expect(remember).toHaveBeenCalledWith('u1', WAREHOUSE);
    // The stores outlive a sign-out: the last user's rows, suppliers and warehouses are forgotten before anything loads.
    for (const forgotten of [reset, partnersReset, warehousesReset]) {
      expect(forgotten).toHaveBeenCalled();
    }
  });

  it('shows the figures of a row and the value to a person with cost.view', async () => {
    granted.add(PERMISSION.CostView);
    await render([
      row(1, { qtyOnHand: 3, onOrderQty: 5, suggestedQty: 12.5, suggestedValue: 25 }),
      row(2, { suggestedValue: null as unknown as undefined })
    ]);

    expect(headers()).toEqual(
      expect.arrayContaining(['On hand', 'Safety stock', 'On order', 'Order qty'])
    );
    expect(headers().some(header => header.startsWith('Est. value'))).toBe(true);
    expect(rowText()).toContain('SKU-1');
    expect(rowText()).toContain('25.00');
    // A value the server could not work out is a dash, never 0.00.
    expect(rowText(1)).not.toContain('0.00');
    expect(rowText(1)).toMatch(/ - ?$/);
  });

  it('has no value column and no money for a person without cost.view (the API sends null)', async () => {
    await render([row(1, { suggestedValue: null as unknown as undefined })]);

    expect(headers().some(header => header.startsWith('Est. value'))).toBe(false);
    expect(rowText()).not.toMatch(/\d\.\d{2}/);
    expect(spectator.queryAll('tbody tr:first-child td')).toHaveLength(headers().length);
  });

  it.each([PERMISSION.PoManage, PERMISSION.CostView, PERMISSION.PoRead])(
    'offers no selection, no supplier and no editing with %p alone',
    async permission => {
      granted.add(permission);
      await render([row(1)]);

      expect(checkboxes()).toHaveLength(0);
      expect(spectator.query('.reorder-page-create')).toBeNull();
      expect(qtyBoxes()).toHaveLength(0);
      expect(rowText()).toContain('17');
    }
  );

  describe('creating', () => {
    beforeEach(() => {
      granted.add(PERMISSION.PoManage);
      granted.add(PERMISSION.CostView);
    });

    it('starts each box at the suggested quantity and refuses a quantity the editor would refuse', async () => {
      await render([row(1, { suggestedQty: 12.5 })]);
      expect(qtyBoxes()[0].value).toBe('12.5');

      await type(qtyBoxes()[0], '0');
      expect(spectator.element.textContent).toContain('Enter a quantity above 0');
      expect(qtyBoxes()[0].getAttribute('aria-invalid')).toBe('true');

      await type(qtyBoxes()[0], '3');
      expect(qtyBoxes()[0].getAttribute('aria-invalid')).toBeNull();
    });

    it('opens the editor with the supplier, the warehouse and the edited quantities of the selected rows', async () => {
      await render([row(1), row(2), row(3)]);
      const navigate = jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      await type(qtyBoxes()[0], '4');
      await pick(0);
      await pick(2);
      await chooseSupplier();

      press();

      expect(navigate).toHaveBeenCalledWith(['/', 'purchase-orders', 'new'], {
        queryParams: {
          warehouseId: WAREHOUSE,
          supplierId: SUPPLIER,
          line: ['SKU-1|4', 'SKU-3|17']
        }
      });
    });

    it('puts the focus on the first field with an error once the errors are drawn', async () => {
      await render([row(1)]);

      press();
      await settle();

      expect(document.activeElement).toBe(spectator.query('.reorder-page-create select'));
    });

    it('does not open the editor while a selected row holds a bad quantity, and says which', async () => {
      await render([row(1), row(2)]);
      const navigate = jest.spyOn(spectator.inject(Router), 'navigate');
      await type(qtyBoxes()[1], '');
      await pick(1);
      await chooseSupplier();

      press();

      expect(navigate).not.toHaveBeenCalled();
      expect(spectator.element.textContent).toContain(
        'Correct the quantities that have an error under them.'
      );
    });

    it('selects every row of the page at once and says how many lines the editor takes', async () => {
      const rows = Array.from({ length: MAX_PREFILL_LINES + 3 }, (_, index) => row(index + 1));
      await render(rows);
      spectator.query<HTMLInputElement>('.reorder-page-selection input[type="checkbox"]')?.click();
      await settle();

      expect(checkboxes().every(box => box.checked)).toBe(true);
      expect(spectator.element.textContent).toContain(`first ${MAX_PREFILL_LINES} selected`);
      expect(spectator.element.textContent).toContain('3 are left out');
    });

    it('says the selection is for one page, and a new warehouse clears it and shows nothing of the old one', async () => {
      await render([row(1)]);
      await type(qtyBoxes()[0], '4');
      await pick(0);
      expect(checkboxes()[0].checked).toBe(true);
      expect(spectator.element.textContent).toContain('Only this page is selected');

      spectator.component.warehouseControl.setValue('00000000-0000-4000-8000-0000000000ac');
      await settle();

      expect(spectator.component.$pickedIds().size).toBe(0);
      expect(spectator.component.$rows()).toHaveLength(0);
      expect(spectator.component.qtyOf(row(1))).toBe(17);
      // The total of the old warehouse is not shown under the new one.
      expect(spectator.query('app-pager')).toBeNull();
    });
  });
});
