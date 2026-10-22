import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { ProductDto } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
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
  const createComponent = createComponentFactory({
    component: ProductEditPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([{ path: 'products/:id', children: [] }]),
      { provide: PermissionService, useValue: { has: () => true } },
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

  describe('the save request (the accounting fields are gone, the API leaves stored values alone)', () => {
    const sent = () => run.mock.calls[0][0].body as Record<string, unknown>;
    const TAX_KEYS = ['hsCode', 'sstCode'];

    beforeEach(() => run.mockReturnValue(of(PRODUCT)));

    it('an edit sends no tax code even when the API still returns one with the product', async () => {
      data.set(PRODUCT);
      spectator = createComponent({ props: { id: NINE } as never });
      await settle();
      spectator.component.form.controls.name.setValue('Green tea');

      spectator.component.onSave();

      expect(run).toHaveBeenCalledTimes(1);
      expect(sent()).toMatchObject({ name: 'Green tea', sku: 'TEA-1' });
      for (const key of TAX_KEYS) {
        expect(key in sent()).toBe(false);
      }
      expect(spectator.query('app-input[formControlName="hsCode"]')).toBeNull();
      expect(spectator.query('app-input[formControlName="sstCode"]')).toBeNull();
    });

    it('a new product sends no tax code either', async () => {
      spectator = createComponent();
      await settle();
      spectator.component.form.patchValue({ sku: 'NEW-1', name: 'New', baseUomId: UNIT });

      spectator.component.onSave();

      expect(run).toHaveBeenCalledTimes(1);
      expect(sent()).toMatchObject({ sku: 'NEW-1' });
      for (const key of TAX_KEYS) {
        expect(key in sent()).toBe(false);
      }
    });
  });
});
