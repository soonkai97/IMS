import { PERMISSION } from '@ims/models';
import { PHONE_TAB } from '../constants/phone-tab.const';
import { PermissionProfile } from '../types/permission-profile.types';

/**
 * Derives the profile from permission codes. Screens and menus ask the profile; no code compares a role name
 * (docs/design/2026-refresh-plan.md D5), so a custom role gets the right tabs and defaults without a new rule.
 */
export function profileOf(permissions: ReadonlySet<string>): PermissionProfile {
  const canApprove =
    permissions.has(PERMISSION.DocumentApprove) || permissions.has(PERMISSION.DocumentApproveLarge);
  const canCreate = permissions.has(PERMISSION.DocumentCreate);

  const phoneTabs: string[] = [PHONE_TAB.Home];
  if (canApprove) {
    phoneTabs.push(PHONE_TAB.Approvals);
  }
  if (canCreate) {
    phoneTabs.push(PHONE_TAB.Receipts);
  }
  if (canCreate && !canApprove) {
    phoneTabs.push(PHONE_TAB.Issues);
  }
  phoneTabs.push(PHONE_TAB.Stock);
  if (!canCreate) {
    phoneTabs.push(PHONE_TAB.Reports);
  }

  return {
    canViewCost: permissions.has(PERMISSION.CostView),
    canViewValuation: permissions.has(PERMISSION.ValuationView),
    canApprove,
    mineFirst: canCreate && !canApprove,
    phoneTabs
  };
}
