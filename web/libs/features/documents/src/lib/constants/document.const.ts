import { DocumentStatus, DocumentType } from '@ims/api';
import type { StatusTone } from '@ims/ui';

export const DOCUMENT_ROUTE: Readonly<Record<DocumentType, string>> = {
  [DocumentType.Receipt]: 'receipts',
  [DocumentType.Issue]: 'issues',
  [DocumentType.Transfer]: 'receipts',
  [DocumentType.Adjust]: 'receipts'
};

export const STATUS_TONE: Readonly<Record<DocumentStatus, StatusTone>> = {
  [DocumentStatus.Draft]: 'draft',
  [DocumentStatus.Submitted]: 'submitted',
  [DocumentStatus.Approved]: 'approved',
  [DocumentStatus.Posted]: 'posted',
  [DocumentStatus.Reversed]: 'reversed',
  [DocumentStatus.Cancelled]: 'cancelled'
};

/** Number prefix of issue documents ("ISS-WH01-2610-0012"); anything else is a receipt-side number. */
const ISSUE_NUMBER_PREFIX = 'ISS';

export function routeForDocumentNo(documentNo: string): string {
  return documentNo.startsWith(ISSUE_NUMBER_PREFIX)
    ? DOCUMENT_ROUTE[DocumentType.Issue]
    : DOCUMENT_ROUTE[DocumentType.Receipt];
}
