import { PermissionDto, PermissionGroup } from '@ims/api';
import { groupPermissions } from './permission-rules';

const permission = (code: string, group: PermissionGroup): PermissionDto => ({
  code,
  group,
  description: code,
  isDangerous: false,
  adminCore: false,
  requires: []
});

describe('groupPermissions', () => {
  it('keeps the catalog order of the sections and of the rows inside them', () => {
    const groups = groupPermissions([
      permission('users.read', PermissionGroup.Administration),
      permission('stock.read', PermissionGroup.StockAndReports),
      permission('doc.post', PermissionGroup.Documents),
      permission('users.manage', PermissionGroup.Administration),
      permission('doc.reverse', PermissionGroup.Documents)
    ]);

    expect(groups.map(g => g.group)).toEqual([
      PermissionGroup.Administration,
      PermissionGroup.StockAndReports,
      PermissionGroup.Documents
    ]);
    expect(groups[0].permissions.map(p => p.code)).toEqual(['users.read', 'users.manage']);
    expect(groups[2].permissions.map(p => p.code)).toEqual(['doc.post', 'doc.reverse']);
  });

  it('is empty for an empty catalog', () => {
    expect(groupPermissions([])).toEqual([]);
  });
});
