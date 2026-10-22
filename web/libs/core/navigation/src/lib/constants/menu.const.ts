import { MODULE_CODE, PERMISSION } from '@ims/models';
import { MenuGroup, MenuItem } from '../types/menu.types';

export const MENU_GROUPS: readonly MenuGroup[] = [
  { id: 'main', labelKey: 'nav.group.main' },
  { id: 'documents', labelKey: 'nav.group.documents' },
  { id: 'stock', labelKey: 'nav.group.stock' },
  { id: 'master', labelKey: 'nav.group.master' },
  { id: 'insight', labelKey: 'nav.group.insight' },
  { id: 'admin', labelKey: 'nav.group.admin' }
];

/** Menu per docs/design/mvp-ux.md 1.2. An item shows when the user holds ANY listed permission. */
export const MENU_ITEMS: readonly MenuItem[] = [
  { id: 'home', labelKey: 'nav.home', icon: 'home', route: '/', permissions: [], group: 'main' },
  {
    id: 'receipts',
    labelKey: 'nav.receipts',
    icon: 'receipt',
    route: '/receipts',
    permissions: [PERMISSION.StockRead],
    group: 'documents'
  },
  {
    id: 'issues',
    labelKey: 'nav.issues',
    icon: 'issue',
    route: '/issues',
    permissions: [PERMISSION.StockRead],
    group: 'documents'
  },
  {
    id: 'purchase-orders',
    labelKey: 'nav.purchaseOrders',
    icon: 'file',
    route: '/purchase-orders',
    permissions: [PERMISSION.PoRead],
    group: 'documents',
    module: MODULE_CODE.Purchasing
  },
  {
    id: 'approvals',
    labelKey: 'nav.approvals',
    icon: 'check-double',
    route: '/approvals',
    permissions: [PERMISSION.DocumentApprove, PERMISSION.DocumentApproveLarge],
    group: 'documents'
  },
  {
    id: 'stock',
    labelKey: 'nav.stock',
    icon: 'stock',
    route: '/stock',
    permissions: [PERMISSION.StockRead],
    group: 'stock'
  },
  {
    id: 'low-stock',
    labelKey: 'nav.lowStock',
    icon: 'low-stock',
    route: '/low-stock',
    permissions: [PERMISSION.StockRead],
    group: 'stock'
  },
  {
    id: 'products',
    labelKey: 'nav.products',
    icon: 'tag',
    route: '/products',
    permissions: [PERMISSION.MasterDataRead],
    group: 'master'
  },
  {
    id: 'catalog',
    labelKey: 'nav.catalog',
    icon: 'list',
    route: '/catalog',
    permissions: [PERMISSION.MasterDataRead],
    group: 'master'
  },
  {
    id: 'warehouses',
    labelKey: 'nav.warehouses',
    icon: 'warehouse',
    route: '/warehouses',
    permissions: [PERMISSION.MasterDataRead],
    group: 'master'
  },
  {
    id: 'partners',
    labelKey: 'nav.partners',
    icon: 'partners',
    route: '/partners',
    permissions: [PERMISSION.MasterDataRead],
    group: 'master'
  },
  {
    id: 'reports',
    labelKey: 'nav.reports',
    icon: 'reports',
    route: '/reports',
    permissions: [PERMISSION.ReportsRead],
    group: 'insight',
    exactMatch: true
  },
  {
    id: 'users',
    labelKey: 'nav.users',
    icon: 'users',
    route: '/users',
    permissions: [PERMISSION.UsersManage],
    group: 'admin'
  },
  {
    id: 'roles',
    labelKey: 'nav.roles',
    icon: 'shield',
    route: '/roles',
    permissions: [PERMISSION.RolesRead],
    group: 'admin'
  },
  {
    id: 'settings',
    labelKey: 'nav.settings',
    icon: 'settings',
    route: '/settings',
    permissions: [PERMISSION.SettingsManage],
    group: 'admin',
    exactMatch: true
  },
  {
    id: 'modules',
    labelKey: 'nav.modules',
    icon: 'settings',
    route: '/settings/modules',
    permissions: [PERMISSION.ModulesManage],
    group: 'admin'
  },
  {
    id: 'audit',
    labelKey: 'nav.audit',
    icon: 'audit',
    route: '/audit-log',
    permissions: [PERMISSION.AuditRead],
    group: 'admin'
  }
];
