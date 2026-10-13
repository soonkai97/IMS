import { Injectable, inject } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import {
  DocumentCounts,
  DocumentDto,
  DocumentSaveRequest,
  DocumentStatus,
  DocumentType,
  DocumentsClient,
  PagedResultOfDocumentListItem
} from '@ims/api';
import { withIfMatch } from '@ims/core/http';
import { PagedQuery } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface DocumentQuery extends PagedQuery {
  readonly type: DocumentType;
  readonly status?: DocumentStatus;
  readonly warehouseId?: number;
  readonly from?: string;
  readonly to?: string;
  readonly search?: string;
  /** true = only documents I created. */
  readonly mine?: boolean;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * The document a command acts on and the version the user saw. The server refuses the command with 409 when the
 * document changed since (E4); without a token it acts on whatever is current.
 */
export interface DocumentRef {
  readonly id: number;
  readonly concurrencyToken?: string;
}

export interface SaveDocumentArgs {
  readonly type: DocumentType;
  /** Present when editing an existing draft. */
  readonly ref?: DocumentRef;
  readonly body: DocumentSaveRequest;
}

export interface ReasonArgs extends DocumentRef {
  readonly reason: string;
}

export interface ApproveArgs extends DocumentRef {
  /** Required by the server when the document requires an override (stock goes below zero). */
  readonly negativeStockOverrideReason?: string;
}

/** A document number matches itself and, at most, a few look-alikes. */
const LOOKUP_PAGE_SIZE = 5;

export function toDocumentRef(document: DocumentDto): DocumentRef {
  return { id: document.id, concurrencyToken: document.concurrencyToken };
}

/**
 * Receipts and issues share one resource and one state machine. The server decides which actions a user may take
 * (`allowedActions`); this store only runs them and keeps the opened document up to date with each response.
 */
@Injectable({ providedIn: 'root' })
export class DocumentsStore {
  private readonly client = inject(DocumentsClient);

  readonly list = new QueryLoader<DocumentQuery, PagedResultOfDocumentListItem>(query =>
    this.client.list(
      query.type,
      query.status,
      query.warehouseId,
      query.from,
      query.to,
      query.search,
      query.page,
      query.pageSize,
      undefined,
      undefined,
      query.mine
    )
  );
  /** The editor loads it with `handledInline()` (its banner shows a failure); the context goes to the call. */
  readonly detail = new QueryLoader<number, DocumentDto>((id, context) =>
    this.client.get(id, context)
  );
  /** Home numbers and the approvals badge: four indexed counts for the signed-in user. */
  readonly counts = new QueryLoader<void, DocumentCounts>((_, context) =>
    this.client.counts(context)
  );

  /** Save, submit and post are exclusive: a second tap while one runs sends nothing (NR-07). */
  readonly save: ApiCommand<SaveDocumentArgs, DocumentDto> = new ApiCommand(
    args => this.accept(this.saveRequest(args), this.save),
    true
  );
  readonly submit: ApiCommand<DocumentRef, DocumentDto> = new ApiCommand(
    ref => this.accept(this.client.submit(ref.id, withIfMatch(ref.concurrencyToken)), this.submit),
    true
  );
  readonly approve: ApiCommand<ApproveArgs, DocumentDto> = new ApiCommand(args =>
    this.accept(
      this.client.approve(
        args.id,
        { negativeStockOverrideReason: args.negativeStockOverrideReason || undefined },
        withIfMatch(args.concurrencyToken)
      ),
      this.approve
    )
  );
  readonly reject: ApiCommand<ReasonArgs, DocumentDto> = new ApiCommand(args =>
    this.accept(
      this.client.reject(args.id, { reason: args.reason }, withIfMatch(args.concurrencyToken)),
      this.reject
    )
  );
  readonly cancel: ApiCommand<ReasonArgs, DocumentDto> = new ApiCommand(args =>
    this.accept(
      this.client.cancel(
        args.id,
        { reason: args.reason || undefined },
        withIfMatch(args.concurrencyToken)
      ),
      this.cancel
    )
  );
  /** Posting takes no body: an override reason was given when the document was approved. */
  readonly post: ApiCommand<DocumentRef, DocumentDto> = new ApiCommand(
    ref => this.accept(this.client.post(ref.id, withIfMatch(ref.concurrencyToken)), this.post),
    true
  );
  readonly reverse: ApiCommand<ReasonArgs, DocumentDto> = new ApiCommand(args =>
    this.accept(
      this.client.reverse(args.id, { reason: args.reason }, withIfMatch(args.concurrencyToken)),
      this.reverse
    )
  );

  /**
   * The token the server holds for a document right now, read from the list (the list item's token equals the full
   * document's). Comparing it with the opened document tells whether it went stale, before any save gets a 409.
   */
  currentToken(document: Pick<DocumentDto, 'id' | 'no' | 'type'>): Observable<string | undefined> {
    return this.client
      .list(
        document.type,
        undefined,
        undefined,
        undefined,
        undefined,
        document.no,
        1,
        LOOKUP_PAGE_SIZE,
        undefined,
        false
      )
      .pipe(map(page => page.items.find(item => item.id === document.id)?.concurrencyToken));
  }

  /** Forgets the outcome of every command (their errors drive banners such as "changed by someone else"). */
  resetCommands(): void {
    this.resetCommandsExcept(undefined);
  }

  private resetCommandsExcept(keep: { reset(): void } | undefined): void {
    for (const command of [
      this.save,
      this.submit,
      this.approve,
      this.reject,
      this.cancel,
      this.post,
      this.reverse
    ]) {
      if (command !== keep) {
        command.reset();
      }
    }
  }

  private saveRequest(args: SaveDocumentArgs): Observable<DocumentDto> {
    if (args.ref !== undefined) {
      return this.client.update(args.ref.id, args.body, withIfMatch(args.ref.concurrencyToken));
    }
    return args.type === DocumentType.Receipt
      ? this.client.createReceipt(args.body)
      : this.client.createIssue(args.body);
  }

  /**
   * Every state change returns the fresh document; keep it as the opened one. The document is now in a new state, so
   * what the OTHER commands failed with ('Stock changed' after a failed post, then the document was cancelled) no
   * longer describes it and must not keep a banner on screen.
   */
  private accept(
    source$: Observable<DocumentDto>,
    command: { reset(): void }
  ): Observable<DocumentDto> {
    return source$.pipe(
      tap(document => {
        this.detail.$data.set(document);
        this.resetCommandsExcept(command);
      })
    );
  }
}
