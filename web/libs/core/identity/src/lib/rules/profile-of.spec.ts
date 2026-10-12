import { PERMISSION } from '@ims/models';
import { profileOf } from './profile-of';

const P = PERMISSION;

/** The five system roles with the permissions the API gives them today (Permissions.cs). */
const STAFF = [P.MasterDataRead, P.DocumentCreate, P.DocumentPost, P.StockRead];
const CLERK = [P.MasterDataRead, P.PartnersCreate, P.DocumentCreate, P.StockRead, P.CostView];
const SUPERVISOR = [
  P.MasterDataManage,
  P.MasterDataRead,
  P.PartnersCreate,
  P.DocumentCreate,
  P.DocumentApprove,
  P.DocumentPost,
  P.DocumentReverse,
  P.StockRead,
  P.ReportsRead,
  P.CostView,
  P.ValuationView
];
const OWNER = [
  P.MasterDataRead,
  P.DocumentApprove,
  P.StockRead,
  P.ReportsRead,
  P.AuditRead,
  P.CostView,
  P.ValuationView
];
const ADMIN = Object.values(P).filter(code => code !== P.DocumentApproveLarge);

describe('profileOf', () => {
  it.each([
    ['Warehouse staff', STAFF, ['home', 'receipts', 'issues', 'stock'], true, false, false, false],
    ['Clerk', CLERK, ['home', 'receipts', 'issues', 'stock'], true, true, false, false],
    ['Supervisor', SUPERVISOR, ['home', 'approvals', 'receipts', 'stock'], false, true, true, true],
    ['Owner', OWNER, ['home', 'approvals', 'stock', 'reports'], false, true, true, true],
    ['Admin', ADMIN, ['home', 'approvals', 'receipts', 'stock'], false, true, true, true]
  ])(
    "%s gets today's tabs and defaults",
    (_name, codes, tabs, mineFirst, canViewCost, canViewValuation, canApprove) => {
      const profile = profileOf(new Set(codes));

      expect(profile.phoneTabs).toEqual(tabs);
      expect(profile.mineFirst).toBe(mineFirst);
      expect(profile.canViewCost).toBe(canViewCost);
      expect(profile.canViewValuation).toBe(canViewValuation);
      expect(profile.canApprove).toBe(canApprove);
    }
  );

  it('cost is hidden from staff and valuation from the clerk, by permission alone', () => {
    expect(profileOf(new Set(STAFF))).toMatchObject({
      canViewCost: false,
      canViewValuation: false
    });
    expect(profileOf(new Set(CLERK))).toMatchObject({ canViewCost: true, canViewValuation: false });
  });

  it('a reviewer who holds only the large tier can approve and still gets the approvals tab', () => {
    const profile = profileOf(new Set([P.DocumentApproveLarge, P.StockRead, P.ReportsRead]));

    expect(profile.canApprove).toBe(true);
    expect(profile.phoneTabs).toEqual(['home', 'approvals', 'stock', 'reports']);
  });

  it('a custom permission set gets its tabs from the permissions', () => {
    const night = profileOf(new Set([P.DocumentCreate, P.DocumentApprove, P.StockRead]));
    expect(night.phoneTabs).toEqual(['home', 'approvals', 'receipts', 'stock']);
    expect(night.mineFirst).toBe(false);

    expect(profileOf(new Set([P.StockRead])).phoneTabs).toEqual(['home', 'stock', 'reports']);
    expect(profileOf(new Set()).canApprove).toBe(false);
  });
});
