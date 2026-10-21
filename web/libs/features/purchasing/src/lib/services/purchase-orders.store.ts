import { Injectable, inject } from '@angular/core';
import {
  PagedResultOfPurchaseOrderListItem,
  PurchaseOrderStatus,
  PurchaseOrdersClient
} from '@ims/api';
import { PagedQuery, PublicId } from '@ims/models';
import { QueryLoader } from '@ims/utils';

export interface PurchaseOrderQuery extends PagedQuery {
  readonly status?: PurchaseOrderStatus;
  readonly supplierId?: PublicId;
  readonly warehouseId?: PublicId;
  readonly from?: string;
  readonly to?: string;
  readonly search?: string;
}

/** Purchase orders (module purchasing). The server decides what a user may see and do; this store only loads. */
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
}
