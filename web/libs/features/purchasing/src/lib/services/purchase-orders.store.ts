import { HttpContext } from '@angular/common/http';
import { Injectable, Signal, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  DocumentDto,
  PagedResultOfPurchaseOrderListItem,
  PurchaseOrderDto,
  PurchaseOrderSaveRequest,
  PurchaseOrderStatus,
  PurchaseOrdersClient
} from '@ims/api';
import { HANDLED_INLINE, withIfMatch } from '@ims/core/http';
import { ApiError, ApiOperation, HTTP_STATUS, PagedQuery, PublicId } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface PurchaseOrderQuery extends PagedQuery {
  readonly status?: PurchaseOrderStatus;
  readonly supplierId?: PublicId;
  readonly warehouseId?: PublicId;
  readonly from?: string;
  readonly to?: string;
  readonly search?: string;
}

/** The order a command acts on and the version the user saw: the server refuses with 409 when it changed since. */
export interface PurchaseOrderRef {
  readonly id: PublicId;
  readonly concurrencyToken: string;
}

export interface PurchaseOrderReasonArgs extends PurchaseOrderRef {
  readonly reason: string;
}

/** Save a draft: without `ref` it is created, with one it is updated (If-Match) from the version the person opened. */
export interface SavePurchaseOrderArgs {
  readonly ref?: PurchaseOrderRef;
  readonly body: PurchaseOrderSaveRequest;
}

export function toPurchaseOrderRef(order: PurchaseOrderDto): PurchaseOrderRef {
  return { id: order.id, concurrencyToken: order.concurrencyToken };
}

/**
 * Purchase orders (module purchasing). The server decides what a user may do (`allowedActions`, `canAct`); this store only
 * runs the commands, always with the token of the version on screen, and keeps the answer (the fresh order) as the opened one.
 * Every command is exclusive: a second tap while one runs sends nothing.
 */
@Injectable({ providedIn: 'root' })
export class PurchaseOrdersStore {
  private readonly client = inject(PurchaseOrdersClient);

  readonly list = new QueryLoader<PurchaseOrderQuery, PagedResultOfPurchaseOrderListItem>(query =>
    this.client.list(
      query.status,
      query.supplierId,
      query.warehouseId,
      query.from,
      query.to,
      query.search,
      query.page,
      query.pageSize
    )
  );
  /** The page loads it with `handledInline()` (its banner shows a failure); the context goes to the call. */
  readonly detail = new QueryLoader<PublicId, PurchaseOrderDto>((id, context) =>
    this.client.get(id, context)
  );

  readonly save: ApiCommand<SavePurchaseOrderArgs, PurchaseOrderDto> = new ApiCommand(args => {
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
  readonly submit: ApiCommand<PurchaseOrderRef, PurchaseOrderDto> = new ApiCommand(
    ref => this.accept(this.client.submit(ref.id, withIfMatch(ref.concurrencyToken)), this.submit),
    true
  );
  readonly recall: ApiCommand<PurchaseOrderRef, PurchaseOrderDto> = new ApiCommand(
    ref => this.accept(this.client.recall(ref.id, withIfMatch(ref.concurrencyToken)), this.recall),
    true
  );
  readonly approve: ApiCommand<PurchaseOrderRef, PurchaseOrderDto> = new ApiCommand(
    ref =>
      this.accept(this.client.approve(ref.id, withIfMatch(ref.concurrencyToken)), this.approve),
    true
  );
  readonly reject: ApiCommand<PurchaseOrderReasonArgs, PurchaseOrderDto> = new ApiCommand(
    args =>
      this.accept(
        this.client.reject(args.id, { reason: args.reason }, withIfMatch(args.concurrencyToken)),
        this.reject
      ),
    true
  );
  /** The reason is optional for a Draft or Submitted order and required (5 or more characters) for an Approved one; the server decides. */
  readonly cancel: ApiCommand<PurchaseOrderReasonArgs, PurchaseOrderDto> = new ApiCommand(
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
  readonly close: ApiCommand<PurchaseOrderReasonArgs, PurchaseOrderDto> = new ApiCommand(
    args =>
      this.accept(
        this.client.close(args.id, { reason: args.reason }, withIfMatch(args.concurrencyToken)),
        this.close
      ),
    true
  );

  /**
   * Starts a draft receipt from the order, at the order's prices, for what is still to receive. The call carries no token (the
   * API checks the order itself); a refusal (409: the order is no longer open, module off) is the page's alert, so no toast.
   */
  readonly receive: ApiCommand<PublicId, DocumentDto> = new ApiCommand(
    id =>
      this.client.createReceipt(id, new HttpContext().set(HANDLED_INLINE, [HTTP_STATUS.Conflict])),
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
      this.receive
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
    source$: Observable<PurchaseOrderDto>,
    command: { reset(): void }
  ): Observable<PurchaseOrderDto> {
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
