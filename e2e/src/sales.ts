import { apiAs, ifMatch, type PublicId } from './api';
import type { Role } from './config';
import { mainWarehouseId } from './purchasing';

/** Helpers of `45-sales-orders`: every order a scenario makes is cancelled again (an open order stops the module being switched off). */

/** Prefixes of the throw-away rows, so a run that stopped half way can be found again. */
export const CUSTOMER_PREFIX = 'E2E SO Customer ';
export const SKU_PREFIX = 'E2E-SO-';

/** Above the placeholder RM 5,000 rule of the Standard tier, below the RM 20,000 one of the Large tier: a Supervisor approves it. */
export const NEEDS_APPROVAL = { qty: 10, unitPrice: 600 };
/** Below every rule: submitting releases the order at once. */
export const NO_APPROVAL = { qty: 10, unitPrice: 5 };

export interface ApiSalesOrder {
  id: PublicId;
  no: string;
  status: string;
  allowedActions: string[];
  concurrencyToken: string;
  /** Absent (not null) without so.manage. */
  totalAmount?: number;
  lines: {
    lineNo: number;
    qty: number;
    unitPrice?: number;
    deliveredQty: number;
    remainingQty: number;
  }[];
  deliveries: { id: PublicId; no: string; status: string }[];
}

const admin = () => apiAs('admin');

export async function salesIsOn(): Promise<boolean> {
  const list = await admin().call<{ code: string; enabled: boolean }[]>('GET', '/api/modules');
  return list.find(item => item.code === 'sales')?.enabled === true;
}

/** Puts the module in the state asked for (a call that finds it there already does nothing, so nothing is audited twice). */
export async function setSales(enabled: boolean, reason: string): Promise<void> {
  if ((await salesIsOn()) !== enabled) {
    await admin().call('PUT', '/api/modules/sales', { enabled, reason });
  }
}

/** One order with a single line, made by the Clerk (a holder of so.manage), still a Draft. */
export async function createSalesOrder(
  customerId: PublicId,
  productId: PublicId,
  price: { qty: number; unitPrice: number },
  remark?: string,
  role: Role = 'clerk'
): Promise<ApiSalesOrder> {
  return apiAs(role).call<ApiSalesOrder>('POST', '/api/sales-orders', {
    warehouseId: await mainWarehouseId(),
    customerId,
    remark: remark ?? null,
    lines: [
      {
        productId,
        qty: price.qty,
        uomId: null,
        unitPrice: price.unitPrice,
        remark: null
      }
    ]
  });
}

export function getSalesOrder(id: PublicId, role: Role = 'admin'): Promise<ApiSalesOrder> {
  return apiAs(role).call<ApiSalesOrder>('GET', `/api/sales-orders/${id}`);
}

/** A workflow command; without a token the helper reads the order first and sends its current token as If-Match (a wildcard is a 428). */
export function salesCommand(
  role: Role,
  id: PublicId,
  name: 'submit' | 'approve' | 'reject' | 'recall' | 'cancel' | 'close',
  body: object = {},
  token?: string
): Promise<ApiSalesOrder> {
  return apiAs(role).call<ApiSalesOrder>(
    'POST',
    `/api/sales-orders/${id}/${name}`,
    body,
    token === undefined ? {} : ifMatch(token)
  );
}

/** Every order of these scenarios that is still open is cancelled (found by its customer prefix), so the module can be switched off. */
export async function cancelOpenSalesOrders(): Promise<void> {
  for (const status of ['Draft', 'Submitted', 'Approved']) {
    const page = await admin().call<{
      items: { id: PublicId; customerName: string }[];
    }>('GET', `/api/sales-orders?Status=${status}&PageSize=100`);
    for (const item of page.items.filter(row => row.customerName.startsWith(CUSTOMER_PREFIX))) {
      // Cancel is the creator's for a Draft, the submitter's for a Submitted one and a reviewer's for an Approved one.
      for (const role of ['clerk', 'supervisor', 'owner', 'admin'] as const) {
        const result = await apiAs(role).raw('POST', `/api/sales-orders/${item.id}/cancel`, {
          reason: 'E2E: throw-away order'
        });
        if (result.status < 300) {
          break;
        }
      }
    }
  }
}

/** A delivery draft made from an Approved order (`POST /api/sales-orders/{id}/delivery`), as the web's Deliver button makes it. */
export interface ApiDelivery {
  id: PublicId;
  no: string;
  status: string;
  concurrencyToken: string;
  requiresApproval: boolean;
  warehouseId: PublicId;
  partnerId: PublicId | null;
  lines: { productId: PublicId; uomId: PublicId; qty: number; salesOrderLineNo: number }[];
}

export function startDelivery(
  order: Pick<ApiSalesOrder, 'id'>,
  role: Role = 'supervisor'
): Promise<ApiDelivery> {
  return apiAs(role).call<ApiDelivery>('POST', `/api/sales-orders/${order.id}/delivery`);
}

/** Saves the delivery with `qty` on its first line, every field and the order line named again (as the editor does). */
export function saveDelivery(
  draft: ApiDelivery,
  qty: number,
  role: Role = 'supervisor'
): Promise<ApiDelivery> {
  return apiAs(role).call<ApiDelivery>('PUT', `/api/documents/${draft.id}`, {
    warehouseId: draft.warehouseId,
    partnerId: draft.partnerId,
    purpose: 'Sale',
    remark: null,
    lines: draft.lines.map(line => ({
      productId: line.productId,
      qty,
      uomId: line.uomId,
      remark: null,
      salesOrderLineNo: line.salesOrderLineNo
    }))
  });
}

/** Submits (and approves, when a rule holds it) a saved delivery; returns it ready to post. */
export async function submitDelivery(
  draft: ApiDelivery,
  role: Role = 'supervisor'
): Promise<ApiDelivery> {
  const api = apiAs(role);
  let current = await api.command(draft.id, 'submit', {}, draft.concurrencyToken);
  if (current.requiresApproval) {
    current = await apiAs('owner').command(draft.id, 'approve', {}, current.concurrencyToken);
  }
  return current as unknown as ApiDelivery;
}

/** Delivers `qty` of the first line against the order: draft, save, submit, post. Returns the posted delivery's id. */
export async function deliver(
  order: Pick<ApiSalesOrder, 'id'>,
  qty: number,
  onDraft?: (id: PublicId) => void
): Promise<PublicId> {
  const draft = await startDelivery(order);
  onDraft?.(draft.id);
  const saved = await saveDelivery(draft, qty);
  const ready = await submitDelivery(saved);
  await apiAs('supervisor').command(draft.id, 'post', {}, ready.concurrencyToken);
  return draft.id;
}
