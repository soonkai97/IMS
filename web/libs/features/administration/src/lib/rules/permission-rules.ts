import { PermissionDto, PermissionGroup } from '@ims/api';

export interface PermissionGroupView {
  readonly group: PermissionGroup;
  readonly permissions: readonly PermissionDto[];
}

/**
 * The catalog by section. The API lists the permissions in the order of the role editor, so the sections and the rows keep
 * the order of first appearance; nothing here knows a section or a permission by name.
 */
export function groupPermissions(
  catalog: readonly PermissionDto[]
): readonly PermissionGroupView[] {
  const groups = new Map<PermissionGroup, PermissionDto[]>();
  for (const permission of catalog) {
    const rows = groups.get(permission.group) ?? [];
    rows.push(permission);
    groups.set(permission.group, rows);
  }
  return [...groups].map(([group, permissions]) => ({ group, permissions }));
}
