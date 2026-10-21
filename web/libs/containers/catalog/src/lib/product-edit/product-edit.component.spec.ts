import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { ProductDto } from '@ims/api';
import { ModulesStore, PermissionService } from '@ims/core/identity';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import { ApiError } from '@ims/models';
import { ProductEditPageComponent } from './product-edit.component';

const NINE = '00000000-0000-4000-8000-000000000009';
const EIGHT = '00000000-0000-4000-8000-000000000008';
const UNIT = '00000000-0000-4000-8000-0000000000aa';

const PRODUCT: ProductDto = {
  id: NINE,
  sku: 'TEA-1',
  name: 'Tea',
  categoryId: undefined,
  categoryName: undefined,
  baseUomId: UNIT,
  baseUomCode: 'PCS',
  safetyStock: 5,
  isActive: true,
  barcodes: ['9501234'],
  conversions: [],
  hsCode: '0901.21',
  sstCode: 'ST-01'
};

describe('ProductEditPageComponent follows the URL (EA-20)', () => {
  let spectator: Spectator<ProductEditPageComponent>;
  const load = jest.fn();
  const reset = jest.fn();
  const data = signal<ProductDto | undefined>(undefined);
  const run = jest.fn();
  const operation = signal<{ error?: Partial<ApiError> }>({});
  // Like the real command: forgetting it empties its outcome.
  const saveReset = jest.fn(() => operation.set({}));
  let accountingOn = true;
  const moduleTick = signal(0);
  /** The module list of the screen changes (an Admin switched the module, the list was read again). */
  const setModule = (on: boolean) => {
    accountingOn = on;
    moduleTick.update(count => count + 1);
  };
  const createComponent = createComponentFactory({
    component: ProductEditPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([{ path: 'products/:id', children: [] }]),
      { provide: PermissionService, useValue: { has: () => true } },
      {
        provide: ModulesStore,
        useValue: {
          isEnabled: () => {
            moduleTick();
            return accountingOn;
          }
        }
      },
      {
        provide: CatalogStore,
        useValue: { ensureLoaded: jest.fn(), $units: signal([]), $categories: signal([]) }
      },
      {
        provide: ProductsStore,
        useValue: {
          detail: { $data: data, $isLoading: signal(false), load, reset },
          save: { $isRunning: signal(false), $operation: operation, run, reset: saveReset }
        }
      }
    ]
  });

  async function settle(): Promise<void> {
    spectator.detectChanges();
    await spectator.fixture.whenStable();
  }

  beforeEach(() => {
    load.mockClear();
    reset.mockClear();
    run.mockReset();
    saveReset.mockClear();
    operation.set({});
    data.set(undefined);
    setModule(true);
  });

  it('loads the product named in the URL', async () => {
    spectator = createComponent({ props: { id: NINE } as never });
    await settle();

    expect(load).toHaveBeenCalledWith(NINE);
  });

  it('loads the other product when the page is reused for a new id (Back/Forward between products)', async () => {
    spectator = createComponent({ props: { id: NINE } as never });
    await settle();

    spectator.fixture.componentRef.setInput('id', EIGHT);
    await settle();

    expect(load).toHaveBeenLastCalledWith(EIGHT);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('what the last product (or the last user) failed to save with is gone when another product opens', async () => {
    operation.set({
      error: { status: 400, fieldErrors: { sku: ['SKU TEA-1 is already used.'] } }
    });

    spectator = createComponent({ props: { id: EIGHT } as never });
    await settle();

    expect(saveReset).toHaveBeenCalled();
    expect(spectator.element.textContent).not.toContain('is already used.');
  });

  it('an old numeric bookmark is a not-found page: no request, no form, nothing to save', async () => {
    spectator = createComponent({ props: { id: '123' } as never });
    await settle();

    expect(load).not.toHaveBeenCalled();
    expect(spectator.query('h1')?.textContent).toContain('Page not found');
    expect(spectator.query('form')).toBeNull();
    expect(spectator.queryAll('button').length).toBe(0);
  });

  it('a new product starts empty instead of loading anything', async () => {
    spectator = createComponent();
    await settle();

    expect(reset).toHaveBeenCalled();
    expect(load).not.toHaveBeenCalled();
  });

  it('QA edge 9: Save with an empty SKU moves the focus to the SKU field, not on Save', async () => {
    spectator = createComponent();
    await settle();

    spectator.component.onSave();
    await settle();

    const sku = spectator.query('app-input[formControlName="sku"] input');
    expect(sku?.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(sku);
  });

  it('a product name takes up to the 300 characters the API accepts', async () => {
    spectator = createComponent();
    await settle();

    expect(
      spectator.query('app-input[formControlName="name"] input')?.getAttribute('maxlength')
    ).toBe('300');
  });

  describe('tax codes of the accounting module (PL1.8)', () => {
    const input = (name: string) =>
      spectator.query<HTMLInputElement>(`app-input[formControlName="${name}"] input`);
    const text = () => spectator.element.textContent?.replace(/\s+/g, ' ') ?? '';

    async function openLoaded(product: ProductDto = PRODUCT): Promise<void> {
      data.set(product);
      spectator = createComponent({ props: { id: NINE } as never });
      await settle();
    }

    const sent = () => run.mock.calls[0][0].body as Record<string, unknown>;

    beforeEach(() => run.mockReturnValue(of(PRODUCT)));

    it('shows both codes with the value they loaded while the module is on', async () => {
      await openLoaded();

      expect(input('hsCode')?.value).toBe('0901.21');
      expect(input('sstCode')?.value).toBe('ST-01');
      expect(text()).toContain('Type it as text, for example 0901.21.');
    });

    it('ROUND TRIP: editing only the name sends both codes exactly as they were loaded', async () => {
      await openLoaded();

      spectator.component.form.controls.name.setValue('Green tea');
      spectator.component.onSave();

      expect(run).toHaveBeenCalledTimes(1);
      expect(run.mock.calls[0][0].id).toBe(NINE);
      expect(sent()).toMatchObject({ name: 'Green tea', hsCode: '0901.21', sstCode: 'ST-01' });
    });

    it('sends both keys even for a product without codes (a save replaces them, so a missing key would clear)', async () => {
      await openLoaded({ ...PRODUCT, hsCode: undefined, sstCode: undefined });

      spectator.component.onSave();

      expect(sent()['hsCode']).toBe('');
      expect(sent()['sstCode']).toBe('');
    });

    it('a code that was emptied goes as empty text, which clears it; the other one is kept', async () => {
      await openLoaded();

      spectator.component.form.controls.hsCode.setValue('   ');
      spectator.component.onSave();

      expect(sent()['hsCode']).toBe('');
      expect(sent()['sstCode']).toBe('ST-01');
    });

    it('sends what the server would store: the value without the spaces at its ends, the case as typed', async () => {
      await openLoaded();

      spectator.component.form.controls.sstCode.setValue('  st-01a ');
      spectator.component.onSave();

      expect(sent()['sstCode']).toBe('st-01a');
    });

    it('a new product also sends the two codes (empty), so nothing depends on the module list being fresh', async () => {
      spectator = createComponent();
      await settle();
      spectator.component.form.patchValue({ sku: 'NEW-1', name: 'New', baseUomId: UNIT });

      spectator.component.onSave();

      expect(sent()).toMatchObject({ sku: 'NEW-1', hsCode: '', sstCode: '' });
    });

    it('a wrong code is refused before anything is sent: the sentence names the rule and the focus goes to the field', async () => {
      await openLoaded();

      spectator.component.form.controls.hsCode.setValue('0901-21');
      spectator.component.onSave();
      await settle();

      expect(run).not.toHaveBeenCalled();
      expect(text()).toContain('HS code may contain only digits and dots, up to 16 characters.');
      expect(input('hsCode')?.getAttribute('aria-invalid')).toBe('true');
      expect(document.activeElement).toBe(input('hsCode'));
    });

    it('a code of 17 characters is refused, 16 are fine, and spaces at the ends do not count', async () => {
      await openLoaded();
      const { hsCode } = spectator.component.form.controls;

      hsCode.setValue('1'.repeat(17));
      expect(hsCode.invalid).toBe(true);
      hsCode.setValue(` ${'1'.repeat(16)} `);
      expect(hsCode.valid).toBe(true);
    });

    it('shows the sentence of the server at the field it names', async () => {
      await openLoaded();
      operation.set({
        error: {
          status: 400,
          fieldErrors: { sstCode: ['SST code may contain only letters, digits and hyphens.'] }
        }
      });
      spectator.detectChanges();
      await settle();

      expect(text()).toContain('SST code may contain only letters, digits and hyphens.');
    });

    it('the module off: no code is shown and none is sent (the server would answer 400 to a value)', async () => {
      accountingOn = false;
      await openLoaded({ ...PRODUCT, hsCode: undefined, sstCode: undefined });

      spectator.component.onSave();

      expect(input('hsCode')).toBeNull();
      expect(input('sstCode')).toBeNull();
      expect(run).toHaveBeenCalledTimes(1);
      expect('hsCode' in sent()).toBe(false);
      expect('sstCode' in sent()).toBe(false);
    });

    it('a module list that is older than the product (another tab turned the module on): the codes the server just showed are kept, not cleared', async () => {
      accountingOn = false;
      await openLoaded();

      spectator.component.form.controls.name.setValue('Green tea');
      spectator.component.onSave();

      expect(input('hsCode')?.value).toBe('0901.21');
      expect(sent()).toMatchObject({ hsCode: '0901.21', sstCode: 'ST-01' });
    });

    it('DATA LOSS: loaded while the module was OFF and saved after it was turned ON, nothing is sent and the product is read again', async () => {
      setModule(false);
      await openLoaded({ ...PRODUCT, hsCode: undefined, sstCode: undefined });
      load.mockClear();
      setModule(true);

      spectator.component.form.controls.name.setValue('Green tea');
      spectator.component.onSave();
      await settle();

      expect(run).not.toHaveBeenCalled();
      expect(load).toHaveBeenCalledTimes(1);
      expect(load).toHaveBeenCalledWith(NINE);
      expect(text()).toContain('Accounting was turned on after this was opened.');

      // The product comes back with its codes: the form shows them and a save sends them.
      data.set({ ...PRODUCT });
      await settle();
      expect(input('hsCode')?.value).toBe('0901.21');
      spectator.component.onSave();
      expect(sent()).toMatchObject({ hsCode: '0901.21', sstCode: 'ST-01' });
    });

    it('a product loaded while the module was ON saves at once (no reload), also when it has no codes', async () => {
      await openLoaded({ ...PRODUCT, hsCode: undefined, sstCode: undefined });
      load.mockClear();

      spectator.component.onSave();

      expect(run).toHaveBeenCalledTimes(1);
      expect(load).not.toHaveBeenCalled();
    });
  });
});
