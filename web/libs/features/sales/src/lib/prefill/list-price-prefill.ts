import { Signal, signal } from '@angular/core';
import { EMPTY, Observable, Subject, Subscription, catchError, map, mergeMap, of } from 'rxjs';
import { PriceLookupDto, PriceSource } from '@ims/api';
import { PublicId } from '@ims/models';

/** What an order line says about its own product and unit, and the field the list price goes into. */
export interface PrefillLine {
  readonly productId: () => PublicId;
  readonly uomId: () => PublicId;
  readonly price: () => number | null;
  /** Writes the price into the line (the person's field); the line is then changed by them as if they had typed it. */
  readonly setPrice: (price: number | null) => void;
}

export type ListPriceStatus = 'loading' | 'found' | 'none' | 'unavailable';

/** The list price of one line as the last answer left it. */
export interface ListPriceState {
  readonly status: ListPriceStatus;
  readonly price?: number;
  readonly source?: PriceSource;
  /** The list price this class itself put in the field and the person has not changed since; undefined when none. */
  readonly applied?: number;
  /** What this answer did to a price this class had put in earlier (another unit, another customer): replaced it, or cleared it. */
  readonly change?: 'replaced' | 'cleared';
}

/** Two prices are the same when they are the same in money (2 decimals): the words never say "different" about identical text. */
export const samePrice = (a: number, b: number): boolean =>
  Math.round(a * 100) === Math.round(b * 100);

export type PriceLookup = (
  customerId: PublicId,
  productId: PublicId,
  uomId: PublicId
) => Observable<PriceLookupDto>;

/** How long a line waits after its last change before it asks, and how many answers are awaited at once. */
export const PREFILL_DEBOUNCE_MS = 250;
export const PREFILL_CONCURRENCY = 4;

interface Job {
  readonly line: PrefillLine;
  readonly ticket: number;
  readonly customerId: PublicId;
}

/**
 * Prefills the price of order lines from the price list lookup, and keeps it honest.
 *
 * - Asked for a line when it gets a product and a unit, when its unit changes, for EVERY line when the customer changes, and after
 *   the order is read again, so the field never silently disagrees with what the server decides at save.
 * - A price the person typed is never overwritten: only an empty field, or the very price this class put there, is replaced. A
 *   typed price differing from the list price shows the list price beside it, and the person may take it (`useListPrice`).
 * - A slow or failed lookup blocks nothing (the state says "unavailable"); the latest ask per line wins, an older answer is
 *   ignored; a line asks only after it was quiet for a moment, and a few answers are awaited at once.
 * - `reset()` forgets everything (another order opened, another user): no answer for the old order can land.
 */
export class ListPricePrefill {
  private readonly states = signal<ReadonlyMap<PrefillLine, ListPriceState>>(new Map());
  private readonly tickets = new Map<PrefillLine, number>();
  private readonly timers = new Map<PrefillLine, ReturnType<typeof setTimeout>>();
  private readonly queue = new Subject<Job>();
  private readonly running: Subscription;
  private generation = 0;

  constructor(private readonly lookup: PriceLookup) {
    this.running = this.queue.pipe(mergeMap(job => this.run(job), PREFILL_CONCURRENCY)).subscribe();
  }

  /** The state of every line, by line. */
  get $states(): Signal<ReadonlyMap<PrefillLine, ListPriceState>> {
    return this.states;
  }

