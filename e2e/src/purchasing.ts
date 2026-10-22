import { apiAs, ifMatch, type ApiDocument, type PublicId } from './api';
import type { Role } from './config';

/** Helpers of `42-purchase-orders`: every order a scenario makes is cancelled again (an open order stops the module being switched off). */

/** The first calls after the API started are slow: allow 30 s. */
export const SLOW = 30_000;
/** Prefixes of the throw-away rows, so a run that stopped half way can be found again. */
export const SUPPLIER_PREFIX = 'E2E PO Supplier ';
export const SKU_PREFIX = 'E2E-PO-';

/** Above the placeholder RM 5,000 rule of the Standard tier, below the RM 20,000 one of the Large tier: a Supervisor approves it. */
export const NEEDS_APPROVAL = { qty: 10, unitPrice: 600 };
/** Above the placeholder RM 20,000 rule of the Large tier: only the Owner approves it. */
export const LARGE = { qty: 10, unitPrice: 2100 };
/** Below every rule: submitting releases the order at once. */
export const NO_APPROVAL = { qty: 10, unitPrice: 5 };

export interface ApiOrder {
  id: PublicId;
  no: string;
  status: string;
  warehouseId: PublicId;
  allowedActions: string[];
  concurrencyToken: string;
  requiresApproval: boolean;
  totalAmount: number | null;
  lines: {
    lineNo: number;
    productId: PublicId;
    uomId: PublicId;
    qty: number;
    unitPrice: number | null;
    receivedQty: number;
    remainingQty: number;
  }[];
  receipts: { id: PublicId; no: string; status: string }[];
}

const admin = () => apiAs('admin');

export async function purchasingIsOn(): Promise<boolean> {
  const list = await admin().call<{ code: string; enabled: boolean }[]>('GET', '/api/modules');
  return list.find(item => item.code === 'purchasing')?.enabled === true;
}

/** Puts the module in the state asked for (a call that finds it there already does nothing, so nothing is audited twice). */
export async function setPurchasing(enabled: boolean, reason: string): Promise<void> {
  if ((await purchasingIsOn()) !== enabled) {
    await admin().call('PUT', '/api/modules/purchasing', { enabled, reason });
  }
}

export async function mainWarehouseId(): Promise<PublicId> {
  const list = await admin().call<{ id: PublicId; name: string }[]>('GET', '/api/warehouses');
  return (list.find(warehouse => /main/i.test(warehouse.name)) ?? list[0])!.id;
}

/** One order with a single line, made by the Clerk (a holder of po.manage), still a Draft. */
export async function createOrder(
  supplierId: PublicId,
  productId: PublicId,
  price: { qty: number; unitPrice: number },
  remark?: string,
  role: Role = 'clerk'
): Promise<ApiOrder> {
  return apiAs(role).call<ApiOrder>('POST', '/api/purchase-orders', {
    warehouseId: await mainWarehouseId(),
    supplierId,
    remark: remark ?? null,
    lines: [{ productId, qty: price.qty, uomId: null, unitPrice: price.unitPrice, remark: null }]
  });
}

export function getOrder(id: PublicId, role: Role = 'admin'): Promise<ApiOrder> {
  return apiAs(role).call<ApiOrder>('GET', `/api/purchase-orders/${id}`);
}

/** A workflow command; without a token the helper reads the order first and sends its current token as If-Match (a wildcard is a 428). */
export function command(
  role: Role,
  id: PublicId,
  name: 'submit' | 'approve' | 'reject' | 'recall' | 'cancel' | 'close',
  body: object = {},
  token?: string
): Promise<ApiOrder> {
  return apiAs(role).call<ApiOrder>(
    'POST',
    `/api/purchase-orders/${id}/${name}`,
    body,
    token === undefined ? {} : ifMatch(token)
  );
}

