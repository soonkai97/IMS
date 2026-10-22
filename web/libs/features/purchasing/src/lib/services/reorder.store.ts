import { Injectable, inject, signal } from '@angular/core';
import { PagedResultOfReorderSuggestion, PurchaseOrdersClient } from '@ims/api';
import { PagedQuery, PublicId } from '@ims/models';
import { QueryLoader } from '@ims/utils';

/** The warehouse is required: the API refuses a request without it, so the screen sends none until one is chosen. */
export interface ReorderQuery extends PagedQuery {
  readonly warehouseId: PublicId;
  readonly search?: string;
}

/**
 * What to order (module purchasing). The loader is latest-wins: a slower answer to an earlier warehouse or search never replaces
 * the newer one. The store outlives a sign-out, so the screen resets it when it opens; the warehouse last chosen is remembered
 * with the user it belongs to and is only offered back to that user.
 */
@Injectable({ providedIn: 'root' })
export class ReorderStore {
  private readonly client = inject(PurchaseOrdersClient);

  readonly suggestions = new QueryLoader<ReorderQuery, PagedResultOfReorderSuggestion>(query =>
    this.client.suggestions(query.warehouseId, query.search, query.page, query.pageSize)
  );

  private readonly $last = signal<{ userId: string; warehouseId: PublicId } | null>(null);

  lastWarehouse(userId: string | undefined): PublicId | null {
    const last = this.$last();
    return userId !== undefined && last?.userId === userId ? last.warehouseId : null;
  }

  remember(userId: string | undefined, warehouseId: PublicId | null): void {
    if (userId !== undefined && warehouseId !== null) {
      this.$last.set({ userId, warehouseId });
    }
  }
}
