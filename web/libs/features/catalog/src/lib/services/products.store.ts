import { Injectable, inject } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import {
  PagedResultOfProductDto,
  ProductDto,
  ProductSaveRequest,
  ProductSort,
  ProductsClient
} from '@ims/api';
import { PagedQuery } from '@ims/models';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface ProductQuery extends PagedQuery {
  readonly search?: string;
  readonly categoryId?: number;
  readonly active?: boolean;
  readonly sortBy?: ProductSort;
}

export interface SaveProductArgs {
  /** Present when editing. */
  readonly id?: number;
  readonly body: ProductSaveRequest;
}

const PICKER_RESULT_COUNT = 8;

@Injectable({ providedIn: 'root' })
export class ProductsStore {
  private readonly client = inject(ProductsClient);

  readonly list = new QueryLoader<ProductQuery, PagedResultOfProductDto>(query =>
    this.client.list(
      query.search,
      query.categoryId,
      query.active,
      query.page,
      query.pageSize,
      query.sortBy,
      query.desc
    )
  );
  readonly detail = new QueryLoader<number, ProductDto>(id => this.client.get(id));
  readonly save = new ApiCommand<SaveProductArgs, ProductDto>(args =>
    (args.id === undefined
      ? this.client.create(args.body)
      : this.client.update(args.id, args.body)
    ).pipe(tap(product => this.detail.$data.set(product)))
  );

  /** Type-ahead for product pickers: active products only. */
  search(term: string): Observable<PagedResultOfProductDto> {
    return this.client.list(term, undefined, true, 1, PICKER_RESULT_COUNT);
  }

  /** Exact SKU or barcode (scanner input). */
  lookup(code: string): Observable<ProductDto | undefined> {
    return this.client.lookup(code).pipe(map(result => result.product));
  }
}
