import { Component, input, output, signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { ProductDto, SalesOrderDto, SalesOrderStatus } from '@ims/api';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { CatalogStore } from '@ims/features/catalog';
import { SalesOrdersStore } from '@ims/features/sales';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiError,
  ApiOperation,
  IDLE_OPERATION,
  failureOperation
} from '@ims/models';
import { SalesOrderEditorPageComponent } from './sales-order-editor.component';

const ID = '00000000-0000-4000-8000-000000000001';
const OTHER_ID = '00000000-0000-4000-8000-000000000002';
const CUSTOMER = '00000000-0000-4000-8000-0000000000a1';
const WAREHOUSE = '00000000-0000-4000-8000-0000000000b1';

@Component({ selector: 'app-product-picker', template: '' })
class StubProductPickerComponent {
  $notice = input<string | undefined>(undefined, { alias: 'notice' });
  $picked = output<PickedProduct>({ alias: 'picked' });
  $termChanged = output<string>({ alias: 'termChanged' });
}

const PRODUCT = {
  id: 'p1',
  sku: 'SKU-1',
  name: 'Widget',
  isActive: true,
  baseUomId: 'pcs',
  baseUomCode: 'PCS',
  conversions: []
} as unknown as ProductDto;

/** A saved draft as a holder of so.manage gets it: what it does not have is absent or null. */
function draft(over: Partial<SalesOrderDto> = {}): SalesOrderDto {
  return {
    id: ID,
    no: 'SO-MAIN-2611-0001',
    status: SalesOrderStatus.Draft,
    customerId: CUSTOMER,
    customerName: 'Acme Trading',
    warehouseId: WAREHOUSE,
    warehouseName: 'Main warehouse',
    remark: null,
    concurrencyToken: 'AAAA',
    allowedActions: ['edit', 'submit'],
    lines: [
      {
        lineNo: 1,
        productId: 'p1',
        sku: 'SKU-1',
        productName: 'Widget',
        qty: 10,
        uomId: 'pcs',
        uomCode: 'PCS',
        baseQty: 10,
        unitPrice: 5,
        amount: 50,
        remark: null
      }
    ],
    ...over
  } as unknown as SalesOrderDto;
}

function apiError(status: number, kind: ApiError['kind'], extra: Partial<ApiError> = {}): ApiError {
  return {
    isApiError: true,
    kind,
    status,
    title: 't',
    detail: 'x',
    message: 'x',
    fieldErrors: {},
    extensions: {},
    ...extra
  };
}

const STALE = () =>
  failureOperation(
    apiError(409, API_ERROR_KIND.Conflict, { extensions: { code: 'document_changed' } })
  );

