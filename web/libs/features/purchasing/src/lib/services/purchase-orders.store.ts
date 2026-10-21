import { Injectable, Signal, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  PagedResultOfPurchaseOrderListItem,
  PurchaseOrderDto,
  PurchaseOrderStatus,
  PurchaseOrdersClient
} from '@ims/api';
import { withIfMatch } from '@ims/core/http';
import { ApiError, ApiOperation, PagedQuery, PublicId } from '@ims/models';
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
    return [this.submit, this.recall, this.approve, this.reject, this.cancel, this.close];
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
