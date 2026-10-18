import { PermissionDto, RoleDto } from '@ims/api';

/**
 * A locked role is a system role that holds every permission flagged `adminCore` (the API's rule, `Role.IsSystem` and the
 * whole core): it refuses a change of its permissions, a rename, a retirement and a deletion. A created role that holds the
 * whole core is still editable. Decided from the data, never from a role name. Without the catalog nothing can be said, so
 * no role is reported as locked.
 */
export function isLockedRole(
  role: RoleDto,
  catalog: readonly PermissionDto[] | undefined
): boolean {
  if (!role.isSystem) {
    return false;
  }
  const core = (catalog ?? []).filter(permission => permission.adminCore);
  if (core.length === 0) {
    return false;
  }
  const held = new Set(role.permissions);
  return core.every(permission => held.has(permission.code));
}

export type RoleKind = 'locked' | 'system' | 'custom';

export function kindOfRole(role: RoleDto, catalog: readonly PermissionDto[] | undefined): RoleKind {
  if (isLockedRole(role, catalog)) {
    return 'locked';
  }
  return role.isSystem ? 'system' : 'custom';
}

/**
 * The order of the list: the standard (system) roles first, the most powerful first (the Administrator, which holds every
 * permission, leads), then the created roles by name. The API sorts by name, which would put Clerk before Owner.
 */
export function sortRoles(roles: readonly RoleDto[]): readonly RoleDto[] {
  return [...roles].sort(
    (a, b) =>
      Number(b.isSystem) - Number(a.isSystem) ||
      (a.isSystem ? b.permissions.length - a.permissions.length : 0) ||
      a.name.localeCompare(b.name)
  );
}
