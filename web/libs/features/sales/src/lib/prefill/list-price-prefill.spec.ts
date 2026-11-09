import { Subject, of, throwError } from 'rxjs';
import { PriceLookupDto, PriceSource } from '@ims/api';
import {
  ListPricePrefill,
  PREFILL_CONCURRENCY,
  PREFILL_DEBOUNCE_MS,
  PrefillLine,
  samePrice
} from './list-price-prefill';

/** A line with a product, a unit and a price field the tests can read. */
function line(
  price: number | null = null,
  uomId = 'pcs'
): PrefillLine & { value: number | null; unit: string } {
  const state = { value: price, unit: uomId };
  return {
    productId: () => 'p1',
    uomId: () => state.unit,
    price: () => state.value,
    setPrice: next => {
      state.value = next;
    },
    get value() {
      return state.value;
    },
    set value(next: number | null) {
      state.value = next;
    },
    get unit() {
      return state.unit;
    },
    set unit(next: string) {
      state.unit = next;
    }
  };
}

const found = (price: number, source = PriceSource.Customer): PriceLookupDto => ({
  found: true,
  price,
  source
});
const nothing: PriceLookupDto = { found: false, price: undefined, source: undefined };

describe('ListPricePrefill', () => {
  let lookup: jest.Mock;
  let prefill: ListPricePrefill;
  const settle = async (ms = PREFILL_DEBOUNCE_MS): Promise<void> => {
    await jest.advanceTimersByTimeAsync(ms);
  };

  beforeEach(() => {
    jest.useFakeTimers();
    lookup = jest.fn(() => of(found(5)));
    prefill = new ListPricePrefill(lookup);
  });
  afterEach(() => {
    prefill.destroy();
    jest.useRealTimers();
  });

  it('asks after a quiet moment, once however often the line changed, and puts the list price in an empty field', async () => {
    const row = line();

    prefill.ask(row, 'c1');
    prefill.ask(row, 'c1');
    prefill.ask(row, 'c1');
    expect(lookup).not.toHaveBeenCalled();
    expect(prefill.$states().get(row)?.status).toBe('loading');
    await settle();

    expect(lookup).toHaveBeenCalledTimes(1);
    expect(lookup).toHaveBeenCalledWith('c1', 'p1', 'pcs');
    expect(row.value).toBe(5);
    expect(prefill.$states().get(row)).toMatchObject({ status: 'found', price: 5, applied: 5 });
  });

  it('never overwrites a price the person typed: the list price is shown beside it, and only the explicit action takes it', async () => {
    const row = line(7.5);

    prefill.ask(row, 'c1');
    await settle();

    expect(row.value).toBe(7.5);
    expect(prefill.$states().get(row)).toMatchObject({ status: 'found', price: 5 });
    expect(prefill.$states().get(row)?.applied).toBeUndefined();

    prefill.useListPrice(row);
    expect(row.value).toBe(5);
  });

  it('replaces the price it put there itself when the answer changes (another unit, another customer), but not one the person edited since', async () => {
    const row = line();
    prefill.ask(row, 'c1');
    await settle();
    expect(row.value).toBe(5);

    lookup.mockReturnValue(of(found(9, PriceSource.Default)));
    prefill.ask(row, 'c2');
    await settle();
    expect(row.value).toBe(9);

    row.value = 8.8;
    lookup.mockReturnValue(of(found(11)));
    prefill.ask(row, 'c3');
    await settle();
    expect(row.value).toBe(8.8);
    expect(prefill.$states().get(row)).toMatchObject({ price: 11 });
  });

  it('no list price (found false) is a quiet "none"; it clears a price of its own that no longer stands, and keeps a typed one', async () => {
    const own = line();
    const typed = line();
    prefill.ask(own, 'c1');
    prefill.ask(typed, 'c1');
    await settle();
    typed.value = 6;

    lookup.mockReturnValue(of(nothing));
    prefill.ask(own, 'c1');
    prefill.ask(typed, 'c1');
    await settle();

    expect(own.value).toBeNull();
    expect(typed.value).toBe(6);
    expect(prefill.$states().get(own)?.status).toBe('none');
    expect(prefill.$states().get(typed)?.status).toBe('none');
  });

  it('a failed lookup says "unavailable" and leaves the price as it is, so typing and saving go on', async () => {
    const row = line(3);
    lookup.mockReturnValue(throwError(() => new Error('down')));

    prefill.ask(row, 'c1');
    await settle();

    expect(prefill.$states().get(row)?.status).toBe('unavailable');
    expect(row.value).toBe(3);
  });

  it('the latest ask of a line wins: an older answer that arrives late is ignored', async () => {
    const row = line();
    const slow = new Subject<PriceLookupDto>();
    lookup.mockReturnValueOnce(slow).mockReturnValueOnce(of(found(9)));

    prefill.ask(row, 'c1');
    await settle();
    prefill.ask(row, 'c2');
    await settle();
    expect(row.value).toBe(9);

    slow.next(found(1));
    slow.complete();
    await settle(0);

    expect(row.value).toBe(9);
    expect(prefill.$states().get(row)).toMatchObject({ price: 9 });
  });

  it('every line asks again when the customer changes, each with its own product and unit', async () => {
    const a = line(null, 'pcs');
    const b = line(null, 'ctn');

    prefill.askAll([a, b], 'c1');
    await settle();
    lookup.mockClear();
    prefill.askAll([a, b], 'c2');
    await settle();

    expect(lookup.mock.calls).toEqual([
      ['c2', 'p1', 'pcs'],
      ['c2', 'p1', 'ctn']
    ]);
  });

  it('with no customer there is nothing to ask and nothing to show', async () => {
    const row = line();

    prefill.ask(row, null);
    await settle();

    expect(lookup).not.toHaveBeenCalled();
    expect(prefill.$states().get(row)).toBeUndefined();
  });

  it('waits for only a few answers at once', async () => {
    const waiting: Subject<PriceLookupDto>[] = [];
    lookup.mockImplementation(() => {
      const answer = new Subject<PriceLookupDto>();
      waiting.push(answer);
      return answer;
    });
    const rows = Array.from({ length: 10 }, () => line());

    prefill.askAll(rows, 'c1');
    await settle();
    expect(waiting).toHaveLength(PREFILL_CONCURRENCY);

    waiting[0].next(found(5));
    waiting[0].complete();
    await settle(0);
    expect(waiting).toHaveLength(PREFILL_CONCURRENCY + 1);
  });

  it('a reset forgets every state, and an answer on its way for the old order does not land', async () => {
    const row = line();
    const slow = new Subject<PriceLookupDto>();
    lookup.mockReturnValue(slow);
    prefill.ask(row, 'c1');
    await settle();

    prefill.reset();
    slow.next(found(5));
    slow.complete();
    await settle(0);

    expect(row.value).toBeNull();
    expect(prefill.$states().size).toBe(0);
  });

  it('0.00 is no price yet: it takes the list price, like an empty field', async () => {
    const row = line(0);

    prefill.ask(row, 'c1');
    await settle();

    expect(row.value).toBe(5);
  });

  it('says what it did to a price of its own: replaced by the new answer, or cleared when there is none', async () => {
    const row = line();
    prefill.ask(row, 'c1');
    await settle();
    expect(prefill.$states().get(row)?.change).toBeUndefined();

    lookup.mockReturnValue(of(found(9)));
    prefill.ask(row, 'c2');
    await settle();
    expect(prefill.$states().get(row)).toMatchObject({ change: 'replaced', price: 9 });

    lookup.mockReturnValue(of(nothing));
    prefill.ask(row, 'c3');
    await settle();
    expect(prefill.$states().get(row)).toMatchObject({ status: 'none', change: 'cleared' });
    expect(row.value).toBeNull();

    prefill.ask(row, 'c3');
    expect(prefill.$states().get(row)?.change).toBeUndefined();
  });

  it('cancelPending stops what waits and what is on its way: nothing lands afterwards, and what was known stays', async () => {
    const known = line();
    prefill.ask(known, 'c1');
    await settle();
    const waiting = line();
    const slow = new Subject<PriceLookupDto>();
    lookup.mockReturnValue(slow);
    prefill.ask(waiting, 'c1');
    await settle();
    const timed = line();
    prefill.ask(timed, 'c1');

    prefill.cancelPending();
    slow.next(found(1));
    slow.complete();
    await settle();

    expect(waiting.value).toBeNull();
    expect(timed.value).toBeNull();
    expect(known.value).toBe(5);
    expect(prefill.$states().get(known)?.status).toBe('found');
    expect(prefill.$states().has(waiting)).toBe(false);
    expect(prefill.$states().has(timed)).toBe(false);
  });

  it('two prices are the same when they are the same in money', () => {
    expect(samePrice(5, 5.004)).toBe(true);
    expect(samePrice(5, 5.01)).toBe(false);
    expect(samePrice(12.345, 12.35)).toBe(true);
  });

  it('a removed line is forgotten and a timer still waiting never asks', async () => {
    const row = line();
    prefill.ask(row, 'c1');

    prefill.forget(row);
    await settle();

    expect(lookup).not.toHaveBeenCalled();
    expect(prefill.$states().size).toBe(0);
  });
});
