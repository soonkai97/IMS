import { apiAs, type PublicId } from './api';
import type { Role } from './config';

/** Helpers of `42-purchase-orders`: every order a scenario makes is cancelled again (an open order stops the module being switched off). */

/** The first calls after the API started are slow: allow 30 s. */
export const SLOW = 30_000;
/** Prefixes of the throw-away rows, so a run that stopped half way can be found again. */
export const SUPPLIER_PREFIX = 'E2E PO Supplier ';
export const SKU_PREFIX = 'E2E-PO-';

/** Above the placeholder RM 5,000 rule of the Standard tier, below the RM 20,000 one of the Large tier: a Supervisor approves it. */
export const NEEDS_APPROVAL = { qty: 10, unitPrice: 600 };
/** Below every rule: submitting releases the order at once. */
export const NO_APPROVAL = { qty: 10, unitPrice: 5 };

export interface ApiOrder {
  id: PublicId;
  no: string;
  status: string;
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

/** A workflow command with the wildcard If-Match (the screens send the real token; these are set-up and clean-up calls). */
export function command(
  role: Role,
  id: PublicId,
  name: 'submit' | 'approve' | 'reject' | 'recall' | 'cancel' | 'close',
  body: object = {}
): Promise<ApiOrder> {
  return apiAs(role).call<ApiOrder>('POST', `/api/purchase-orders/${id}/${name}`, body);
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