/** Submitted by the Clerk and, when an approval rule holds it, approved by the Supervisor: an Approved order. */
export async function approvedOrder(
  supplierId: PublicId,
  productId: PublicId,
  price = NEEDS_APPROVAL
): Promise<ApiOrder> {
  const order = await createOrder(supplierId, productId, price);
  const submitted = await command('clerk', order.id, 'submit');
  return submitted.status === 'Approved' ? submitted : command('supervisor', order.id, 'approve');
}

/** A draft receipt made from an Approved order, as a role (the Receive button of the web makes the same call). */
export function receiptFromOrder(
  order: Pick<ApiOrder, 'id'>,
  role: Role = 'supervisor'
): Promise<ReceiptDraft> {
  return apiAs(role).call<ReceiptDraft>('POST', `/api/purchase-orders/${order.id}/receipt`);
}

/** Receives `qty` of the first line against the order: a draft receipt from the order, set to `qty`, submitted, approved if held, posted. */
export async function receive(
  order: ApiOrder,
  qty: number,
  /** Told the draft's id at once, so a clean-up can find it even when a later step fails. */
  onDraft?: (id: PublicId) => void
): Promise<ApiDocument> {
  const supervisor = apiAs('supervisor');
  const draft = await supervisor.call<ReceiptDraft>(
    'POST',
    `/api/purchase-orders/${order.id}/receipt`
  );
  onDraft?.(draft.id);
  const saved = await supervisor.call<ApiDocument>('PUT', `/api/documents/${draft.id}`, {
    warehouseId: draft.warehouseId,
    partnerId: draft.partnerId,
    purpose: null,
    remark: null,
    lines: draft.lines.map(line => ({
      productId: line.productId,
      qty,
      uomId: line.uomId,
      unitCost: line.unitCost ?? undefined,
      remark: null,
      purchaseOrderLineNo: line.purchaseOrderLineNo
    }))
  });
  let current = await supervisor.command(draft.id, 'submit', {}, saved.concurrencyToken);
  if (current.requiresApproval) {
    current = await apiAs('owner').command(draft.id, 'approve', {}, current.concurrencyToken);
  }
  return supervisor.command(draft.id, 'post', {}, current.concurrencyToken);
}

export interface ReceiptDraft {
  id: PublicId;
  no: string;
  partnerId: PublicId | null;
  warehouseId: PublicId;
  lines: {
    productId: PublicId;
    uomId: PublicId;
    unitCost: number | null;
    purchaseOrderLineNo: number;
  }[];
}

/** Every order of these scenarios that is still open is cancelled (found by its supplier prefix), so the module can be switched off. */
export async function cancelOpenOrders(): Promise<void> {
  for (const status of ['Draft', 'Submitted', 'Approved']) {
    const page = await admin().call<{ items: { id: PublicId; supplierName: string }[] }>(
      'GET',
      `/api/purchase-orders?Status=${status}&PageSize=100`
    );
    for (const item of page.items.filter(row => row.supplierName.startsWith(SUPPLIER_PREFIX))) {
      // Cancel is the creator's for a Draft, the submitter's for a Submitted one and a reviewer's for an Approved one.
      for (const role of ['clerk', 'supervisor', 'owner', 'admin'] as const) {
        const result = await apiAs(role).raw('POST', `/api/purchase-orders/${item.id}/cancel`, {
          reason: 'E2E: throw-away order'
        });
        if (result.status < 300) {
          break;
        }
      }
    }
  }
}

/** Puts a throw-away receipt out of the way by its state: a posted one is reversed, an unfinished one cancelled. */
export async function retireReceipt(id: PublicId): Promise<void> {
  const { status } = await admin().getDocument(id);
  if (status === 'Posted') {
    await admin().reverse(id, 'E2E: throw-away receipt');
  } else if (['Draft', 'Submitted', 'Approved'].includes(status)) {
    for (const role of ['supervisor', 'admin'] as const) {
      if ((await apiAs(role).raw('POST', `/api/documents/${id}/cancel`, {})).status < 300) {
        return;
      }
    }
  }
}
