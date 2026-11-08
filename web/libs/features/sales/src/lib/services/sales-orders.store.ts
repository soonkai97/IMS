import { HttpContext } from '@angular/common/http';
import { Injectable, Signal, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  DocumentDto,
  PagedResultOfSalesOrderListItem,
  SalesOrderDto,
  SalesOrderSaveRequest,
  SalesOrderStatus,
  SalesOrdersClient
} from '@ims/api';
import { HANDLED_INLINE, withIfMatch } from '@ims/core/http';
import { ApiError, ApiOperation, HTTP_STATUS, PagedQuery, PublicId } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface SalesOrderQuery extends PagedQuery {
  readonly status?: SalesOrderStatus;
  readonly customerId?: PublicId;
  readonly search?: string;
}

/** The order a command acts on and the version the user saw: the server refuses with 409 when it changed since. */
export interface SalesOrderRef {
  readonly id: PublicId;
  readonly concurrencyToken: string;
}

export interface SalesOrderReasonArgs extends SalesOrderRef {
  readonly reason: string;
}

/** Save a draft: without `ref` it is created, with one it is updated (If-Match) from the version the person opened. */
export interface SaveSalesOrderArgs {
  readonly ref?: SalesOrderRef;
  readonly body: SalesOrderSaveRequest;
}

export function toSalesOrderRef(order: SalesOrderDto): SalesOrderRef {
  return { id: order.id, concurrencyToken: order.concurrencyToken };
}

/**
 * Sales orders (module sales). The server decides what a user may do (`allowedActions`, `canAct`); this store only runs the
 * commands, always with the token of the version on screen, and keeps the answer (the fresh order) as the opened one. Every
 * command is exclusive: a second tap while one runs sends nothing.
 */
@Injectable({ providedIn: 'root' })
export class SalesOrdersStore {
  private readonly client = inject(SalesOrdersClient);

  readonly list = new QueryLoader<SalesOrderQuery, PagedResultOfSalesOrderListItem>(query =>
    this.client.list(
      query.status,
      query.customerId,
      undefined,
      undefined,
      undefined,
      query.search,
      query.page,
      query.pageSize
    )
  );
  /** The page loads it with `handledInline()` (its banner shows a failure); the context goes to the call. */
  readonly detail = new QueryLoader<PublicId, SalesOrderDto>((id, context) =>
    this.client.get(id, context)
  );

  readonly save: ApiCommand<SaveSalesOrderArgs, SalesOrderDto> = new ApiCommand(args => {
    // The editor shows a refusal (403) and a conflict (409) in its own alert: no toast on top.
    const context = (args.ref ? withIfMatch(args.ref.concurrencyToken) : new HttpContext()).set(
      HANDLED_INLINE,
      [HTTP_STATUS.Forbidden, HTTP_STATUS.Conflict]
    );
    return this.accept(
      args.ref
        ? this.client.update(args.ref.id, args.body, context)
        : this.client.create(args.body, context),
      this.save
    );
  }, true);
  readonly submit: ApiCommand<SalesOrderRef, SalesOrderDto> = new ApiCommand(
    ref => this.accept(this.client.submit(ref.id, withIfMatch(ref.concurrencyToken)), this.submit),
    true
  );
  readonly recall: ApiCommand<SalesOrderRef, SalesOrderDto> = new ApiCommand(
    ref => this.accept(this.client.recall(ref.id, withIfMatch(ref.concurrencyToken)), this.recall),
    true
  );
  readonly approve: ApiCommand<SalesOrderRef, SalesOrderDto> = new ApiCommand(
    ref =>
      this.accept(this.client.approve(ref.id, withIfMatch(ref.concurrencyToken)), this.approve),
    true
  );
  readonly reject: ApiCommand<SalesOrderReasonArgs, SalesOrderDto> = new ApiCommand(
    args =>
      this.accept(
        this.client.reject(args.id, { reason: args.reason }, withIfMatch(args.concurrencyToken)),
        this.reject
      ),
    true
  );
  /** The reason is optional for a Draft or Submitted order and required (5 or more characters) for an Approved one; the server decides. */
  readonly cancel: ApiCommand<SalesOrderReasonArgs, SalesOrderDto> = new ApiCommand(
    args =>
      this.accept(
        this.client.cancel(
          args.id,
          { reason: args.reason || undefined },
          withIfMatch(args.concurrencyToken)
        ),
        this.cancel
      ),
    true
  );
  readonly close: ApiCommand<SalesOrderReasonArgs, SalesOrderDto> = new ApiCommand(
    args =>
      this.accept(
        this.client.close(args.id, { reason: args.reason }, withIfMatch(args.concurrencyToken)),
        this.close
      ),
    true
  );

  /**
   * Starts a draft delivery from the order, for what is still to deliver, with no price. The call carries no token (the API checks
   * the order itself); a refusal (409: the order is no longer open, nothing left, module off) is the page's alert, so no toast.
   */
  readonly deliver: ApiCommand<PublicId, DocumentDto> = new ApiCommand(
    id =>
      this.client.createDelivery(id, new HttpContext().set(HANDLED_INLINE, [HTTP_STATUS.Conflict])),
    true
  );

  /** Forgets the outcome of every command (their errors drive banners such as "changed by someone else"). */
  resetCommands(): void {
    this.resetCommandsExcept(undefined);
  }

  /** Forgets the failed commands whose error `refused` accepts (an old refusal must not fire its handler again). */
  resetFailed(refused: (error: ApiError) => boolean): void {
    for (const command of this.commands()) {
      const error = command.$operation().error;
      if (error && refused(error)) {
        command.reset();
      }
    }
  }

  private commands(): readonly { $operation: Signal<ApiOperation>; reset(): void }[] {
    return [
      this.save,
      this.submit,
      this.recall,
      this.approve,
      this.reject,
      this.cancel,
      this.close,
      this.deliver
    ];
  }

  private resetCommandsExcept(keep: { reset(): void } | undefined): void {
    for (const command of this.commands()) {
      if (command !== keep) {
        command.reset();
      }
    }
  }

  /** Every state change returns the fresh order; keep it as the opened one and drop the other commands' old errors. */
  private accept(
    source$: Observable<SalesOrderDto>,
    command: { reset(): void }
  ): Observable<SalesOrderDto> {
    return source$.pipe(
      tap(order => {
        // The user may have opened another order while this call ran.
        if (this.detail.$data()?.id === order.id) {
          this.detail.$data.set(order);
        }
        this.resetCommandsExcept(command);
      })
    );
  }
}
