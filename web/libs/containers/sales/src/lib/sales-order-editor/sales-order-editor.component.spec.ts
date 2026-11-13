import { Component, input, output, signal } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { EMPTY, Subject, of, throwError } from 'rxjs';
import { PriceLookupDto, PriceSource, ProductDto, SalesOrderDto, SalesOrderStatus } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { CatalogStore } from '@ims/features/catalog';
import { PriceListsStore, SalesOrdersStore } from '@ims/features/sales';
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
  readonly $picked = output<PickedProduct>({ alias: 'picked' });
  readonly $termChanged = output<string>({ alias: 'termChanged' });
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
  const lookup = jest.fn<ReturnType<PriceListsStore['lookup']>, [string, string, string]>();
  const permissionHas = jest.fn((code: string) => code === 'so.manage');
  const partnersData = signal<{ total: number; items: unknown[] } | undefined>(undefined);
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
      { provide: PriceListsStore, useValue: { lookup } },
      // A seller: so.manage and nothing about price lists. The price they are offered must not depend on more.
      { provide: PermissionService, useValue: { has: permissionHas, hasAny: () => true } },
      {
        provide: PartnersStore,
        useValue: {
          $partners: signal([{ id: CUSTOMER, name: 'Acme Trading' }]),
          loadActive,
          list: { reset: partnersReset, $data: partnersData }
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
    partnersData.set(undefined);
    lookup.mockReset();
    lookup.mockReturnValue(of({ found: false, price: undefined, source: undefined }));
    permissionHas.mockClear();
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

  describe('review fixes', () => {
    it('a customer past the first page can be found: a truncated list says so and offers a search that asks the API', async () => {
      jest.useFakeTimers();
      partnersData.set({ total: 450, items: [{ id: CUSTOMER }] });
      spectator = createComponent();
      spectator.detectChanges();
      await jest.advanceTimersByTimeAsync(0);
      loadActive.mockClear();

      expect(text()).toContain('Only the first customers are listed.');
      component().$customerSearch.set('zed');
      spectator.detectChanges();
      await jest.advanceTimersByTimeAsync(1000);
      spectator.detectChanges();

      expect(loadActive).toHaveBeenCalledWith('Customer', 'zed');
      jest.useRealTimers();
    });

    it('no search box and no notice while every customer is in the list', async () => {
      partnersData.set({ total: 1, items: [{ id: CUSTOMER }] });
      await render();

      expect(text()).not.toContain('Only the first customers are listed.');
      expect(spectator.query('app-search-input')).toBeNull();
    });

    it('says a blank or zero price is saved as 0.00 and cannot be submitted, and says it no more once priced', async () => {
      await render();
      component().onProductPicked({ product: PRODUCT, available: null });
      spectator.detectChanges();
      expect(text()).toContain('A blank price is saved as 0.00.');

      component().lines.at(0).patchValue({ unitCost: 2 });
      spectator.detectChanges();
      expect(text()).not.toContain('A blank price is saved as 0.00.');

      component().lines.at(0).patchValue({ unitCost: 0 });
      spectator.detectChanges();
      expect(text()).toContain('A blank price is saved as 0.00.');
    });

    it('says the warehouse is fixed once saved, on an existing order only', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      expect(text()).toContain('Fixed once the order is saved.');

      detail.$data.set(undefined);
      await render();
      expect(text()).not.toContain('Fixed once the order is saved.');
    });

    it('the totals are a live region only after the typing stopped, and a removed line is announced', async () => {
      jest.useFakeTimers();
      spectator = createComponent();
      spectator.detectChanges();
      component().onProductPicked({ product: PRODUCT, available: null });
      component().lines.at(0).patchValue({ qty: 2, unitCost: 3 });
      spectator.detectChanges();

      expect(spectator.query('.so-editor-totals')?.hasAttribute('aria-live')).toBe(false);
      const announced = (): string =>
        spectator.query('.so-editor-bar [role="status"]')?.textContent?.trim() ?? '';
      expect(announced()).not.toContain('6.00');
      await jest.advanceTimersByTimeAsync(2000);
      spectator.detectChanges();
      expect(announced()).toContain('6.00');

      component().onRemove(0);
      spectator.detectChanges();
      expect(spectator.query('.so-editor-lines [role="status"]')?.textContent).toContain(
        'SKU-1 removed.'
      );
      jest.useRealTimers();
    });

    it('the units of a line are built once, not on every key', async () => {
      await render();
      component().onProductPicked({ product: PRODUCT, available: null });
      const first = component().$rows()[0].units;

      component().lines.at(0).patchValue({ qty: 3 });

      expect(component().$rows()[0].units).toBe(first);
    });

    it('two identical messages are both shown (no duplicate-key crash)', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      save.$operation.set(
        failureOperation(
          apiError(400, API_ERROR_KIND.Validation, {
            fieldErrors: { 'lines[0]': ['Same.', 'Same.'] }
          })
        )
      );
      spectator.detectChanges();

      expect(spectator.queryAll('app-banner[role="alert"]')).toHaveLength(2);
    });

    it('moves the focus to the first field the API found fault with', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      save.$operation.set(
        failureOperation(
          apiError(400, API_ERROR_KIND.Validation, { fieldErrors: { remark: ['Too long.'] } })
        )
      );
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(document.activeElement?.getAttribute('aria-invalid')).toBe('true');
      expect(text()).toContain('Too long.');
    });

    it('an order that stops being editable leaves with the form clean, so the unsaved-changes guard does not ask', async () => {
      detail.$data.set(draft());
      await render({ id: ID });
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().form.controls.remark.setValue('typed');
      component().form.controls.remark.markAsDirty();
      expect(component().hasUnsavedChanges()).toBe(true);

      detail.$data.set(draft({ allowedActions: ['cancel'] }));
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(component().hasUnsavedChanges()).toBe(false);
    });
  });

  describe('the list price of a line (PL3.14)', () => {
    const found = (price: number, source = PriceSource.Customer): PriceLookupDto => ({
      found: true,
      price,
      source
    });
    const settle = async (): Promise<void> => {
      await jest.advanceTimersByTimeAsync(300);
      spectator.detectChanges();
    };
    const open = async (): Promise<void> => {
      jest.useFakeTimers();
      await render();
      component().form.patchValue({ customerId: CUSTOMER, warehouseId: WAREHOUSE });
      await settle();
    };
    const price = (): number | null => component().lines.at(0).controls.unitCost.value;
    const note = (): string =>
      (spectator.query('.so-editor-listprice')?.textContent ?? '').replace(/\s+/g, ' ').trim();
    afterEach(() => jest.useRealTimers());

    it('asks for the customer, product and unit of a new line and puts the list price in the field; a seller with so.manage only gets it', async () => {
      lookup.mockReturnValue(of(found(5)));
      await open();

      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();

      expect(lookup).toHaveBeenCalledWith(CUSTOMER, 'p1', 'pcs');
      expect(price()).toBe(5);
      expect(note()).toBe("List price 5.00, from the customer's list.");
      expect(permissionHas.mock.calls.map(call => call[0])).not.toContain('pricelist.manage');
    });

    it('a price the person changes stays theirs; the list price is shown beside it in words, and "Use list price" takes it back', async () => {
      lookup.mockReturnValue(of(found(5, PriceSource.Default)));
      await open();
      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();

      component().lines.at(0).patchValue({ unitCost: 7 });
      spectator.detectChanges();
      expect(note()).toContain('List price 5.00, from the default list. This price is different.');
      expect(price()).toBe(7);

      spectator.click(spectator.query('.so-editor-listprice button') as HTMLElement);
      expect(price()).toBe(5);
      expect(note()).not.toContain('different');
    });

    it('no list price (found false) leaves the field empty with a quiet note, and typing goes on', async () => {
      await open();

      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();

      expect(price()).toBeNull();
      expect(note()).toBe('No list price for this product and unit. Type the price.');
      component().lines.at(0).patchValue({ unitCost: 4 });
      expect(price()).toBe(4);
    });

    it('a failed lookup is a quiet note, never a toast or a blocked Save', async () => {
      lookup.mockReturnValue(throwError(() => new Error('down')));
      await open();
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);

      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();
      component().lines.at(0).patchValue({ unitCost: 4 });
      spectator.detectChanges();

      expect(note()).toBe('List price unavailable. Type the price.');
      expect(saveButton().disabled).toBe(false);
      spectator.click(saveButton());
      expect(save.run).toHaveBeenCalled();
    });

    it('a change of customer asks again for EVERY line, and a typed price is kept', async () => {
      lookup.mockReturnValue(of(found(5)));
      await open();
      component().onProductPicked({ product: PRODUCT, available: null });
      component().onProductPicked({
        product: { ...PRODUCT, id: 'p2', sku: 'SKU-2' } as ProductDto,
        available: null
      });
      await settle();
      component().lines.at(1).patchValue({ unitCost: 8 });
      lookup.mockClear();
      lookup.mockReturnValue(of(found(6)));

      component().form.patchValue({ customerId: 'c-other' });
      await settle();

      expect(lookup.mock.calls.map(call => call.join('|'))).toEqual([
        'c-other|p1|pcs',
        'c-other|p2|pcs'
      ]);
      expect(price()).toBe(6);
      expect(component().lines.at(1).controls.unitCost.value).toBe(8);
    });

    it('a change of unit asks again; the price this page put there is replaced, or cleared when the new unit has none', async () => {
      lookup.mockReturnValue(of(found(5)));
      await open();
      const packed = {
        ...PRODUCT,
        conversions: [{ fromUomId: 'ctn', toUomId: 'pcs', factor: 12 }]
      } as unknown as ProductDto;
      component().onProductPicked({ product: packed, available: null });
      await settle();
      expect(price()).toBe(5);

      lookup.mockReturnValue(of(found(55)));
      component().lines.at(0).controls.uomId.setValue('ctn');
      await settle();
      expect(lookup).toHaveBeenLastCalledWith(CUSTOMER, 'p1', 'ctn');
      expect(price()).toBe(55);

      lookup.mockReturnValue(of({ found: false, price: undefined, source: undefined }));
      component().lines.at(0).controls.uomId.setValue('pcs');
      await settle();
      expect(price()).toBeNull();
    });

    it('the answer that arrives last is not always the answer that counts: an older answer for a line is ignored', async () => {
      const slow = new Subject<PriceLookupDto>();
      lookup.mockReturnValueOnce(slow).mockReturnValueOnce(of(found(9)));
      await open();
      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();

      component().form.patchValue({ customerId: 'c-other' });
      await settle();
      slow.next(found(1));
      slow.complete();
      await settle();

      expect(price()).toBe(9);
    });

    it('after a Reload every line asks again, so the field agrees with what the server will decide at save', async () => {
      lookup.mockReturnValue(of(found(5)));
      detail.$data.set(draft());
      jest.useFakeTimers();
      await render({ id: ID });
      await settle();
      lookup.mockClear();

      detail.$data.set(draft({ concurrencyToken: 'BBBB' }));
      spectator.detectChanges();
      await settle();

      expect(lookup).toHaveBeenCalledWith(CUSTOMER, 'p1', 'pcs');
      // The saved price is the person's: it stays, and the list price is shown beside it.
      expect(price()).toBe(5);
    });

    it('a saved price different from the list price shows the list price beside it, and is not overwritten', async () => {
      lookup.mockReturnValue(of(found(4.5)));
      detail.$data.set(draft());
      jest.useFakeTimers();
      await render({ id: ID });
      await settle();

      expect(price()).toBe(5);
      expect(note()).toContain(
        "List price 4.50, from the customer's list. This price is different."
      );
    });

    it('a unit changed on an order opened for edit asks again, like a new line', async () => {
      lookup.mockReturnValue(of(found(5)));
      detail.$data.set(draft());
      jest.useFakeTimers();
      await render({ id: ID });
      await settle();
      lookup.mockClear();
      lookup.mockReturnValue(of(found(55)));

      component().lines.at(0).controls.uomId.setValue('ctn');
      await settle();

      expect(lookup).toHaveBeenCalledWith(CUSTOMER, 'p1', 'ctn');
    });

    it('a pending lookup is a visible note in the same element, Save stays usable, and an answer after Save never fills the field', async () => {
      const slow = new Subject<PriceLookupDto>();
      lookup.mockReturnValue(slow);
      await open();
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().onProductPicked({ product: PRODUCT, available: null });
      component().lines.at(0).patchValue({ unitCost: null });
      await jest.advanceTimersByTimeAsync(300);
      spectator.detectChanges();

      expect(note()).toBe('Checking list price…');
      expect(spectator.query('.so-editor-listprice')?.id).toMatch(/^so-listprice-/);
      expect(saveButton().disabled).toBe(false);

      spectator.click(saveButton());
      slow.next(found(1));
      slow.complete();
      await settle();

      expect(save.run).toHaveBeenCalledTimes(1);
      expect(price()).toBeNull();
    });

    it('a refused save asks the prices again', async () => {
      lookup.mockReturnValue(of(found(5)));
      await open();
      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();
      save.run.mockReturnValueOnce(EMPTY as never);
      lookup.mockClear();

      spectator.click(saveButton());
      await settle();

      expect(lookup).toHaveBeenCalledWith(CUSTOMER, 'p1', 'pcs');
    });

    it('the note is tied to the price field, the button names the product, and ONE debounced status says how many were filled', async () => {
      lookup.mockReturnValue(of(found(5)));
      await open();
      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();
      await jest.advanceTimersByTimeAsync(2000);
      spectator.detectChanges();

      const id = spectator.query('.so-editor-listprice')?.id ?? '';
      expect(
        spectator.query('input[aria-label="Unit price of SKU-1"]')?.getAttribute('aria-describedby')
      ).toContain(id);
      expect(spectator.query('.so-editor-bar')?.textContent).toContain('1 list price filled');
      expect(spectator.queryAll('.so-editor-listprice[aria-live]')).toHaveLength(0);

      component().lines.at(0).patchValue({ unitCost: 7 });
      spectator.detectChanges();
      expect(spectator.query('.so-editor-listprice button')?.getAttribute('aria-label')).toBe(
        'Use list price for SKU-1'
      );
    });

    it('an emptied price shows the list price with "Use list price"; a price of 0.00 is filled; identical money is never called different', async () => {
      lookup.mockReturnValue(of(found(5)));
      await open();
      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();

      component().lines.at(0).patchValue({ unitCost: null });
      spectator.detectChanges();
      expect(note()).toContain('List price 5.00');
      expect(spectator.query('.so-editor-listprice button')).not.toBeNull();
      spectator.click(spectator.query('.so-editor-listprice button') as HTMLElement);
      expect(price()).toBe(5);

      component().lines.at(0).patchValue({ unitCost: 5.004 });
      spectator.detectChanges();
      expect(note()).not.toContain('different');
      expect(spectator.query('.so-editor-listprice button')).toBeNull();
    });

    it('a saved price of 0.00 is no price: the list price fills it', async () => {
      lookup.mockReturnValue(of(found(5)));
      detail.$data.set(draft({ lines: [{ ...draft().lines[0], unitPrice: 0 }] }));
      jest.useFakeTimers();
      await render({ id: ID });
      await settle();

      expect(price()).toBe(5);
    });

    it('says in words when it replaced or cleared a price of its own after a new unit', async () => {
      lookup.mockReturnValue(of(found(5)));
      await open();
      component().onProductPicked({ product: PRODUCT, available: null });
      await settle();

      lookup.mockReturnValue(of(found(9)));
      component().lines.at(0).controls.uomId.setValue('ctn');
      await settle();
      expect(note()).toContain('Price changed to the list price 9.00');

      lookup.mockReturnValue(of({ found: false, price: undefined, source: undefined }));
      component().lines.at(0).controls.uomId.setValue('pcs');
      await settle();
      expect(note()).toContain('Price cleared: no list price for this product and unit.');
    });

    it('a Reload forgets what was waiting for the old lines before the new ones are built', async () => {
      const slow = new Subject<PriceLookupDto>();
      lookup.mockReturnValueOnce(slow).mockReturnValue(of(found(4)));
      detail.$data.set(draft({ lines: [{ ...draft().lines[0], unitPrice: null as never }] }));
      jest.useFakeTimers();
      await render({ id: ID });
      await settle();

      detail.$data.set(
        draft({
          concurrencyToken: 'BBBB',
          lines: [{ ...draft().lines[0], unitPrice: null as never }]
        })
      );
      spectator.detectChanges();
      await settle();
      slow.next(found(1));
      slow.complete();
      await settle();

      expect(price()).toBe(4);
    });

    it("what was asked for order A never shows on order B, and a former user's refusal of the lookup is gone", async () => {
      const slow = new Subject<PriceLookupDto>();
      lookup.mockReturnValueOnce(throwError(() => apiError(403, API_ERROR_KIND.Forbidden)));
      lookup.mockReturnValueOnce(slow);
      detail.$data.set(draft({ lines: [{ ...draft().lines[0], unitPrice: null as never }] }));
      jest.useFakeTimers();
      await render({ id: ID });
      await settle();
      expect(note()).toBe('List price unavailable. Type the price.');

      // Order B opens while a second answer for A is on its way.
      component().form.patchValue({ customerId: 'c-other' });
      await settle();
      detail.$data.set(
        draft({
          id: OTHER_ID,
          no: 'SO-B',
          lines: [{ ...draft().lines[0], unitPrice: null as never }]
        })
      );
      spectator.setInput('id', OTHER_ID);
      spectator.detectChanges();
      slow.next(found(1));
      slow.complete();
      await settle();

      expect(note()).not.toContain('unavailable');
      expect(price()).not.toBe(1);
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
