import { Component, input, output, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { PriceListDto, ProductDto } from '@ims/api';
import { PickedProduct, ProductPickerComponent } from '@ims/containers/catalog-shared';
import { ConfirmService } from '@ims/core/notification';
import { CatalogStore } from '@ims/features/catalog';
import { PriceListsStore, createPriceItemGroup } from '@ims/features/sales';
import { PartnersStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiError,
  ApiOperation,
  IDLE_OPERATION,
  failureOperation
} from '@ims/models';
import { PriceListEditorPageComponent } from './price-list-editor.component';

const ID = '00000000-0000-4000-8000-000000000001';
const OTHER_ID = '00000000-0000-4000-8000-000000000002';
const CUSTOMER = '00000000-0000-4000-8000-0000000000a1';

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

/** A saved list as the API sends it to a holder of pricelist.manage. */
function list(over: Partial<PriceListDto> = {}): PriceListDto {
  return {
    id: ID,
    name: 'Retail',
    isDefault: false,
    customerId: CUSTOMER,
    customerName: 'Acme Trading',
    concurrencyToken: 'AAAA',
    items: [
      {
        productId: 'p1',
        sku: 'SKU-1',
        productName: 'Widget',
        uomId: 'pcs',
        uomCode: 'PCS',
        price: 5
      }
    ],
    ...over
  } as unknown as PriceListDto;
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

describe('PriceListEditorPageComponent', () => {
  let spectator: Spectator<PriceListEditorPageComponent>;
  const detail = {
    $data: signal<PriceListDto | undefined>(undefined),
    $isLoading: signal(false),
    $isFailed: signal(false),
    $operation: signal<ApiOperation>(IDLE_OPERATION),
    load: jest.fn(),
    reload: jest.fn(),
    reset: jest.fn()
  };
  const saveOperation = signal<ApiOperation>(IDLE_OPERATION);
  const deleteOperation = signal<ApiOperation>(IDLE_OPERATION);
  const save = {
    run: jest.fn(() => of(list())),
    $isRunning: signal(false),
    $operation: saveOperation
  };
  const del = {
    run: jest.fn(() => of(undefined)),
    $isRunning: signal(false),
    $operation: deleteOperation
  };
  // Like the real store: forgetting the commands empties the outcome of the save and the delete, whoever left it.
  const store = {
    detail,
    save,
    delete: del,
    resetCommands: jest.fn(() => {
      saveOperation.set(IDLE_OPERATION);
      deleteOperation.set(IDLE_OPERATION);
    })
  };
  const partnersData = signal<{ total: number; items: unknown[] } | undefined>(undefined);
  const loadActive = jest.fn();
  const createComponent = createComponentFactory({
    component: PriceListEditorPageComponent,
    detectChanges: false,
    overrideComponents: [
      [
        PriceListEditorPageComponent,
        {
          remove: { imports: [ProductPickerComponent] },
          add: { imports: [StubProductPickerComponent] }
        }
      ]
    ],
    providers: [
      provideRouter([]),
      { provide: PriceListsStore, useValue: store },
      {
        provide: PartnersStore,
        useValue: {
          $partners: signal([{ id: CUSTOMER, name: 'Acme Trading' }]),
          loadActive,
          list: { reset: jest.fn(), $data: partnersData }
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
    spectator
      .queryAll<HTMLButtonElement>('.price-list-editor-bar button')
      .find(button => button.textContent?.includes('Save'))!;
  const deleteButton = (): HTMLButtonElement | undefined =>
    spectator
      .queryAll<HTMLButtonElement>('.price-list-editor-bar button')
      .find(button => button.textContent?.includes('Delete list'));

  beforeEach(() => {
    detail.$data.set(undefined);
    detail.$operation.set(IDLE_OPERATION);
    detail.$isFailed.set(false);
    saveOperation.set(IDLE_OPERATION);
    deleteOperation.set(IDLE_OPERATION);
    partnersData.set(undefined);
    for (const mock of [
      detail.load,
      detail.reload,
      detail.reset,
      save.run,
      del.run,
      store.resetCommands,
      loadActive
    ]) {
      mock.mockClear();
    }
  });

  describe('a new list', () => {
    it('asks for a name in words, focus on it, and sends nothing', async () => {
      await render();

      spectator.click(saveButton());
      await spectator.fixture.whenStable();
      spectator.detectChanges();

      expect(save.run).not.toHaveBeenCalled();
      expect(document.activeElement?.getAttribute('aria-invalid')).toBe('true');
      expect(deleteButton()).toBeUndefined();
    });

    it('lists customers only, saves the whole body without a token and goes back to the list', async () => {
      await render();
      expect(loadActive).toHaveBeenCalledWith('Customer');
      const navigate = jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().form.patchValue({ name: ' Retail ', customerId: CUSTOMER });
      component().onProductPicked({ product: PRODUCT, available: null });
      component().items.at(0).patchValue({ price: 4.5 });
      spectator.detectChanges();

      spectator.click(saveButton());

      expect(save.run).toHaveBeenCalledWith({
        ref: undefined,
        body: {
          name: 'Retail',
          isDefault: false,
          customerId: CUSTOMER,
          items: [{ productId: 'p1', uomId: 'pcs', price: 4.5 }]
        }
      });
      expect(navigate).toHaveBeenCalledWith(['/', 'price-lists'], { replaceUrl: true });
      spectator.click(saveButton());
      expect(save.run).toHaveBeenCalledTimes(1);
      expect(component().hasUnsavedChanges()).toBe(false);
    });

    it('a price left empty, zero or with three decimals is said in words and stops the save', async () => {
      await render();
      component().form.patchValue({ name: 'Retail' });
      component().onProductPicked({ product: PRODUCT, available: null });
      spectator.detectChanges();

      for (const price of [null, 0, 1.234]) {
        component().items.at(0).patchValue({ price });
        spectator.click(saveButton());
        expect(save.run).not.toHaveBeenCalled();
      }
      expect(spectator.queryAll('[aria-invalid="true"]').length).toBeGreaterThan(0);
    });

    it('the same product and unit twice is one sentence in words, stops the save, and the second row is named', async () => {
      await render();
      component().form.patchValue({ name: 'Retail' });
      component().onProductPicked({ product: PRODUCT, available: null });
      component().items.push(createPriceItemGroup({ ...component().items.at(0).getRawValue() }));
      component().items.controls.forEach(group => group.patchValue({ price: 5 }));
      spectator.detectChanges();

      expect(text()).toContain('SKU-1 in PCS already has a price on this list (first on row 1).');
      spectator.click(saveButton());
      await spectator.fixture.whenStable();
      expect(save.run).not.toHaveBeenCalled();
      expect(document.activeElement?.classList.contains('price-list-editor-duplicate')).toBe(true);
    });

    it('picking a product already on the list adds nothing and says so; the cap of 2000 is said in words', async () => {
      await render();
      component().onProductPicked({ product: PRODUCT, available: null });
      component().onProductPicked({ product: PRODUCT, available: null });

      expect(component().items.length).toBe(1);
      expect(component().$lastAdded()).toBe(
        'SKU-1 already has a price in every unit it can be priced in.'
      );

      for (let n = 1; n < 2000; n++) {
        component().items.push(
          createPriceItemGroup({ ...component().items.at(0).getRawValue(), productId: `p${n}` })
        );
      }
      expect(component().$canAddItem()).toBe(false);
      component().onProductPicked({
        product: { ...PRODUCT, id: 'other' } as ProductDto,
        available: null
      });
      expect(component().items.length).toBe(2000);
      expect(component().$lastAdded()).toBe(
        'A price list holds up to 2000 prices. Remove one to add another.'
      );
    });

    it('the default flag clears and locks the customer and sends no customer', async () => {
      await render();
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().form.patchValue({ name: 'All', customerId: CUSTOMER });
      component().form.controls.isDefault.setValue(true);
      spectator.detectChanges();

      expect(component().form.controls.customerId.disabled).toBe(true);
      expect(component().form.controls.customerId.value).toBeNull();
      spectator.click(saveButton());
      expect(save.run).toHaveBeenCalledWith(
        expect.objectContaining({
          body: expect.objectContaining({ isDefault: true, customerId: undefined })
        })
      );
    });
  });

  describe('an existing list', () => {
    it('opens with its prices; editing only the name sends every field with the opened version (If-Match)', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      component().form.controls.name.setValue('Retail 2');
      component().form.controls.name.markAsDirty();

      spectator.click(saveButton());

      expect(save.run).toHaveBeenCalledWith({
        ref: { id: ID, concurrencyToken: 'AAAA' },
        body: {
          name: 'Retail 2',
          isDefault: false,
          customerId: CUSTOMER,
          items: [{ productId: 'p1', uomId: 'pcs', price: 5 }]
        }
      });
      expect(text()).toContain('SKU-1');
    });

    it('a 409 that names document_changed is one Reload banner and locks Save and Delete; Reload reads the list again', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      saveOperation.set(STALE());
      spectator.detectChanges();

      expect(text()).toContain('Retail changed since you opened it');
      expect(saveButton().disabled).toBe(true);
      expect(deleteButton()?.disabled).toBe(true);

      spectator.click(spectator.query('app-banner app-button button') as HTMLElement);
      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reload).toHaveBeenCalled();
    });

    it('shows the API field messages in words (the price at its field) and its own sentence for a refusal, such as a deactivated customer', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      saveOperation.set(
        failureOperation(
          apiError(400, API_ERROR_KIND.Validation, {
            fieldErrors: {
              'items[0].price': ['The price has too many decimals.'],
              'items[0]': ['The product is switched off.'],
              name: ['The name is taken.']
            }
          })
        )
      );
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(text()).toContain('The price has too many decimals.');
      expect(text()).toContain('SKU-1: The product is switched off.');
      expect(text()).toContain('The name is taken.');
      expect(document.activeElement?.getAttribute('aria-invalid')).toBe('true');

      saveOperation.set(
        failureOperation(
          apiError(409, API_ERROR_KIND.Conflict, {
            detail: 'The customer Acme Trading is not active.',
            message: 'General.'
          })
        )
      );
      spectator.detectChanges();
      expect(text()).toContain('The customer Acme Trading is not active.');
      expect(text()).not.toContain('General.');
    });

    it('delete asks first, sends the opened version and goes back to the list; a refusal is the API sentence', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      const navigate = jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      const confirm = TestBed.inject(ConfirmService);

      spectator.click(deleteButton() as HTMLElement);
      expect(confirm.$pending()?.request).toMatchObject({ destructive: true });
      expect(del.run).not.toHaveBeenCalled();
      confirm.cancel();
      expect(del.run).not.toHaveBeenCalled();
      spectator.detectChanges();

      spectator.click(deleteButton() as HTMLElement);
      confirm.confirm('');
      expect(del.run).toHaveBeenCalledWith({ id: ID, concurrencyToken: 'AAAA' });
      expect(navigate).toHaveBeenCalledWith(['/', 'price-lists'], { replaceUrl: true });

      deleteOperation.set(
        failureOperation(apiError(409, API_ERROR_KIND.Conflict, { detail: 'It is in use.' }))
      );
      spectator.detectChanges();
      expect(text()).toContain('It is in use.');
    });

    it('an unknown list is the not-found page, a failed load has Retry and a way out', async () => {
      detail.$operation.set(failureOperation(apiError(404, API_ERROR_KIND.NotFound)));
      detail.$isFailed.set(true);
      await render({ id: ID });
      expect(text()).toContain("This page or document doesn't exist or was removed.");

      detail.$operation.set(
        failureOperation(apiError(503, API_ERROR_KIND.Server, { message: 'Down.' }))
      );
      await render({ id: ID });
      expect(text()).toContain('Down.');
      expect(spectator.query('app-banner a')?.getAttribute('href')).toBe('/price-lists');
    });
  });

  describe('what the last list or the last user left in the store', () => {
    it('forgets the commands and the opened list every time it opens', async () => {
      await render();

      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reset).toHaveBeenCalled();
    });

    it('a stale 409 of list A does not show on list B opened next, and Save works there', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      saveOperation.set(STALE());
      spectator.detectChanges();
      expect(text()).toContain('changed since you opened it');
      store.resetCommands.mockClear();
      detail.reset.mockClear();

      detail.$data.set(list({ id: OTHER_ID, name: 'Other' }));
      spectator.setInput('id', OTHER_ID);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(store.resetCommands).toHaveBeenCalled();
      expect(detail.reset).toHaveBeenCalled();
      expect(text()).not.toContain('changed since you opened it');
      expect(saveButton().disabled).toBe(false);
    });

    it('a refusal (403) the last user left is gone when the page opens, and so is a failed delete', async () => {
      saveOperation.set(
        failureOperation(apiError(403, API_ERROR_KIND.Forbidden, { detail: 'Not for you.' }))
      );
      deleteOperation.set(
        failureOperation(apiError(409, API_ERROR_KIND.Conflict, { detail: 'Old refusal.' }))
      );

      await render();

      expect(text()).not.toContain('Not for you.');
      expect(text()).not.toContain('Old refusal.');
      expect(spectator.queryAll('app-banner')).toHaveLength(0);
    });

    it('list A is not left in the form when list B opens, also when B is not found', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      expect(component().items.length).toBe(1);

      detail.$data.set(undefined);
      spectator.setInput('id', OTHER_ID);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(component().items.length).toBe(0);
      expect(component().form.controls.name.value).toBe('');
    });
  });

  describe('a long list and the refusals of the API', () => {
    const many = (count: number): PriceListDto =>
      list({
        items: Array.from({ length: count }, (_, n) => ({
          productId: `p${n}`,
          sku: `SKU-${n}`,
          productName: `Product ${n}`,
          uomId: 'pcs',
          uomCode: 'PCS',
          price: 5
        }))
      } as unknown as Partial<PriceListDto>);

    it('2000 prices put 50 rows on screen, the rest behind the pager; the rows of the next page are the next 50', async () => {
      detail.$data.set(many(2000));
      await render({ id: ID });

      expect(component().items.length).toBe(2000);
      expect(spectator.queryAll('li.price-list-editor-item')).toHaveLength(50);
      expect(spectator.queryAll('li.price-list-editor-item strong')[0].textContent).toBe('SKU-0');
      expect(text()).toContain('Prices (2000)');

      component().onPageChange(2);
      spectator.detectChanges();
      expect(spectator.queryAll('li.price-list-editor-item')).toHaveLength(50);
      expect(spectator.queryAll('li.price-list-editor-item strong')[0].textContent).toBe('SKU-50');
    });

    it('typing a price builds the rows of the page again (50), never all 2000', async () => {
      detail.$data.set(many(2000));
      await render({ id: ID });
      const before = component().$rows();

      component().items.at(3).controls.price.setValue(9);

      expect(component().$rows()).toHaveLength(50);
      expect(component().$rows()[0].group).toBe(before[0].group);
    });

    it('an error on a price of another page takes the page with the price, the field has the message and the banner names the product', async () => {
      detail.$data.set(many(120));
      await render({ id: ID });
      saveOperation.set(
        failureOperation(
          apiError(400, API_ERROR_KIND.Validation, {
            fieldErrors: { 'items[103].price': ['The price has too many decimals.'] }
          })
        )
      );
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(component().$currentPage()).toBe(3);
      expect(text()).toContain('SKU-103: The price has too many decimals.');
      expect(document.activeElement?.getAttribute('aria-invalid')).toBe('true');
    });

    it('a refusal that names no field (a deactivated customer) is the alert, and it takes the focus away from Save', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      saveButton().focus();
      saveOperation.set(
        failureOperation(
          apiError(409, API_ERROR_KIND.Conflict, { detail: 'The customer is not active.' })
        )
      );
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(document.activeElement?.tagName).toBe('APP-BANNER');
      expect(document.activeElement?.textContent).toContain('The customer is not active.');
    });

    it('a product priced in its base unit gets its next unit when picked again, and is refused only when every unit has a price', async () => {
      await render();
      const packed = {
        ...PRODUCT,
        conversions: [{ fromUomId: 'ctn', toUomId: 'pcs', factor: 12 }]
      } as unknown as ProductDto;

      component().onProductPicked({ product: packed, available: null });
      component().onProductPicked({ product: packed, available: null });
      expect(component().items.controls.map(group => group.controls.uomId.value)).toEqual([
        'pcs',
        'ctn'
      ]);
      expect(component().$duplicates().size).toBe(0);

      component().onProductPicked({ product: packed, available: null });
      expect(component().items.length).toBe(2);
      expect(component().$lastAdded()).toBe(
        'SKU-1 already has a price in every unit it can be priced in.'
      );
    });

    it('a save in flight does not build the rows again from the answer, and Delete is off meanwhile', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
      const rows = component().$rows()[0].group;

      spectator.click(saveButton());
      detail.$data.set(list({ concurrencyToken: 'BBBB' }));
      spectator.detectChanges();

      expect(component().$rows()[0].group).toBe(rows);
      expect(deleteButton()?.disabled).toBe(true);
    });

    it('a second tap on Delete while the dialog is open asks nothing more', async () => {
      detail.$data.set(list());
      await render({ id: ID });
      const confirm = TestBed.inject(ConfirmService);
      const ask = jest.spyOn(confirm, 'ask');

      spectator.click(deleteButton() as HTMLElement);
      spectator.click(deleteButton() as HTMLElement);

      expect(ask).toHaveBeenCalledTimes(1);
      confirm.cancel();
    });

    it('the price field has a top, and the section is named by its heading', async () => {
      detail.$data.set(list());
      await render({ id: ID });

      expect(spectator.query('app-input[formcontrolname="price"] input')?.getAttribute('max')).toBe(
        '1000000000'
      );
      expect(spectator.query('section')?.getAttribute('aria-labelledby')).toBe(
        'price-list-items-heading'
      );
    });
  });

  describe('the customer picker', () => {
    it('does not cap silently: a truncated list says so and offers a search that asks the API', async () => {
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
  });

  it('asks before a reload or a closed tab only with unsaved changes', async () => {
    await render();
    const event = { preventDefault: jest.fn() } as unknown as BeforeUnloadEvent;
    component().onBeforeUnload(event);
    expect(event.preventDefault).not.toHaveBeenCalled();

    component().form.patchValue({ name: 'Retail' });
    component().form.markAsDirty();
    component().onBeforeUnload(event);
    expect(event.preventDefault).toHaveBeenCalled();
  });
});
