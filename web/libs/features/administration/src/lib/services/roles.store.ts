import { Injectable, inject } from '@angular/core';
import { PermissionDto, PermissionsClient, RoleDto, RolesClient } from '@ims/api';
import { QueryLoader } from '@ims/utils';

export interface RoleQuery {
  readonly search?: string;
  readonly includeInactive: boolean;
}

@Injectable({ providedIn: 'root' })
export class RolesStore {
  private readonly roles = inject(RolesClient);
  private readonly permissions = inject(PermissionsClient);

  /** Not paged by the API: a tiny table (the system roles first, then the custom ones by name). */
  readonly list = new QueryLoader<RoleQuery, RoleDto[]>(query =>
    this.roles.list(query.search, query.includeInactive)
  );
  /** Every permission the API knows, with the flags that decide what a role cannot lose. */
  readonly catalog = new QueryLoader<void, PermissionDto[]>(() => this.permissions.list());
}
