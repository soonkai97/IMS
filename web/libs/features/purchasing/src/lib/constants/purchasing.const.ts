import { PurchaseOrderStatus } from '@ims/api';
import type { StatusTone } from '@ims/ui';

/** First segment of the purchase-order pages (list at `/purchase-orders`, detail at `/purchase-orders/:id`). */
export const PURCHASE_ORDER_ROUTE = 'purchase-orders';

/** Action names in `allowedActions` of a purchase order. The server decides; the screen only renders what it names. */
export const PO_ACTION = {
  Edit: 'edit',
  Submit: 'submit',
  Recall: 'recall',
  Approve: 'approve',
  Reject: 'reject',
  Cancel: 'cancel',
  /** Close short: only after something was received (the server offers it then). */
  Close: 'close',
  /** Start a draft receipt from the order: offered on an Approved order with something left to receive, to whoever may create receipts and read orders. */
  Receive: 'receive'
} as const;

export type PoAction = (typeof PO_ACTION)[keyof typeof PO_ACTION];

/** The supplier copy is for an order that was approved (and one that was then closed): not for a Draft, Submitted or Cancelled one. */
export const isPrintable = (status: PurchaseOrderStatus): boolean =>
  status === PurchaseOrderStatus.Approved || status === PurchaseOrderStatus.Closed;

export const PO_STATUS_TONE: Readonly<Record<PurchaseOrderStatus, StatusTone>> = {
  [PurchaseOrderStatus.Draft]: 'draft',
  [PurchaseOrderStatus.Submitted]: 'submitted',
  [PurchaseOrderStatus.Approved]: 'approved',
  [PurchaseOrderStatus.Closed]: 'posted',
  [PurchaseOrderStatus.Cancelled]: 'cancelled'
};
