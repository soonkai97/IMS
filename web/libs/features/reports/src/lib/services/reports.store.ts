import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { DocumentType, FileResponse, PagedResultOfMovementRow, ReportsClient } from '@ims/api';
import { PagedQuery } from '@ims/models';
import { ApiCommand, QueryLoader, saveFile } from '@ims/utils';

export interface MovementFilter {
  readonly type?: DocumentType;
  readonly warehouseId?: number;
  readonly productId?: number;
  readonly from?: string;
  readonly to?: string;
}

export type MovementQuery = MovementFilter & PagedQuery;

export interface StockExportQuery {
  readonly warehouseId?: number;
  readonly productId?: number;
  readonly categoryId?: number;
  readonly search?: string;
  readonly lowOnly?: boolean;
}

export interface LedgerExportQuery {
  readonly warehouseId?: number;
  readonly productId?: number;
  readonly from?: string;
  readonly to?: string;
}

const EXPORT_FALLBACK_NAME = 'export.xlsx';

/** Report data and the Excel exports. The server names each file (Content-Disposition); we only hand it to the browser. */
@Injectable({ providedIn: 'root' })
export class ReportsStore {
  private readonly client = inject(ReportsClient);

  readonly movements = new QueryLoader<MovementQuery, PagedResultOfMovementRow>(query =>
    this.client.movements(
      query.type,
      query.warehouseId,
      query.productId,
      query.from,
      query.to,
      query.page,
      query.pageSize
    )
  );

  readonly exportMovements = new ApiCommand<MovementFilter, FileResponse>(query =>
    this.download(
      this.client.movementsExport(
        query.type,
        query.warehouseId,
        query.productId,
        query.from,
        query.to
      )
    )
  );
  readonly exportStockSummary = new ApiCommand<StockExportQuery, FileResponse>(query =>
    this.download(
      this.client.stockSummaryExport(
        query.warehouseId,
        query.productId,
        query.categoryId,
        query.search,
        query.lowOnly
      )
    )
  );
  readonly exportLedger = new ApiCommand<LedgerExportQuery, FileResponse>(query =>
    this.download(
      this.client.ledgerExport(query.warehouseId, query.productId, query.from, query.to)
    )
  );
  readonly exportLowStock = new ApiCommand<void, FileResponse>(() =>
    this.download(this.client.lowStockExport())
  );

  private download(source$: Observable<FileResponse>): Observable<FileResponse> {
    return source$.pipe(tap(file => saveFile(file, EXPORT_FALLBACK_NAME)));
  }
}
