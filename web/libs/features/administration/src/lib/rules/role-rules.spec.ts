import { PermissionDto, PermissionGroup, RoleDto } from '@ims/api';
import { isLockedRole, kindOfRole } from './role-rules';

const permission = (code: string, adminCore = false): PermissionDto => ({
  code,
  group: PermissionGroup.Administration,
  description: code,
  isDangerous: false,
  adminCore,
  requires: []
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

  it('without the catalog no role is reported as locked', () => {
    expect(isLockedRole(role(['users.manage', 'roles.manage']), undefined)).toBe(false);
    expect(isLockedRole(role(['users.manage']), [permission('stock.read')])).toBe(false);
  });
});
