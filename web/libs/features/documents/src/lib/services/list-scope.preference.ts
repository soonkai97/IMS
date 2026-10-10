import { Injectable, inject } from '@angular/core';
import { PermissionService } from '@ims/core/identity';
import { ROLE } from '@ims/models';

export const LIST_SCOPE = { Mine: 'mine', All: 'all' } as const;
export type ListScope = (typeof LIST_SCOPE)[keyof typeof LIST_SCOPE];

const STORAGE_KEY_PREFIX = 'ims.documents.scope.';
/** Roles that mostly look after their own documents start on "Mine" (docs/design/mvp-ux.md 4.2, note 1). */
const MINE_FIRST_ROLES: readonly string[] = [ROLE.WarehouseStaff, ROLE.Clerk];

/**
 * The Mine / All choice of the receipt and issue lists: remembered per user in this browser, "Mine" for warehouse
 * staff and clerks and "All" for everyone else until they choose. Storage can throw (private mode), so every access is
 * guarded; the choice then lasts until the page is reloaded.
 */
@Injectable({ providedIn: 'root' })
export class ListScopePreference {
  private readonly permissions = inject(PermissionService);

  initial(): ListScope {
    const stored = this.read();
    if (stored) {
      return stored;
    }
    return MINE_FIRST_ROLES.includes(this.permissions.$role() ?? '')
      ? LIST_SCOPE.Mine
      : LIST_SCOPE.All;
  }

  remember(scope: ListScope): void {
    try {
      localStorage.setItem(this.key(), scope);
    } catch {
      // Not remembered across reloads.
    }
  }

  private read(): ListScope | undefined {
    try {
      const value = localStorage.getItem(this.key());
      return value === LIST_SCOPE.Mine || value === LIST_SCOPE.All ? value : undefined;
    } catch {
      return undefined;
    }
  }

  private key(): string {
    return `${STORAGE_KEY_PREFIX}${this.permissions.$userId() ?? ''}`;
  }
}
