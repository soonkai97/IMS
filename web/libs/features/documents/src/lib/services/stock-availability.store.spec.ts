import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { firstValueFrom, of, throwError } from 'rxjs';
import { StockClient } from '@ims/api';
import { API_STATUS } from '@ims/models';
import { StockAvailabilityStore } from './stock-availability.store';

describe('StockAvailabilityStore', () => {
  let spectator: SpectatorService<StockAvailabilityStore>;
  const createService = createServiceFactory({
    service: StockAvailabilityStore,
    providers: [mockProvider(StockClient)]
  });

  beforeEach(() => (spectator = createService()));

  const list = () => spectator.inject(StockClient).list as jest.Mock;

  describe('bySearch', () => {
    it('reads the first 100 rows of the warehouse for the search and maps product id to quantity on hand', async () => {
      list().mockReturnValue(
        of({
          items: [
            { productId: 'p1', qtyOnHand: 4 },
            { productId: 'p2', qtyOnHand: 0.5 }
          ]
        })
      );

      const stock = await firstValueFrom(
        spectator.service.bySearch.call({ warehouseId: 'w1', search: 'bolt' })
      );

      expect(list()).toHaveBeenCalledWith('w1', undefined, undefined, 'bolt', undefined, 1, 100);
      expect(stock).toEqual(
        new Map([
          ['p1', 4],
          ['p2', 0.5]
        ])
      );
    });

    it('leaves a product without a stock row out of the map (nothing on hand)', async () => {
      list().mockReturnValue(of({ items: [] }));

      const stock = await firstValueFrom(
        spectator.service.bySearch.call({ warehouseId: 'w1', search: '' })
      );

      expect(stock.get('p9')).toBeUndefined();
    });

    it('exposes loading and failure, and still raises the failure to the caller', async () => {
      const failure = new Error('down');
      list().mockReturnValue(throwError(() => failure));

      await expect(
        firstValueFrom(spectator.service.bySearch.call({ warehouseId: 'w1', search: 'x' }))
      ).rejects.toBe(failure);

      expect(spectator.service.bySearch.$operation().status).toBe(API_STATUS.Failure);
      expect(spectator.service.bySearch.$isFailed()).toBe(true);
    });
  });

  describe('forProduct', () => {
    it('reads one row of the product in the warehouse and answers its quantity on hand', async () => {
      list().mockReturnValue(of({ items: [{ productId: 'p1', qtyOnHand: 7 }] }));

      const qty = await firstValueFrom(
        spectator.service.forProduct.call({ warehouseId: 'w1', productId: 'p1' })
      );

      expect(list()).toHaveBeenCalledWith('w1', 'p1', undefined, undefined, undefined, 1, 1);
      expect(qty).toBe(7);
    });

    it('answers 0 when the product has no stock row', async () => {
      list().mockReturnValue(of({ items: [] }));

      expect(
        await firstValueFrom(
          spectator.service.forProduct.call({ warehouseId: 'w1', productId: 'p1' })
        )
      ).toBe(0);
    });

    it('exposes the failure and raises it to the caller', async () => {
      const failure = new Error('down');
      list().mockReturnValue(throwError(() => failure));

      await expect(
        firstValueFrom(spectator.service.forProduct.call({ warehouseId: 'w1', productId: 'p1' }))
      ).rejects.toBe(failure);

      expect(spectator.service.forProduct.$isFailed()).toBe(true);
    });
  });
});
