import { PermissionDto, PermissionGroup, RoleDto } from '@ims/api';
import { groupPermissions, roleReadOnlyReason, turnOff, turnOn } from './permission-rules';

const permission = (
  code: string,
  requires: string[] = [],
  group = PermissionGroup.Documents,
  adminCore = false
): PermissionDto => ({
  code,
  group,
  description: code,
  isDangerous: false,
  adminCore,
  requires,
  module: undefined
});
// reverse -> post -> read; approve -> read
const CATALOG = [
  permission('users.manage', ['users.read'], PermissionGroup.Administration, true),
  permission('users.read', [], PermissionGroup.Administration, true),
  permission('stock.read', [], PermissionGroup.StockAndReports),
  permission('doc.post', ['stock.read']),
  permission('doc.reverse', ['doc.post']),
  permission('doc.approve', ['stock.read'])
];
const role = (permissions: string[], extra: Partial<RoleDto> = {}): RoleDto => ({
  id: 'r',
  code: 'custom',
  name: 'Custom',
  description: '',
  isSystem: false,
  isActive: true,
  permissions,
  userCount: 0,
  concurrencyToken: 't',
  ...extra
});

describe('groupPermissions', () => {
  it('keeps the catalog order of the sections and of the rows inside them', () => {
    const groups = groupPermissions(CATALOG);
    expect(groups.map(g => g.group)).toEqual([
      PermissionGroup.Administration,
      PermissionGroup.StockAndReports,
      PermissionGroup.Documents
    ]);
    expect(groups[2].permissions.map(p => p.code)).toEqual([
      'doc.post',
      'doc.reverse',
      'doc.approve'
    ]);
  });
});

describe('turnOn (a permission brings what it requires)', () => {
  it('turns on the requirements, however deep, and says which', () => {
    const result = turnOn(new Set(), CATALOG, 'doc.reverse');
    expect([...result.selected].sort()).toEqual(['doc.post', 'doc.reverse', 'stock.read']);
    expect([...result.also].sort()).toEqual(['doc.post', 'stock.read']);
  });

  it('lists a requirement that two needs share only once', () => {
    const shared = [
      permission('a', ['b', 'c']),
      permission('b', ['d']),
      permission('c', ['d']),
      permission('d')
    ];
    const result = turnOn(new Set(), shared, 'a');
    expect([...result.also].sort()).toEqual(['b', 'c', 'd']);
    expect(result.selected.size).toBe(4);
  });

  it('does not report a requirement that is already on, and leaves the input alone', () => {
    const before = new Set(['stock.read']);
    const result = turnOn(before, CATALOG, 'doc.post');
    expect(result.also).toEqual([]);
    expect([...before]).toEqual(['stock.read']);
  });
});

describe('turnOff (a permission takes its dependants with it)', () => {
  it('turns off everything that requires it, however deep', () => {
    const all = new Set(['stock.read', 'doc.post', 'doc.reverse', 'doc.approve']);
    const result = turnOff(all, CATALOG, 'stock.read');
    expect(result.selected.size).toBe(0);
    expect([...result.also].sort()).toEqual(['doc.approve', 'doc.post', 'doc.reverse']);
  });
});

describe('roleReadOnlyReason (why a role cannot be changed here)', () => {
  const context = (over: Record<string, unknown> = {}) => ({
    catalog: CATALOG,
    held: new Set(['stock.read', 'doc.post', 'users.read', 'users.manage']),
    canManage: true,
    ownRoleCode: 'mine',
    ...over
  });

  it('a created role with the whole admin core is editable; a system role with it is locked, for anyone', () => {
    expect(roleReadOnlyReason(role(['users.manage', 'users.read']), context())).toBeUndefined();
    const locked = role(['users.manage', 'users.read'], { isSystem: true });
    expect(roleReadOnlyReason(locked, context())).toBe('locked');
    expect(roleReadOnlyReason(locked, context({ canManage: false }))).toBe('locked');
  });

  it('a person without roles.manage only views', () => {
    expect(roleReadOnlyReason(role([]), context({ canManage: false }))).toBe('viewOnly');
  });

  it('a person cannot change the role they hold', () => {
    expect(roleReadOnlyReason(role([], { code: 'mine' }), context())).toBe('ownRole');
  });

  it('a person cannot change a role that holds more than they do', () => {
    expect(roleReadOnlyReason(role(['stock.read', 'doc.approve']), context())).toBe('holdsMore');
  });
});
