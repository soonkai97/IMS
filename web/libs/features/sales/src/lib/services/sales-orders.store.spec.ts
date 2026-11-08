import { HttpContext } from '@angular/common/http';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { firstValueFrom, of } from 'rxjs';
import { SalesOrderDto, SalesOrderStatus, SalesOrdersClient } from '@ims/api';
import { IF_MATCH } from '@ims/core/http';
import { SO_ACTION, toSoActions } from '../constants/sales.const';
import { SalesOrdersStore, toSalesOrderRef } from './sales-orders.store';

const ORDER_ID = '00000000-0000-4000-8000-000000000007';
const ORDER = { id: ORDER_ID, concurrencyToken: 'AAAA' } as SalesOrderDto;
const FRESH = { ...ORDER, concurrencyToken: 'BBBB' } as SalesOrderDto;

/** The context the store hands to the generated client; the If-Match interceptor turns it into the header. */
const tokenOf = (context: unknown): string | undefined => (context as HttpContext).get(IF_MATCH);

describe('SalesOrdersStore', () => {
  let spectator: SpectatorService<SalesOrdersStore>;
  const createService = createServiceFactory({
    service: SalesOrdersStore,
    providers: [mockProvider(SalesOrdersClient)]
  });
  const client = () => spectator.inject(SalesOrdersClient);

  beforeEach(() => {
    spectator = createService();
    for (const action of [
      'create',
      'update',
      'submit',
      'recall',
      'approve',
      'reject',
      'cancel',
      'close'
    ]) {
      (client() as unknown as Record<string, jest.Mock>)[action].mockReturnValue(of(FRESH));
    }
  });

  it('lists with the filters in the order of the generated client', async () => {
    client().list.mockReturnValue(of({ items: [], page: 2, pageSize: 25, total: 0 }));

    spectator.service.list.load({
      status: SalesOrderStatus.Submitted,
      customerId: 'c1',
      search: '0100',
      page: 2,
      pageSize: 25
    });
    await Promise.resolve();

    expect(client().list.mock.calls[0]).toEqual([
      SalesOrderStatus.Submitted,
      'c1',
      undefined,
      undefined,
      undefined,
      '0100',
      2,
      25
    ]);
  });

  it('sends the version the user saw with every state change (If-Match)', async () => {
    const ref = toSalesOrderRef(ORDER);

    await firstValueFrom(spectator.service.submit.run(ref));
    await firstValueFrom(spectator.service.recall.run(ref));
    await firstValueFrom(spectator.service.approve.run(ref));
    await firstValueFrom(spectator.service.reject.run({ ...ref, reason: 'price is wrong' }));
    await firstValueFrom(spectator.service.cancel.run({ ...ref, reason: '' }));
    await firstValueFrom(spectator.service.close.run({ ...ref, reason: 'customer stopped' }));

    expect(tokenOf(client().submit.mock.calls[0][1])).toBe('AAAA');
    expect(tokenOf(client().recall.mock.calls[0][1])).toBe('AAAA');
    expect(tokenOf(client().approve.mock.calls[0][1])).toBe('AAAA');
    expect(client().reject.mock.calls[0][1]).toEqual({ reason: 'price is wrong' });
    expect(tokenOf(client().reject.mock.calls[0][2])).toBe('AAAA');
    expect(client().cancel.mock.calls[0][1]).toEqual({ reason: undefined });
    expect(tokenOf(client().cancel.mock.calls[0][2])).toBe('AAAA');
    expect(client().close.mock.calls[0][1]).toEqual({ reason: 'customer stopped' });
    expect(tokenOf(client().close.mock.calls[0][2])).toBe('AAAA');
  });

  it('saves a new draft without a token and an existing one with the version the person opened', async () => {
    const body = { warehouseId: 'w1', customerId: 'c1', remark: undefined, lines: [] };

    await firstValueFrom(spectator.service.save.run({ body }));
    await firstValueFrom(spectator.service.save.run({ ref: toSalesOrderRef(ORDER), body }));

    expect(client().create.mock.calls[0][0]).toEqual(body);
    expect(tokenOf(client().create.mock.calls[0][1])).toBeUndefined();
    expect(client().update.mock.calls[0][0]).toBe(ORDER_ID);
    expect(tokenOf(client().update.mock.calls[0][2])).toBe('AAAA');
  });

  it('keeps the answer as the opened order and drops the other commands old errors', async () => {
    spectator.service.detail.$data.set(ORDER);

    await firstValueFrom(spectator.service.approve.run(toSalesOrderRef(ORDER)));

    expect(spectator.service.detail.$data()?.concurrencyToken).toBe('BBBB');
  });

  it('leaves another order alone when the user opened it while the call ran', async () => {
    spectator.service.detail.$data.set({ ...ORDER, id: 'other' });

    await firstValueFrom(spectator.service.approve.run(toSalesOrderRef(ORDER)));

    expect(spectator.service.detail.$data()?.id).toBe('other');
  });
});

describe('toSoActions', () => {
  it('keeps the names this web knows and ignores the rest', () => {
    expect([...toSoActions(['submit', 'teleport', 'close'])]).toEqual([
      SO_ACTION.Submit,
      SO_ACTION.Close
    ]);
    expect(toSoActions(undefined).size).toBe(0);
  });
});
