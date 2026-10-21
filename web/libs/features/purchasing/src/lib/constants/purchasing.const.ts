import { PurchaseOrderStatus } from '@ims/api';
import type { StatusTone } from '@ims/ui';

/** First segment of the purchase-order pages (list at `/purchase-orders`, detail at `/purchase-orders/:id`). */
export const PURCHASE_ORDER_ROUTE = 'purchase-orders';

export const PO_STATUS_TONE: Readonly<Record<PurchaseOrderStatus, StatusTone>> = {
  [PurchaseOrderStatus.Draft]: 'draft',
  [PurchaseOrderStatus.Submitted]: 'submitted',
  [PurchaseOrderStatus.Approved]: 'approved',
  [PurchaseOrderStatus.Closed]: 'posted',
  [PurchaseOrderStatus.Cancelled]: 'cancelled'
};
