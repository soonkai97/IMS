import { Injectable, computed, inject } from '@angular/core';
import { tap } from 'rxjs';
import {
  CategoriesClient,
  CategoryDto,
  CategorySaveRequest,
  UnitsClient,
  UomDto,
  UomSaveRequest
} from '@ims/api';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface SaveCategoryArgs {
  readonly id?: number;
  readonly body: CategorySaveRequest;
}

export interface SaveUnitArgs {
  readonly id?: number;
  readonly body: UomSaveRequest;
}

/** Categories and units of measure: small lists used by product forms, filters and line editors. */
@Injectable({ providedIn: 'root' })
export class CatalogStore {
  private readonly categoriesClient = inject(CategoriesClient);
  private readonly unitsClient = inject(UnitsClient);

  readonly categories = new QueryLoader<void, CategoryDto[]>(() => this.categoriesClient.list());
  readonly units = new QueryLoader<void, UomDto[]>(() => this.unitsClient.list());

  readonly saveCategory = new ApiCommand<SaveCategoryArgs, CategoryDto>(args =>
    (args.id === undefined
      ? this.categoriesClient.create(args.body)
      : this.categoriesClient.update(args.id, args.body)
    ).pipe(tap(() => this.categories.reload()))
  );
  readonly saveUnit = new ApiCommand<SaveUnitArgs, UomDto>(args =>
    (args.id === undefined
      ? this.unitsClient.create(args.body)
      : this.unitsClient.update(args.id, args.body)
    ).pipe(tap(() => this.units.reload()))
  );

  readonly $categories = computed(() => this.categories.$data() ?? []);
  readonly $units = computed(() => this.units.$data() ?? []);
  /** uomId -> code, for line editors that only know ids. */
  readonly $unitCodes = computed<ReadonlyMap<number, string>>(
    () => new Map(this.$units().map(unit => [unit.id, unit.code]))
  );

  ensureLoaded(): void {
    if (this.categories.$data() === undefined && !this.categories.$isLoading()) {
      this.categories.load();
    }
    if (this.units.$data() === undefined && !this.units.$isLoading()) {
      this.units.load();
    }
  }
}
