import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Subject, of } from 'rxjs';
import { PagedResultOfReorderSuggestion, PurchaseOrdersClient } from '@ims/api';
import { ReorderStore } from './reorder.store';

const page = (total: number): PagedResultOfReorderSuggestion => ({
  items: [],
  page: 1,
  pageSize: 25,
  total
});

describe('ReorderStore', () => {
  let spectator: SpectatorService<ReorderStore>;
  const createService = createServiceFactory({
    service: ReorderStore,
    providers: [mockProvider(PurchaseOrdersClient)]
  });
  const client = () => spectator.inject(PurchaseOrdersClient);

  beforeEach(() => {
    spectator = createService();
  });

  it('sends the warehouse, search and paging in the order of the generated client', () => {
    client().suggestions.mockReturnValue(of(page(0)));

    spectator.service.suggestions.load({
      warehouseId: 'w1',
      search: 'bolt',
      page: 2,
      pageSize: 25
    });

    expect(client().suggestions).toHaveBeenCalledWith('w1', 'bolt', 2, 25);
  });

  it('keeps only the answer to the last request: a slow earlier warehouse never overwrites it', () => {
    const slow = new Subject<PagedResultOfReorderSuggestion>();
    client()
      .suggestions.mockReturnValueOnce(slow)
      .mockReturnValueOnce(of(page(2)));

    spectator.service.suggestions.load({ warehouseId: 'w1', page: 1, pageSize: 25 });
    spectator.service.suggestions.load({ warehouseId: 'w2', page: 1, pageSize: 25 });
    slow.next(page(99));

    expect(spectator.service.suggestions.$data()?.total).toBe(2);
  });

  it('offers the remembered warehouse back to the same user only', () => {
    spectator.service.remember('u1', 'w1');

    expect(spectator.service.lastWarehouse('u1')).toBe('w1');
    expect(spectator.service.lastWarehouse('u2')).toBeNull();
    expect(spectator.service.lastWarehouse(undefined)).toBeNull();
  });
});
