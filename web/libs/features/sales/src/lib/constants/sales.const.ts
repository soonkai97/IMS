import { SalesOrderStatus } from '@ims/api';
import type { StatusTone } from '@ims/ui';

/** First segment of the sales-order pages (list at `/sales-orders`, detail at `/sales-orders/:id`). */
export const SALES_ORDER_ROUTE = 'sales-orders';

/** Action names in `allowedActions` of a sales order. The server decides; the screen only renders what it names. */
export const SO_ACTION = {
  Edit: 'edit',
  Submit: 'submit',
  Recall: 'recall',
  Approve: 'approve',
  Reject: 'reject',
  Cancel: 'cancel',
  /** Close short: only after something was delivered. */
  Close: 'close',
  /** Start a draft delivery from the order. */
  Deliver: 'deliver'
} as const;

export type SoAction = (typeof SO_ACTION)[keyof typeof SO_ACTION];

/**
 * Actions whose screen is a later slice: the editor (PL3.9) and the delivery editor with its order banner (PL3.10). The API may
 * offer them already; the button is not rendered until the screen exists (a button to a page that is not there, or a draft
 * delivery that cannot be saved, is worse than none). PL3.9 and PL3.10 each remove their entry.
 */
export const SO_ACTIONS_NOT_BUILT: ReadonlySet<SoAction> = new Set([
  SO_ACTION.Edit,
  SO_ACTION.Deliver
]);

const KNOWN_ACTIONS: readonly string[] = Object.values(SO_ACTION);

/** Server `allowedActions` -> typed set. A name this web does not know (a newer API) is ignored, never guessed. */
export function toSoActions(raw: readonly string[] | undefined): ReadonlySet<SoAction> {
  return new Set((raw ?? []).filter((name): name is SoAction => KNOWN_ACTIONS.includes(name)));
}

export const SO_STATUS_TONE: Readonly<Record<SalesOrderStatus, StatusTone>> = {
  [SalesOrderStatus.Draft]: 'draft',
  [SalesOrderStatus.Submitted]: 'submitted',
  [SalesOrderStatus.Approved]: 'approved',
  [SalesOrderStatus.Closed]: 'posted',
  [SalesOrderStatus.Cancelled]: 'cancelled'
};
