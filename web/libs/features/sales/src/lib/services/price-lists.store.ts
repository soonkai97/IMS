import { HttpContext } from '@angular/common/http';
import { Injectable, Signal, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  PagedResultOfPriceListListItem,
  PriceListDto,
  PriceListSaveRequest,
  PriceListsClient
} from '@ims/api';
import { HANDLED_INLINE, withIfMatch } from '@ims/core/http';
import { ApiOperation, HTTP_STATUS, PagedQuery, PublicId } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface PriceListQuery extends PagedQuery {
  readonly search?: string;
}

/** The list a command acts on and the version the user saw: the server refuses with 409 when it changed since. */
export interface PriceListRef {
  readonly id: PublicId;
  readonly concurrencyToken: string;
}

/** Save: without `ref` the list is created, with one it is replaced (If-Match) from the version the person opened. */
export interface SavePriceListArgs {
  readonly ref?: PriceListRef;
  readonly body: PriceListSaveRequest;
}

export function toPriceListRef(list: PriceListDto): PriceListRef {
  return { id: list.id, concurrencyToken: list.concurrencyToken };
}

/** A refusal (403, 409) of these two commands is the page's own alert, so the error interceptor raises no toast for it. */
const SHOWN_BY_PAGE = [HTTP_STATUS.Forbidden, HTTP_STATUS.Conflict];

/**
 * Price lists (module sales, permission pricelist.manage). Save replaces the whole list and always carries the version the
 * person opened; delete does too. Every command is exclusive: a second tap while one runs sends nothing.
 */
@Injectable({ providedIn: 'root' })
export class PriceListsStore {
  private readonly client = inject(PriceListsClient);

  readonly list = new QueryLoader<PriceListQuery, PagedResultOfPriceListListItem>(query =>
    this.client.list(query.search, query.page, query.pageSize)
  );
  /** The page loads it with `handledInline()` (its banner shows a failure); the context goes to the call. */
  readonly detail = new QueryLoader<PublicId, PriceListDto>((id, context) =>
    this.client.get(id, context)
  );

  readonly save: ApiCommand<SavePriceListArgs, PriceListDto> = new ApiCommand(args => {
    const context = (args.ref ? withIfMatch(args.ref.concurrencyToken) : new HttpContext()).set(
      HANDLED_INLINE,
      SHOWN_BY_PAGE
    );
    return this.accept(
      args.ref
        ? this.client.update(args.ref.id, args.body, context)
        : this.client.create(args.body, context),
      this.save
    );
  }, true);

  readonly delete: ApiCommand<PriceListRef, void> = new ApiCommand(
    ref =>
      this.client.delete(
        ref.id,
        withIfMatch(ref.concurrencyToken).set(HANDLED_INLINE, SHOWN_BY_PAGE)
      ),
    true
  );

  /** Forgets the outcome of every command (their errors drive banners such as "changed by someone else"). */
  resetCommands(): void {
    for (const command of this.commands()) {
      command.reset();
    }
  }

  private commands(): readonly { $operation: Signal<ApiOperation>; reset(): void }[] {
    return [this.save, this.delete];
  }

  /** The answer of a save is the fresh list: keep it as the opened one, and drop the other commands' old errors. */
  private accept(
    source$: Observable<PriceListDto>,
    command: { reset(): void }
  ): Observable<PriceListDto> {
    return source$.pipe(
      tap(list => {
        if (this.detail.$data()?.id === list.id) {
          this.detail.$data.set(list);
        }
        for (const other of this.commands()) {
          if (other !== command) {
            other.reset();
          }
        }
      })
    );
  }
}
