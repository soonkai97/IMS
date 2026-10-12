import { Injectable, computed, inject } from '@angular/core';
import {
  PagedResultOfLedgerRow,
  PagedResultOfLowStockAlert,
  PagedResultOfStockRow,
  StockClient,
  StockSort,
  StockSummary
} from '@ims/api';
import { handledInline } from '@ims/core/http';
import { PagedQuery } from '@ims/models';
import { QueryLoader } from '@ims/utils';

export interface StockQuery extends PagedQuery {
  readonly warehouseId?: number;
  readonly productId?: number;
  readonly categoryId?: number;
  readonly search?: string;
  readonly lowOnly?: boolean;
  readonly sortBy?: StockSort;
}

export interface LedgerQuery extends PagedQuery {
  readonly warehouseId?: number;
  readonly productId?: number;
  readonly from?: string;
  readonly to?: string;
  readonly sourceDocId?: number;
}

export interface LowStockQuery extends PagedQuery {
  readonly warehouseId?: number;
}

const PRODUCT_STOCK_PAGE_SIZE = 100;

/** Read side of stock: enquiry, ledger, low-stock list and the headline numbers of the dashboard. */
@Injectable({ providedIn: 'root' })
export class StockStore {
  private readonly client = inject(StockClient);

  readonly list = new QueryLoader<StockQuery, PagedResultOfStockRow>(query =>
    this.client.list(
      query.warehouseId,
      query.productId,
      query.categoryId,
      query.search,
      query.lowOnly,
      query.page,
      query.pageSize,
      query.sortBy,
      query.desc
    )
  );
  /** Stock rows of one product across warehouses (header of the ledger screen). */
  readonly productStock = new QueryLoader<number, PagedResultOfStockRow>(productId =>
    this.client.list(
      undefined,
      productId,
      undefined,
      undefined,
      undefined,
      1,
      PRODUCT_STOCK_PAGE_SIZE
    )
  );
  readonly ledger = new QueryLoader<LedgerQuery, PagedResultOfLedgerRow>(query =>
    this.client.ledger(
      query.warehouseId,
      query.productId,
      query.from,
      query.to,
      query.sourceDocId,
      query.page,
      query.pageSize
    )
  );
  readonly lowStock = new QueryLoader<LowStockQuery, PagedResultOfLowStockAlert>(query =>
    this.client.lowStock(query.warehouseId, query.page, query.pageSize)
  );
  /** Home shows a failed load in a banner with Retry, so the call raises no toast (one failure, one announcement). */
  readonly summary = new QueryLoader<void, StockSummary>(() =>
    this.client.summary(handledInline())
  );

  /** Number on the "Low stock" menu badge. */
  readonly $lowStockCount = computed(() => this.summary.$data()?.lowCount ?? 0);
}
