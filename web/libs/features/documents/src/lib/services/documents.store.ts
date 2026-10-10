import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  DocumentDto,
  DocumentSaveRequest,
  DocumentStatus,
  DocumentType,
  DocumentsClient,
  PagedResultOfDocumentListItem
} from '@ims/api';
import { PagedQuery } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface DocumentQuery extends PagedQuery {
  readonly type: DocumentType;
  readonly status?: DocumentStatus;
  readonly warehouseId?: number;
  readonly from?: string;
  readonly to?: string;
  readonly search?: string;
  readonly page: number;
  readonly pageSize: number;
}

export interface SaveDocumentArgs {
  readonly type: DocumentType;
  /** Present when editing an existing draft. */
  readonly id?: number;
  readonly body: DocumentSaveRequest;
}

export interface ReasonArgs {
  readonly id: number;
  readonly reason: string;
}

export interface PostArgs {
  readonly id: number;
  /** Required by the server when posting would take stock below zero. */
  readonly negativeStockOverrideReason?: string;
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
      query.pageSize
    )
  );
  readonly detail = new QueryLoader<number, DocumentDto>(id => this.client.get(id));

  readonly save = new ApiCommand<SaveDocumentArgs, DocumentDto>(args =>
    this.accept(this.saveRequest(args))
  );
  readonly submit = new ApiCommand<number, DocumentDto>(id => this.accept(this.client.submit(id)));
  readonly approve = new ApiCommand<number, DocumentDto>(id =>
    this.accept(this.client.approve(id))
  );
  readonly reject = new ApiCommand<ReasonArgs, DocumentDto>(args =>
    this.accept(this.client.reject(args.id, { reason: args.reason }))
  );
  readonly cancel = new ApiCommand<ReasonArgs, DocumentDto>(args =>
    this.accept(this.client.cancel(args.id, { reason: args.reason || undefined }))
  );
  readonly post = new ApiCommand<PostArgs, DocumentDto>(args =>
    this.accept(
      this.client.post(args.id, {
        negativeStockOverrideReason: args.negativeStockOverrideReason || undefined
      })
    )
  );
  readonly reverse = new ApiCommand<ReasonArgs, DocumentDto>(args =>
    this.accept(this.client.reverse(args.id, { reason: args.reason }))
  );

  private saveRequest(args: SaveDocumentArgs): Observable<DocumentDto> {
    if (args.id !== undefined) {
      return this.client.update(args.id, args.body);
    }
    return args.type === DocumentType.Receipt
      ? this.client.createReceipt(args.body)
      : this.client.createIssue(args.body);
  }

  /** Every state change returns the fresh document; keep it as the opened one. */
  private accept(source$: Observable<DocumentDto>): Observable<DocumentDto> {
    return source$.pipe(tap(document => this.detail.$data.set(document)));
  }
}
