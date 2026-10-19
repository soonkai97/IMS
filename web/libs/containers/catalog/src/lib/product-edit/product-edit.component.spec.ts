import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { ProductDto } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import { IDLE_OPERATION } from '@ims/models';
import { ProductEditPageComponent } from './product-edit.component';

const NINE = '00000000-0000-4000-8000-000000000009';
const EIGHT = '00000000-0000-4000-8000-000000000008';

describe('ProductEditPageComponent follows the URL (EA-20)', () => {
  let spectator: Spectator<ProductEditPageComponent>;
  const load = jest.fn();
  const reset = jest.fn();
  const data = signal<ProductDto | undefined>(undefined);
  const createComponent = createComponentFactory({
    component: ProductEditPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      { provide: PermissionService, useValue: { has: () => true } },
      {
        provide: CatalogStore,
        useValue: { ensureLoaded: jest.fn(), $units: signal([]), $categories: signal([]) }
      },
      {
        provide: ProductsStore,
        useValue: {
          detail: { $data: data, $isLoading: signal(false), load, reset },
          save: { $isRunning: signal(false), $operation: signal(IDLE_OPERATION), run: jest.fn() }
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
});
