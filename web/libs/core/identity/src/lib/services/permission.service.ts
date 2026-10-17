import { Injectable, computed, inject } from '@angular/core';
import { ALL_WAREHOUSE_ROLES } from '@ims/models';
import { profileOf } from '../rules/profile-of';
import { AuthService } from './auth.service';

/** What the signed-in user may do, built from the GET /api/auth/me payload. Drives menus, buttons and route guards. */
@Injectable({ providedIn: 'root' })
export class PermissionService {
  private readonly auth = inject(AuthService);

  readonly $permissions = computed(() => new Set(this.auth.$user()?.permissions ?? []));
  /** Cost, valuation, approval, phone tabs and list defaults, derived from the permission codes. */
  readonly $profile = computed(() => profileOf(this.$permissions()));
  readonly $role = computed(() => this.auth.$user()?.role);
  readonly $displayName = computed(() => this.auth.$user()?.displayName ?? '');
  readonly $username = computed(() => this.auth.$user()?.username ?? '');
  readonly $userId = computed(() => this.auth.$user()?.id);
  readonly $warehouseIds = computed(() => this.auth.$user()?.warehouseIds ?? []);
  readonly $seesAllWarehouses = computed(() => ALL_WAREHOUSE_ROLES.includes(this.$role() ?? ''));

  has(permission: string): boolean {
    return this.$permissions().has(permission);
  }

  /** True when the user holds at least one of the permissions (an empty list means "no restriction"). */
  hasAny(permissions: readonly string[]): boolean {
    return permissions.length === 0 || permissions.some(permission => this.has(permission));
  }
}
