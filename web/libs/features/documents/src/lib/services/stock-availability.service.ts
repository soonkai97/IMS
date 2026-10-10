import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { StockClient } from '@ims/api';

const AVAILABILITY_PAGE_SIZE = 100;

/** On-hand quantity (base unit) per product in one warehouse, for the insufficient-stock checks on issues. */
@Injectable({ providedIn: 'root' })
export class StockAvailabilityService {
  private readonly client = inject(StockClient);

  /** Products without a stock row have nothing on hand and are simply missing from the map. */
  bySearch(warehouseId: number, search: string): Observable<ReadonlyMap<number, number>> {
    return this.client
      .list(warehouseId, undefined, undefined, search, undefined, 1, AVAILABILITY_PAGE_SIZE)
      .pipe(map(page => new Map(page.items.map(row => [row.productId, row.qtyOnHand]))));
  }

  forProduct(warehouseId: number, productId: number): Observable<number> {
    return this.client
      .list(warehouseId, productId, undefined, undefined, undefined, 1, 1)
      .pipe(map(page => page.items[0]?.qtyOnHand ?? 0));
  }
}
