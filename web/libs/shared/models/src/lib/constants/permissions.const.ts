/** Permission names as returned by GET /api/auth/me (see Ims.Domain Permissions). */
export const PERMISSION = {
  UsersManage: 'users.manage',
  UsersRead: 'users.read',
  MasterDataManage: 'masterdata.manage',
  MasterDataRead: 'masterdata.read',
  /** Create a partner (Clerk, Supervisor, Admin). Editing and deactivating need MasterDataManage. */
  PartnersCreate: 'partners.create',
  DocumentCreate: 'document.create',
  DocumentApprove: 'document.approve',
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

export const ROLE = {
  Admin: 'Admin',
  Owner: 'Owner',
  Supervisor: 'Supervisor',
  WarehouseStaff: 'WarehouseStaff',
  Clerk: 'Clerk'
} as const;

export const ALL_ROLES = [
  ROLE.Admin,
  ROLE.Owner,
  ROLE.Supervisor,
  ROLE.WarehouseStaff,
  ROLE.Clerk
] as const;

/** Roles that see every warehouse; the others only their assigned ones. */
export const ALL_WAREHOUSE_ROLES: readonly string[] = [ROLE.Admin, ROLE.Owner];
