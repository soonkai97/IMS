import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';
import { StockClient } from '@ims/api';
import { PublicId } from '@ims/models';
import { ApiCommand } from '@ims/utils';

const AVAILABILITY_PAGE_SIZE = 100;

export interface AvailabilitySearch {
  readonly warehouseId: PublicId;
  readonly search: string;
}

export interface ProductAvailability {
  readonly warehouseId: PublicId;
  readonly productId: PublicId;
}

/**
 * On-hand quantity (base unit) per product in one warehouse, for the insufficient-stock checks on issues. Both reads are
 * `ApiCommand`s used with `call(arg)`: the caller (the picker, the editor) is its own latest-wins and decides what a failure means,
 * so the error still reaches it; `$operation` / `$isFailed` show the last call's state.
 */
@Injectable({ providedIn: 'root' })
export class StockAvailabilityStore {
  private readonly client = inject(StockClient);

  /** Products without a stock row have nothing on hand and are simply missing from the map. */
  readonly bySearch = new ApiCommand<AvailabilitySearch, ReadonlyMap<PublicId, number>>(
    ({ warehouseId, search }) =>
      this.client
        .list(warehouseId, undefined, undefined, search, undefined, 1, AVAILABILITY_PAGE_SIZE)
        .pipe(map(page => new Map(page.items.map(row => [row.productId, row.qtyOnHand]))))
  );

  readonly forProduct = new ApiCommand<ProductAvailability, number>(({ warehouseId, productId }) =>
    this.client
      .list(warehouseId, productId, undefined, undefined, undefined, 1, 1)
      .pipe(map(page => page.items[0]?.qtyOnHand ?? 0))
  );
}