  /** Asks the lookup for this line (after a quiet moment); with no customer there is nothing to ask. */
  ask(line: PrefillLine, customerId: PublicId | null): void {
    const timer = this.timers.get(line);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.timers.delete(line);
    }
    const ticket = (this.tickets.get(line) ?? 0) + 1;
    this.tickets.set(line, ticket);
    if (customerId === null) {
      this.drop(line);
      return;
    }
    this.set(line, { ...this.states().get(line), status: 'loading', change: undefined });
    this.timers.set(
      line,
      setTimeout(() => {
        this.timers.delete(line);
        this.queue.next({ line, ticket, customerId });
      }, PREFILL_DEBOUNCE_MS)
    );
  }

  askAll(lines: readonly PrefillLine[], customerId: PublicId | null): void {
    for (const line of lines) {
      this.ask(line, customerId);
    }
  }

  /** The explicit action: takes the list price into the field, over whatever is there. */
  useListPrice(line: PrefillLine): void {
    const state = this.states().get(line);
    if (state?.status === 'found' && state.price !== undefined) {
      line.setPrice(state.price);
      this.set(line, { ...state, applied: state.price });
    }
  }

  /** A line was removed: its answer, timer and ticket go with it. */
  forget(line: PrefillLine): void {
    const timer = this.timers.get(line);
    if (timer !== undefined) {
      clearTimeout(timer);
    }
    this.timers.delete(line);
    this.tickets.delete(line);
    this.drop(line);
  }

  /**
   * Nothing waiting or on its way may land any more (a save has sent the price as it is). What is already known stays; a line that
   * was only waiting for its first answer shows nothing. Ask again afterwards if the screen goes on.
   */
  cancelPending(): void {
    this.timers.forEach(timer => clearTimeout(timer));
    this.timers.clear();
    for (const line of [...this.tickets.keys()]) {
      this.tickets.set(line, (this.tickets.get(line) ?? 0) + 1);
    }
    this.states.update(all => {
      const next = new Map<PrefillLine, ListPriceState>();
      all.forEach((state, line) => {
        if (state.status !== 'loading') {
          next.set(line, state);
        } else if (state.price !== undefined) {
          next.set(line, { ...state, status: 'found', change: undefined });
        }
      });
      return next;
    });
  }

  /** Everything is forgotten, and no answer on its way can land afterwards. */
  reset(): void {
    this.generation++;
    this.timers.forEach(timer => clearTimeout(timer));
    this.timers.clear();
    this.tickets.clear();
    this.states.set(new Map());
  }

  destroy(): void {
    this.reset();
    this.running.unsubscribe();
  }

  private run(job: Job): Observable<void> {
    const { line, ticket, customerId } = job;
    const generation = this.generation;
    // A newer ask for the line (or a reset) came while this one waited its turn: nothing to ask any more.
    if (this.tickets.get(line) !== ticket) {
      return EMPTY;
    }
    return this.lookup(customerId, line.productId(), line.uomId()).pipe(
      map((answer): ListPriceState => this.toState(answer)),
      catchError(() => of<ListPriceState>({ status: 'unavailable' })),
      map(state => {
        if (generation === this.generation && this.tickets.get(line) === ticket) {
          this.apply(line, state);
        }
      })
    );
  }

  private toState(answer: PriceLookupDto): ListPriceState {
    return answer.found && answer.price !== undefined
      ? { status: 'found', price: answer.price, source: answer.source }
      : { status: 'none' };
  }

  /** Puts the answer on the line, and the price in the field only where the person has not put one of their own. */
  private apply(line: PrefillLine, answer: ListPriceState): void {
    const previous = this.states().get(line);
    const current = line.price();
    // The price in the field is ours while it is still what we put there.
    const ours = previous?.applied !== undefined && current === previous.applied;
    // An empty field takes the list price, and so does 0.00: a zero price cannot be submitted, so it is no price yet.
    const empty = current === null || current === 0;
    if (answer.status === 'found' && answer.price !== undefined) {
      if (empty || ours) {
        line.setPrice(answer.price);
        const replaced = ours && previous?.applied !== answer.price;
        this.set(line, {
          ...answer,
          applied: answer.price,
          ...(replaced && { change: 'replaced' as const })
        });
      } else {
        this.set(line, answer);
      }
      return;
    }
    if (answer.status === 'none' && ours) {
      // The price was the list price of what the line used to be (another unit, another customer): it no longer stands.
      line.setPrice(null);
      this.set(line, { ...answer, change: 'cleared' });
      return;
    }
    this.set(
      line,
      answer.status === 'unavailable' && ours ? { ...answer, applied: previous?.applied } : answer
    );
  }

  private set(line: PrefillLine, state: ListPriceState): void {
    this.states.update(all => new Map(all).set(line, state));
  }

  private drop(line: PrefillLine): void {
    this.states.update(all => {
      const next = new Map(all);
      next.delete(line);
      return next;
    });
  }
}
