import { Component, input, output, signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { ProductDto, PurchaseOrderDto, PurchaseOrderStatus } from '@ims/api';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { CatalogStore } from '@ims/features/catalog';
import { PurchaseOrdersStore } from '@ims/features/purchasing';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiError,
  ApiOperation,
  IDLE_OPERATION,
  failureOperation
} from '@ims/models';
import { PurchaseOrderEditorPageComponent } from './purchase-order-editor.component';

const ID = '00000000-0000-4000-8000-000000000001';
const SUPPLIER = '00000000-0000-4000-8000-0000000000a1';
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

/** A saved draft as the API sends it: what it does not have is JSON null. */
function draft(over: Partial<PurchaseOrderDto> = {}): PurchaseOrderDto {
  return {
    id: ID,
    no: 'PO-MAIN-2610-0001',
    status: PurchaseOrderStatus.Draft,
    supplierId: SUPPLIER,
    supplierName: 'Acme Supplies',
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
  } as unknown as PurchaseOrderDto;
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

describe('PurchaseOrderEditorPageComponent', () => {
  let spectator: Spectator<PurchaseOrderEditorPageComponent>;
  const detail = {
    $data: signal<PurchaseOrderDto | undefined>(undefined),
    $isLoading: signal(false),
    $isFailed: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    load: jest.fn(),
    reload: jest.fn(),
    reset: jest.fn()
  };
  const save = {
    run: jest.fn(() => of(draft())),
    $isRunning: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    reset: jest.fn()
  };
  const store = { detail, save, resetCommands: jest.fn() };
  const createComponent = createComponentFactory({
    component: PurchaseOrderEditorPageComponent,
    detectChanges: false,
    overrideComponents: [
      [
        PurchaseOrderEditorPageComponent,
        {
          remove: { imports: [ProductPickerComponent] },
          add: { imports: [StubProductPickerComponent] }
        }
      ]
    ],
    providers: [
      provideRouter([]),
      { provide: PurchaseOrdersStore, useValue: store },
      {
        provide: PartnersStore,
        useValue: {
          $partners: signal([{ id: SUPPLIER, name: 'Acme Supplies' }]),
          loadActive: jest.fn()
        }
      },
      {
        provide: WarehousesStore,
        useValue: {
          $activeWarehouses: signal([
            { id: WAREHOUSE, name: 'Main warehouse' },
            { id: 'w2', name: 'Second' }
          ]),
          ensureLoaded: jest.fn()
        }
      },
      { provide: CatalogStore, useValue: { $unitCodes: () => new Map() } }
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
    spectator.query<HTMLButtonElement>('.po-editor-bar button')!;

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
      store.resetCommands
    ]) {
      mock.mockClear();
    }
  });

  describe('a new order', () => {
    it('asks for a supplier, a warehouse and a line, in words, and sends nothing', async () => {
      await render();

      spectator.click(saveButton());
      await spectator.fixture.whenStable();
      spectator.detectChanges();

      expect(save.run).not.toHaveBeenCalled();
      expect(spectator.queryAll('[aria-invalid="true"]').length).toBeGreaterThanOrEqual(2);
      expect(text()).toContain('Add at least one item.');
    });

    it('saves the whole body (no token) and goes to the order, without asking to discard', async () => {
      await render();
      const navigateSpy = jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().form.patchValue({
        supplierId: SUPPLIER,
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
          supplierId: SUPPLIER,
          remark: 'Bay 3',
          lines: [{ productId: 'p1', qty: 4, uomId: 'pcs', unitPrice: 2.5, remark: undefined }]
        }
      });
      expect(navigateSpy).toHaveBeenCalledWith(['/', 'purchase-orders', ID], { replaceUrl: true });
      expect(component().hasUnsavedChanges()).toBe(false);
    });

    it('forgets what the last page and the last user left in the store when it opens', async () => {
      await render();

      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reset).toHaveBeenCalled();
    });
  });

  describe('an existing draft', () => {
    it('loads it with the warehouse fixed and the lines filled, and a null remark is empty', async () => {
      detail.$data.set(draft());
      await render({ id: ID });

      expect(detail.load).toHaveBeenCalledWith(ID, expect.anything());
      expect(component().form.controls.warehouseId.disabled).toBe(true);
      expect(component().form.controls.remark.value).toBe('');
      expect(component().lines.at(0).controls.unitCost.value).toBe(5);
      expect(component().hasUnsavedChanges()).toBe(false);
    });

    it('editing only the remark still sends every field, with the version that was opened (If-Match)', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().form.controls.remark.setValue('Call first');
      component().form.controls.remark.markAsDirty();

      spectator.click(saveButton());

      expect(save.run).toHaveBeenCalledWith({
        ref: { id: ID, concurrencyToken: 'AAAA' },
        body: {
          warehouseId: WAREHOUSE,
          supplierId: SUPPLIER,
          remark: 'Call first',
          lines: [{ productId: 'p1', qty: 10, uomId: 'pcs', unitPrice: 5, remark: undefined }]
        }
      });
    });

    it('is not a form when the API does not offer edit (the order left Draft)', async () => {
      detail.$data.set(draft({ allowedActions: ['cancel'], status: PurchaseOrderStatus.Approved }));
      await render({ id: ID });

      expect(text()).toContain("This order can't be edited any more.");
      expect(spectator.query('form')).toBeNull();
    });

    it('a 409 that names document_changed is one Reload banner and locks Save; Reload reads the order again', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      save.$operation.set(
        failureOperation(
          apiError(409, API_ERROR_KIND.Conflict, { extensions: { code: 'document_changed' } })
        )
      );
      spectator.detectChanges();

      expect(text()).toContain('PO-MAIN-2610-0001 changed since you opened it');
      expect(saveButton().disabled).toBe(true);

      spectator.click(spectator.query('app-banner app-button button') as HTMLElement);
      expect(save.reset).toHaveBeenCalled();
      expect(detail.reload).toHaveBeenCalled();
    });

    it('any other 409 and the API field messages are shown in words, the price message at the price field', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      save.$operation.set(
        failureOperation(
          apiError(400, API_ERROR_KIND.Validation, {
            fieldErrors: {
              'lines[0].unitPrice': ['Price has too many decimals.'],
              remark: ['Remark is too long.']
            }
          })
        )
      );
      spectator.detectChanges();

      expect(text()).toContain('Price has too many decimals.');
      expect(text()).toContain('Remark is too long.');

      save.$operation.set(
        failureOperation(
          apiError(409, API_ERROR_KIND.Conflict, { message: 'Only a draft can be edited.' })
        )
      );
      spectator.detectChanges();

      expect(spectator.query('app-banner[role="alert"]')?.textContent).toContain(
        'Only a draft can be edited.'
      );
      expect(text()).not.toContain('changed since you opened it');
    });
  });
});
