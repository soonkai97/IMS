import { HttpContext } from '@angular/common/http';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Subject, firstValueFrom, of } from 'rxjs';
import { PurchaseOrderDto, PurchaseOrderStatus, PurchaseOrdersClient } from '@ims/api';
import { IF_MATCH } from '@ims/core/http';
import { PurchaseOrdersStore, toPurchaseOrderRef } from './purchase-orders.store';

const ORDER_ID = '00000000-0000-4000-8000-000000000007';
const ORDER = { id: ORDER_ID, concurrencyToken: 'AAAA' } as PurchaseOrderDto;
const FRESH = { ...ORDER, concurrencyToken: 'BBBB' } as PurchaseOrderDto;

/** The context the store hands to the generated client; the If-Match interceptor turns it into the header. */
function tokenOf(context: unknown): string | undefined {
  return (context as HttpContext).get(IF_MATCH);
}

describe('PurchaseOrdersStore', () => {
  let spectator: SpectatorService<PurchaseOrdersStore>;
  const createService = createServiceFactory({
    service: PurchaseOrdersStore,
    providers: [mockProvider(PurchaseOrdersClient)]
  });
  const client = () => spectator.inject(PurchaseOrdersClient);

  beforeEach(() => {
    spectator = createService();
    for (const action of ['submit', 'recall', 'approve', 'reject', 'cancel', 'close']) {
      (client() as unknown as Record<string, jest.Mock>)[action].mockReturnValue(of(FRESH));
    }
  });

  it('lists with every filter in the order of the generated client', async () => {
    client().list.mockReturnValue(of({ items: [], page: 2, pageSize: 25, total: 0 }));

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

    expect(client().list.mock.calls[0]).toEqual([
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

  it('sends the version the user saw with every state change (If-Match)', async () => {
    const ref = toPurchaseOrderRef(ORDER);

    await firstValueFrom(spectator.service.submit.run(ref));
    await firstValueFrom(spectator.service.recall.run(ref));
    await firstValueFrom(spectator.service.approve.run(ref));
    await firstValueFrom(spectator.service.reject.run({ ...ref, reason: 'price is wrong' }));
    await firstValueFrom(spectator.service.cancel.run({ ...ref, reason: '' }));
    await firstValueFrom(spectator.service.close.run({ ...ref, reason: 'supplier stopped' }));

    expect(tokenOf(client().submit.mock.calls[0][1])).toBe('AAAA');
    expect(tokenOf(client().recall.mock.calls[0][1])).toBe('AAAA');
    expect(tokenOf(client().approve.mock.calls[0][1])).toBe('AAAA');
    expect(tokenOf(client().reject.mock.calls[0][2])).toBe('AAAA');
    expect(tokenOf(client().cancel.mock.calls[0][2])).toBe('AAAA');
    expect(tokenOf(client().close.mock.calls[0][2])).toBe('AAAA');
    expect(client().reject.mock.calls[0][1]).toEqual({ reason: 'price is wrong' });
    expect(client().close.mock.calls[0][1]).toEqual({ reason: 'supplier stopped' });
  });

  it('cancels without a reason as an absent one, not an empty string', async () => {
    await firstValueFrom(
      spectator.service.cancel.run({ ...toPurchaseOrderRef(ORDER), reason: '' })
    );

    expect(client().cancel.mock.calls[0][1]).toEqual({ reason: undefined });
  });

  it('keeps the answer as the opened order (the new token)', async () => {
    spectator.service.detail.$data.set(ORDER);

    await firstValueFrom(spectator.service.approve.run(toPurchaseOrderRef(ORDER)));

    expect(spectator.service.detail.$data()?.concurrencyToken).toBe('BBBB');
  });

  it('is exclusive: a second tap while a command runs sends nothing', async () => {
    const answer = new Subject<PurchaseOrderDto>();
    client().submit.mockReturnValue(answer);
    const ref = toPurchaseOrderRef(ORDER);

    const first = firstValueFrom(spectator.service.submit.run(ref));
    spectator.service.submit.run(ref).subscribe();
    answer.next(FRESH);
    answer.complete();
    await first;

    expect(client().submit).toHaveBeenCalledTimes(1);
  });
});
