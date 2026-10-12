import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { ProductsStore } from '@ims/features/catalog';
import { StockStore } from '@ims/features/stock';
import { ProductStockPageComponent } from './product-stock.component';

const PRODUCT_ID = 'abcdef01-2345-4678-89ab-cdef01234567';

describe('ProductStockPageComponent follows the URL', () => {
  let spectator: Spectator<ProductStockPageComponent>;
  const productLoad = jest.fn();
  const stockLoad = jest.fn();
  const ledgerLoad = jest.fn();
  const createComponent = createComponentFactory({
    component: ProductStockPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      {
        provide: ProductsStore,
        useValue: { detail: { $data: signal(undefined), load: productLoad } }
      },
      {
        provide: StockStore,
        useValue: {
          productStock: { $data: signal(undefined), load: stockLoad },
          ledger: {
            $data: signal(undefined),
            $isLoading: signal(false),
            $isFailed: signal(false),
            load: ledgerLoad,
            reload: jest.fn()
          }
        }
      }
    ]
  });

  async function settle(): Promise<void> {
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  beforeEach(() => {
    productLoad.mockClear();
    stockLoad.mockClear();
    ledgerLoad.mockClear();
  });

  it('loads the product, its stock and its ledger by the GUID in the URL, lower-cased', async () => {
    spectator = createComponent({ props: { productId: PRODUCT_ID.toUpperCase() } as never });
    await settle();

    expect(productLoad).toHaveBeenCalledWith(PRODUCT_ID);
    expect(stockLoad).toHaveBeenCalledWith(PRODUCT_ID);
    expect(ledgerLoad).toHaveBeenCalledWith(expect.objectContaining({ productId: PRODUCT_ID }));
  });

  it('an old numeric bookmark is a not-found page and makes no request', async () => {
    spectator = createComponent({ props: { productId: '123' } as never });
    await settle();

    expect(productLoad).not.toHaveBeenCalled();
    expect(stockLoad).not.toHaveBeenCalled();
    expect(ledgerLoad).not.toHaveBeenCalled();
    expect(spectator.query('h1')?.textContent).toContain('Page not found');
    expect(spectator.query('app-table')).toBeNull();
  });
});
