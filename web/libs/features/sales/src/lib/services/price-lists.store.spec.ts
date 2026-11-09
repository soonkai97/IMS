import { HttpContext } from '@angular/common/http';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { firstValueFrom, of, throwError } from 'rxjs';
import { PriceListDto, PriceListsClient } from '@ims/api';
import { HANDLED_INLINE, IF_MATCH } from '@ims/core/http';
import { HTTP_STATUS } from '@ims/models';
import { PriceListsStore, toPriceListRef } from './price-lists.store';

const ID = '00000000-0000-4000-8000-000000000007';
const LIST = { id: ID, concurrencyToken: 'AAAA' } as PriceListDto;
const FRESH = { ...LIST, concurrencyToken: 'BBBB' } as PriceListDto;
const BODY = { name: 'Retail', isDefault: true, customerId: undefined, items: [] };

const tokenOf = (context: unknown): string | undefined => (context as HttpContext).get(IF_MATCH);
const shownByPage = (context: unknown): readonly number[] =>
  (context as HttpContext).get(HANDLED_INLINE) as readonly number[];

describe('PriceListsStore', () => {
  let spectator: SpectatorService<PriceListsStore>;
  const createService = createServiceFactory({
    service: PriceListsStore,
    providers: [mockProvider(PriceListsClient)]
  });
  const client = () => spectator.inject(PriceListsClient);

  beforeEach(() => {
    spectator = createService();
    client().create.mockReturnValue(of(FRESH));
    client().update.mockReturnValue(of(FRESH));
    client().delete.mockReturnValue(of(undefined));
  });

  it('lists with the search, the page and the page size in the order of the generated client', async () => {
    client().list.mockReturnValue(of({ items: [], page: 2, pageSize: 25, total: 0 }));

    spectator.service.list.load({ search: 'Ret', page: 2, pageSize: 25 });
    await Promise.resolve();

    expect(client().list.mock.calls[0]).toEqual(['Ret', 2, 25]);
  });

  it('creates a list without a token and replaces one with the version the person opened (If-Match); a refusal is the page alert', async () => {
    await firstValueFrom(spectator.service.save.run({ body: BODY }));
    await firstValueFrom(spectator.service.save.run({ ref: toPriceListRef(LIST), body: BODY }));

    expect(client().create.mock.calls[0][0]).toEqual(BODY);
    expect(tokenOf(client().create.mock.calls[0][1])).toBeUndefined();
    expect(client().update.mock.calls[0][0]).toBe(ID);
    expect(tokenOf(client().update.mock.calls[0][2])).toBe('AAAA');
    expect(shownByPage(client().update.mock.calls[0][2])).toEqual([
      HTTP_STATUS.Forbidden,
      HTTP_STATUS.Conflict
    ]);
  });

  it('deletes with the version the person saw, a refusal being the page alert', async () => {
    await firstValueFrom(spectator.service.delete.run(toPriceListRef(LIST)));

    expect(client().delete.mock.calls[0][0]).toBe(ID);
    expect(tokenOf(client().delete.mock.calls[0][1])).toBe('AAAA');
    expect(shownByPage(client().delete.mock.calls[0][1])).toContain(HTTP_STATUS.Conflict);
  });

  it('keeps the answer of a save as the opened list, and not when another list is open', async () => {
    spectator.service.detail.$data.set(LIST);
    await firstValueFrom(spectator.service.save.run({ ref: toPriceListRef(LIST), body: BODY }));
    expect(spectator.service.detail.$data()?.concurrencyToken).toBe('BBBB');

    spectator.service.detail.$data.set({ ...LIST, id: 'other' });
    await firstValueFrom(spectator.service.save.run({ ref: toPriceListRef(LIST), body: BODY }));
    expect(spectator.service.detail.$data()?.id).toBe('other');
  });

  it('forgets the outcome of every command', async () => {
    const refusal = {
      isApiError: true,
      kind: 'conflict',
      status: 409,
      title: 't',
      detail: 'x',
      message: 'x',
      fieldErrors: {},
      extensions: {}
    };
    client().delete.mockReturnValue(throwError(() => refusal));
    spectator.service.delete.run(toPriceListRef(LIST)).subscribe({ error: () => undefined });
    await Promise.resolve();
    expect(spectator.service.delete.$operation().error).toBeDefined();

    spectator.service.resetCommands();

    expect(spectator.service.delete.$operation().error).toBeUndefined();
  });
});
