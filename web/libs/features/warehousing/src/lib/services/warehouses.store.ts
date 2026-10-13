import { Injectable, computed, inject } from '@angular/core';
import { tap } from 'rxjs';
import { WarehouseDto, WarehouseSaveRequest, WarehousesClient } from '@ims/api';
import { ApiCommand, QueryLoader } from '@ims/utils';
import { PublicId } from '@ims/models';

export interface SaveWarehouseArgs {
  readonly id?: PublicId;
  readonly body: WarehouseSaveRequest;
}

@Injectable({ providedIn: 'root' })
export class WarehousesStore {
  private readonly client = inject(WarehousesClient);

  /** `true` also returns deactivated warehouses (the admin list); pickers use the active ones only. */
  readonly list = new QueryLoader<boolean, WarehouseDto[]>(includeInactive =>
    this.client.list(includeInactive)
  );
  readonly save = new ApiCommand<SaveWarehouseArgs, WarehouseDto>(args =>
    (args.id === undefined
      ? this.client.create(args.body)
      : this.client.update(args.id, args.body)
    ).pipe(tap(() => this.list.reload()))
  );

  readonly $warehouses = computed(() => this.list.$data() ?? []);
  readonly $activeWarehouses = computed(() =>
    this.$warehouses().filter(warehouse => warehouse.isActive)
  );

  ensureLoaded(): void {
    if (this.list.$data() === undefined && !this.list.$isLoading()) {
      this.list.load(false);
    }
  }
}
