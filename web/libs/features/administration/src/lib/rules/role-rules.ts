import { PermissionDto, RoleDto } from '@ims/api';

/**
 * A locked role holds every permission flagged `adminCore` (the API refuses to change its permissions, to rename it, to
 * retire it or to delete it). Decided from the permissions, never from a role name; only the Administrator holds the
 * whole core. Without the catalog nothing can be said, so no role is reported as locked.
 */
export function isLockedRole(
  role: RoleDto,
  catalog: readonly PermissionDto[] | undefined
): boolean {
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
