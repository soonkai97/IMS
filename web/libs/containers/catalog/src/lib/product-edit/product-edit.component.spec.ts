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

  it('a new product starts empty instead of loading anything', async () => {
    spectator = createComponent();
    await settle();

    expect(reset).toHaveBeenCalled();
    expect(load).not.toHaveBeenCalled();
  });
});
