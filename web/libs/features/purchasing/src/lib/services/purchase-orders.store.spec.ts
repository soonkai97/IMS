import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { PurchaseOrderStatus, PurchaseOrdersClient } from '@ims/api';
import { PurchaseOrdersStore } from './purchase-orders.store';

describe('PurchaseOrdersStore', () => {
  let spectator: SpectatorService<PurchaseOrdersStore>;
  const createService = createServiceFactory({
    service: PurchaseOrdersStore,
    providers: [mockProvider(PurchaseOrdersClient)]
  });

  it('lists with every filter in the order of the generated client', async () => {
    spectator = createService();
    const client = spectator.inject(PurchaseOrdersClient);
    client.list.mockReturnValue(of({ items: [], page: 2, pageSize: 25, total: 0 }));

    spectator.service.list.load({
      status: PurchaseOrderStatus.Submitted,
      supplierId: 's1',
      warehouseId: 'w1',
      from: '2026-10-01',
      to: '2026-10-31',
      search: '0100',
      page: 2,
      pageSize: 25
    });
    await Promise.resolve();

    expect(client.list.mock.calls[0]).toEqual([
      PurchaseOrderStatus.Submitted,
      's1',
      'w1',
      '2026-10-01',
      '2026-10-31',
      '0100',
      2,
      25
    ]);
  });
});
