import { PermissionDto, PermissionGroup, RoleDto } from '@ims/api';
import { isLockedRole, kindOfRole, sortRoles } from './role-rules';

const permission = (code: string, adminCore = false): PermissionDto => ({
  code,
  group: PermissionGroup.Administration,
  description: code,
  isDangerous: false,
  adminCore,
  requires: [],
  module: undefined
});
const CATALOG = [
  permission('users.manage', true),
  permission('roles.manage', true),
  permission('stock.read')
];
const role = (permissions: string[], isSystem = false): RoleDto => ({
  id: 'r',
  code: 'x',
  name: 'x',
  description: '',
  isSystem,
  isActive: true,
  permissions,
  userCount: 0,
  concurrencyToken: 't'
});

describe('role rules (a locked role is decided by its permissions, not by its name)', () => {
  it('a role holding the whole admin core is locked, even with another name', () => {
    expect(kindOfRole(role(['users.manage', 'roles.manage', 'stock.read'], true), CATALOG)).toBe(
      'locked'
    );
  });

  it('a role holding part of the core is not locked', () => {
    expect(isLockedRole(role(['users.manage']), CATALOG)).toBe(false);
  });

  it('a standard role below the core is system, a created one is custom', () => {
    expect(kindOfRole(role(['stock.read'], true), CATALOG)).toBe('system');
    expect(kindOfRole(role(['stock.read']), CATALOG)).toBe('custom');
  });

  it('a created role that holds the whole admin core is custom and not locked (the API still edits it)', () => {
    const created = role(['users.manage', 'roles.manage', 'stock.read'], false);

    expect(isLockedRole(created, CATALOG)).toBe(false);
    expect(kindOfRole(created, CATALOG)).toBe('custom');
  });

  it('without the catalog no role is reported as locked', () => {
    expect(isLockedRole(role(['users.manage', 'roles.manage']), undefined)).toBe(false);
    expect(isLockedRole(role(['users.manage']), [permission('stock.read')])).toBe(false);
  });

  it('lists the standard roles first, the most powerful first, then the created roles by name', () => {
    const named = (name: string, permissions: string[], isSystem: boolean) => ({
      ...role(permissions, isSystem),
      name
    });
    const sorted = sortRoles([
      named('Zed', ['a', 'b', 'c', 'd'], false),
      named('Clerk', ['a', 'b'], true),
      named('Alpha', [], false),
      named('Owner', ['a', 'b', 'c'], true),
      named('Chief', ['a', 'b', 'c', 'd', 'e'], true)
    ]);

    expect(sorted.map(r => r.name)).toEqual(['Chief', 'Owner', 'Clerk', 'Alpha', 'Zed']);
  });
});