describe('SalesOrderEditorPageComponent', () => {
  let spectator: Spectator<SalesOrderEditorPageComponent>;
  const detail = {
    $data: signal<SalesOrderDto | undefined>(undefined),
    $isLoading: signal(false),
    $isFailed: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    load: jest.fn(),
    reload: jest.fn(),
    reset: jest.fn()
  };
  const saveOperation = signal<ApiOperation>(IDLE_OPERATION);
  const save = {
    run: jest.fn(() => of(draft())),
    $isRunning: signal(false),
    $operation: saveOperation,
    reset: jest.fn(() => saveOperation.set(IDLE_OPERATION))
  };
  // Like the real store: forgetting the commands empties the outcome of the save, whoever left it.
  const store = {
    detail,
    save,
    resetCommands: jest.fn(() => saveOperation.set(IDLE_OPERATION))
  };
  const partnersReset = jest.fn();
  const warehousesReset = jest.fn();
  const loadActive = jest.fn();
  const createComponent = createComponentFactory({
    component: SalesOrderEditorPageComponent,
    detectChanges: false,
    overrideComponents: [
      [
        SalesOrderEditorPageComponent,
        {
          remove: { imports: [ProductPickerComponent] },
          add: { imports: [StubProductPickerComponent] }
        }
      ]
    ],
    providers: [
      provideRouter([]),
      { provide: SalesOrdersStore, useValue: store },
      {
        provide: PartnersStore,
        useValue: {
          $partners: signal([{ id: CUSTOMER, name: 'Acme Trading' }]),
          loadActive,
          list: { reset: partnersReset }
        }
      },
      {
        provide: WarehousesStore,
        useValue: {
          $activeWarehouses: signal([{ id: WAREHOUSE, name: 'Main warehouse' }]),
          ensureLoaded: jest.fn(),
          list: { reset: warehousesReset }
        }
      },
      { provide: CatalogStore, useValue: { $unitCodes: () => new Map([['ctn', 'CTN']]) } }
    ]
  });

  async function render(props: Record<string, unknown> = {}): Promise<void> {
    spectator = createComponent({ props: props as never });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  const text = (): string => (spectator.element.textContent ?? '').replace(/\s+/g, ' ');
  const component = () => spectator.component;
  const saveButton = (): HTMLButtonElement =>
    spectator.query<HTMLButtonElement>('.so-editor-bar button')!;

  beforeEach(() => {
    detail.$data.set(undefined);
    detail.$operation.set(IDLE_OPERATION);
    detail.$isFailed.set(false);
    save.$operation.set(IDLE_OPERATION);
    save.run.mockClear();
    for (const mock of [
      detail.load,
      detail.reload,
      detail.reset,
      save.reset,
      store.resetCommands,
      loadActive
    ]) {
      mock.mockClear();
    }
  });

  describe('a new order', () => {
    it('asks for a customer and a line, in words, focus on the first, and sends nothing', async () => {
      await render();

      spectator.click(saveButton());
      await spectator.fixture.whenStable();
      spectator.detectChanges();

      expect(save.run).not.toHaveBeenCalled();
      // The only warehouse is pre-selected: the customer is what is missing.
      expect(spectator.queryAll('[aria-invalid="true"]').length).toBe(1);
      expect(text()).toContain('Add at least one item.');
      expect(document.activeElement?.getAttribute('aria-invalid')).toBe('true');
    });

    it('lists customers only, sees amounts and the total, saves the whole body (no token) and goes to the order', async () => {
      await render();
      expect(loadActive).toHaveBeenCalledWith('Customer');
      const navigateSpy = jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().form.patchValue({
        customerId: CUSTOMER,
        warehouseId: WAREHOUSE,
        remark: ' Bay 3 '
      });
      component().onProductPicked({ product: PRODUCT, available: null });
      component().lines.at(0).patchValue({ qty: 4, unitCost: 2.5 });
      spectator.detectChanges();

      expect(text()).toContain('10.00');

      spectator.click(saveButton());

      expect(save.run).toHaveBeenCalledWith({
        ref: undefined,
        body: {
          warehouseId: WAREHOUSE,
          customerId: CUSTOMER,
          remark: 'Bay 3',
          lines: [{ productId: 'p1', qty: 4, uomId: 'pcs', unitPrice: 2.5, remark: undefined }]
        }
      });
      expect(navigateSpy).toHaveBeenCalledWith(['/', 'sales-orders', ID], { replaceUrl: true });
      spectator.click(saveButton());
      expect(save.run).toHaveBeenCalledTimes(1);
      expect(component().hasUnsavedChanges()).toBe(false);
    });
  });

  describe('an existing draft', () => {
    it('has its warehouse fixed; editing only the remark still sends every field with the opened version (If-Match)', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      expect(component().form.controls.warehouseId.disabled).toBe(true);
      expect(component().form.controls.remark.value).toBe('');
      component().form.controls.remark.setValue('Call first');
      component().form.controls.remark.markAsDirty();

      spectator.click(saveButton());

      expect(save.run).toHaveBeenCalledWith({
        ref: { id: ID, concurrencyToken: 'AAAA' },
        body: {
          warehouseId: WAREHOUSE,
          customerId: CUSTOMER,
          remark: 'Call first',
          lines: [{ productId: 'p1', qty: 10, uomId: 'pcs', unitPrice: 5, remark: undefined }]
        }
      });
    });

    it('leaves for the order page when the order is not a draft any more (no Edit offered)', async () => {
      detail.$data.set(draft({ status: SalesOrderStatus.Approved, allowedActions: ['cancel'] }));
      spectator = createComponent({ props: { id: ID } as never });
      const navigate = jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith(['/', 'sales-orders', ID], { replaceUrl: true });
    });

    it('a 409 that names document_changed is one Reload banner and locks Save; Reload reads the order again', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      save.$operation.set(STALE());
      spectator.detectChanges();

      expect(text()).toContain('SO-MAIN-2611-0001 changed since you opened it');
      expect(saveButton().disabled).toBe(true);

      spectator.click(spectator.query('app-banner app-button button') as HTMLElement);
      expect(save.reset).toHaveBeenCalled();
      expect(detail.reload).toHaveBeenCalled();
    });

    it('the API field messages are shown in words, the price message at the price field, and a refusal is the API sentence', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      save.$operation.set(
        failureOperation(
          apiError(400, API_ERROR_KIND.Validation, {
            fieldErrors: {
              'lines[0].unitPrice': ['Price has too many decimals.'],
              'lines[0]': ['The product is switched off.'],
              remark: ['Remark is too long.']
            }
          })
        )
      );
      spectator.detectChanges();

      expect(text()).toContain('Price has too many decimals.');
      expect(text()).toContain('SKU-1: The product is switched off.');
      expect(text()).toContain('Remark is too long.');

      save.$operation.set(
        failureOperation(
          apiError(403, API_ERROR_KIND.Forbidden, { detail: 'You may not.', message: 'General.' })
        )
      );
      spectator.detectChanges();
      expect(text()).toContain('You may not.');
      expect(text()).not.toContain('General.');
    });
  });

  describe('what the last order or the last user left in the store', () => {
    it('forgets the commands and the opened order every time it opens, and reads the customer and warehouse lists fresh', async () => {
      await render();

      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reset).toHaveBeenCalled();
      expect(partnersReset).toHaveBeenCalled();
      expect(warehousesReset).toHaveBeenCalled();
    });

    it('a stale 409 of order A does not show on order B opened next, and Save works there', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      save.$operation.set(STALE());
      spectator.detectChanges();
      expect(text()).toContain('changed since you opened it');
      store.resetCommands.mockClear();
      detail.reset.mockClear();

      detail.$data.set(draft({ id: OTHER_ID, no: 'SO-B' }));
      spectator.setInput('id', OTHER_ID);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reset).toHaveBeenCalled();
      expect(text()).not.toContain('changed since you opened it');
      expect(saveButton().disabled).toBe(false);
    });

    it('a refusal the last user left (the store outlives a sign-out) is gone when the page opens', async () => {
      save.$operation.set(
        failureOperation(apiError(403, API_ERROR_KIND.Forbidden, { detail: 'Not for you.' }))
      );

      await render();

      expect(text()).not.toContain('Not for you.');
      expect(spectator.queryAll('app-banner')).toHaveLength(0);
    });

    it('order A is not left in the form when order B opens, also when B is not found', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      expect(component().lines.length).toBe(1);

      detail.$data.set(undefined);
      spectator.setInput('id', OTHER_ID);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(component().lines.length).toBe(0);
      expect(component().form.controls.warehouseId.disabled).toBe(false);
    });
  });

  describe('lines', () => {
    it('offers the units of a product with a name per line, and the price with its own label', async () => {
      await render();
      const packed = {
        ...PRODUCT,
        conversions: [{ fromUomId: 'ctn', toUomId: 'pcs', factor: 12 }]
      } as unknown as ProductDto;
      component().onProductPicked({ product: packed, available: null });
      spectator.detectChanges();

      expect(
        spectator.query('app-select[formcontrolname="uomId"] select')?.getAttribute('aria-label')
      ).toBe('Unit of SKU-1');
      expect(
        spectator.query('app-input[formcontrolname="unitCost"] input')?.getAttribute('aria-label')
      ).toBe('Unit price of SKU-1');
    });

    it('picking the same product again adds exactly one (no float noise on 1.0001)', async () => {
      await render();
      component().onProductPicked({ product: PRODUCT, available: null });
      component().lines.at(0).controls.qty.setValue(1.0001);

      component().onProductPicked({ product: PRODUCT, available: null });

      expect(component().lines.at(0).controls.qty.value).toBe(2.0001);
    });

    it('the row amount leaves out what the form refuses, like the total', async () => {
      await render();
      component().onProductPicked({ product: PRODUCT, available: null });
      const amount = (): string =>
        spectator.query('.so-editor-amount strong')?.textContent?.trim() ?? '';

      for (const [qty, unitCost, shown] of [
        [3, 1.005, '0.00'],
        [-1, 5, '0.00'],
        [3, 1.01, '3.03']
      ] as [number, number, string][]) {
        component().lines.at(0).patchValue({ qty, unitCost });
        spectator.detectChanges();
        expect(amount()).toBe(shown);
      }
    });

    it('asks before a reload or a closed tab only with unsaved changes', async () => {
      await render();
      const event = { preventDefault: jest.fn() } as unknown as BeforeUnloadEvent;
      component().onBeforeUnload(event);
      expect(event.preventDefault).not.toHaveBeenCalled();

      component().form.patchValue({ customerId: CUSTOMER, warehouseId: WAREHOUSE });
      component().onProductPicked({ product: PRODUCT, available: null });
      component().onBeforeUnload(event);
      expect(event.preventDefault).toHaveBeenCalled();
    });
  });
});
