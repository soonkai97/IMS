import { MessageKey } from './message.types';

/** Server enum value -> message key. Use with I18nService.enumLabel(). */
type LabelMap = Readonly<Record<string, MessageKey>>;

export const ROLE_LABELS: LabelMap = {
  Admin: 'role.Admin',
  Owner: 'role.Owner',
  Supervisor: 'role.Supervisor',
  WarehouseStaff: 'role.WarehouseStaff',
  Clerk: 'role.Clerk'
};

export const DOCUMENT_STATUS_LABELS: LabelMap = {
  Draft: 'status.Draft',
  Submitted: 'status.Submitted',
  Approved: 'status.Approved',
  Posted: 'status.Posted',
  Reversed: 'status.Reversed',
  Cancelled: 'status.Cancelled'
};

export const DOCUMENT_TYPE_LABELS: LabelMap = {
  Receipt: 'docType.Receipt',
  Issue: 'docType.Issue',
  Transfer: 'docType.Transfer',
  Adjust: 'docType.Adjust'
};

export const ISSUE_PURPOSE_LABELS: LabelMap = {
  Sale: 'purpose.Sale',
  Use: 'purpose.Use',
  Scrap: 'purpose.Scrap'
};

export const PARTNER_TYPE_LABELS: LabelMap = {
  Supplier: 'partnerType.Supplier',
  Customer: 'partnerType.Customer'
};

export const LEDGER_REASON_LABELS: LabelMap = {
  Receipt: 'ledgerReason.Receipt',
  Issue: 'ledgerReason.Issue',
  TransferOut: 'ledgerReason.TransferOut',
  TransferIn: 'ledgerReason.TransferIn',
  Adjustment: 'ledgerReason.Adjustment',
  Reversal: 'ledgerReason.Reversal',
  Opening: 'ledgerReason.Opening'
};

/** Approval rule tier (the server's ApproverTier) -> the plain words the Settings form shows. */
export const APPROVER_TIER_LABELS: LabelMap = {
  Standard: 'rule.tier.Standard',
  Large: 'rule.tier.Large'
};
