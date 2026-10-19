/** Permission names as returned by GET /api/auth/me (see Ims.Domain Permissions). */
export const PERMISSION = {
  UsersManage: 'users.manage',
  UsersRead: 'users.read',
  /** See the roles and what they allow (read-only screens). */
  RolesRead: 'roles.read',
  /** Create, change, retire and delete roles. */
  RolesManage: 'roles.manage',
  MasterDataManage: 'masterdata.manage',
  MasterDataRead: 'masterdata.read',
  /** Create a partner (Clerk, Supervisor, Admin). Editing and deactivating need MasterDataManage. */
  PartnersCreate: 'partners.create',
  DocumentCreate: 'document.create',
  DocumentApprove: 'document.approve',
  /** Approve large documents (second approval tier; the API adds it with the configurable roles, see the roles ADR). */
  DocumentApproveLarge: 'document.approve.large',
  DocumentPost: 'document.post',
  DocumentReverse: 'document.reverse',
  StockRead: 'stock.read',
  ReportsRead: 'reports.read',
  AuditRead: 'audit.read',
  SettingsManage: 'settings.manage',
  /** Unit cost, line cost, document totals and approval amounts (Clerk, Supervisor, Owner, Admin). */
  CostView: 'cost.view',
  /** Average cost, stock value, the stock total and ledger cost (Supervisor, Owner, Admin). */
  ValuationView: 'valuation.view'
} as const;
