/** What the signed-in user can do, derived from permission codes only (never from the role name). */
export interface PermissionProfile {
  /** Unit cost, line cost, document totals (`cost.view`). */
  readonly canViewCost: boolean;
  /** Average cost and stock value (`valuation.view`). */
  readonly canViewValuation: boolean;
  /** May approve standard or large documents (`document.approve` or `document.approve.large`). */
  readonly canApprove: boolean;
  /** Creates documents but does not review them: the lists start on "Mine". */
  readonly mineFirst: boolean;
  /** Menu ids for the phone tab bar, in order; the menu still hides what the user may not open. */
  readonly phoneTabs: readonly string[];
}
