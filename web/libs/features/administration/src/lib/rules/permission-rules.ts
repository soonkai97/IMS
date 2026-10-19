import { PermissionDto, PermissionGroup, RoleDto } from '@ims/api';
import { isLockedRole } from './role-rules';

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

export interface ToggleResult {
  readonly selected: ReadonlySet<string>;
  /** Permissions that changed because the clicked one needs them (turned on) or needs it (turned off). */
  readonly also: readonly string[];
}

/** Turning a permission on also turns on what it requires, however deep (`Requires` is closed in the role set). */
export function turnOn(
  selected: ReadonlySet<string>,
  catalog: readonly PermissionDto[],
  code: string
): ToggleResult {
  const next = new Set(selected).add(code);
  const also: string[] = [];
  const pending = [code];
  while (pending.length > 0) {
    const current = pending.pop() as string;
    for (const required of catalog.find(p => p.code === current)?.requires ?? []) {
      // Added at once, so a requirement that two permissions share is listed once.
      if (!next.has(required)) {
        next.add(required);
        also.push(required);
        pending.push(required);
      }
    }
  }
  return { selected: next, also };
}

/** Turning a permission off also turns off everything that requires it, however deep. */
export function turnOff(
  selected: ReadonlySet<string>,
  catalog: readonly PermissionDto[],
  code: string
): ToggleResult {
  const next = new Set(selected);
  next.delete(code);
  const also: string[] = [];
  let changed = true;
  while (changed) {
    changed = false;
    for (const permission of catalog) {
      if (next.has(permission.code) && permission.requires.some(required => !next.has(required))) {
        next.delete(permission.code);
        also.push(permission.code);
        changed = true;
      }
    }
  }
  return { selected: next, also };
}

export function sameSet(a: ReadonlySet<string>, b: ReadonlySet<string>): boolean {
  return a.size === b.size && [...a].every(code => b.has(code));
}

/** The codes the person does not hold: they cannot give these (the API refuses with 403). */
export function notHeld(held: ReadonlySet<string>, codes: Iterable<string>): string[] {
  return [...codes].filter(code => !held.has(code));
}

/** Why a person cannot change a role, when they cannot. The API enforces every one of these; the screen says so first. */
export type RoleReadOnlyReason = 'locked' | 'viewOnly' | 'ownRole' | 'holdsMore';

export interface RoleAccessContext {
  readonly catalog: readonly PermissionDto[] | undefined;
  readonly held: ReadonlySet<string>;
  readonly canManage: boolean;
  /** The code of the role the signed-in person holds (the role cannot be edited by the person who holds it). */
  readonly ownRoleCode: string | undefined;
}

/**
 * Decided from the data and the caller's permissions, never from a role name: the locked role (a system role with the whole
 * administration core), a person without `roles.manage`, the person's own role, and a role that holds more than the person.
 */
export function roleReadOnlyReason(
  role: RoleDto,
  context: RoleAccessContext
): RoleReadOnlyReason | undefined {
  if (isLockedRole(role, context.catalog)) {
    return 'locked';
  }
  if (!context.canManage) {
    return 'viewOnly';
  }
  if (context.ownRoleCode !== undefined && role.code === context.ownRoleCode) {
    return 'ownRole';
  }
  return notHeld(context.held, role.permissions).length > 0 ? 'holdsMore' : undefined;
}
