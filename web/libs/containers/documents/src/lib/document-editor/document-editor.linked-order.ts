import { PurchaseOrderCloseKind, PurchaseOrderDto, PurchaseOrderStatus } from '@ims/api';
import { MessageKey } from '@ims/core/i18n';
import { LineValue, OrderLineContext } from '@ims/features/documents';
import { ApiError, PublicId } from '@ims/models';
import { isDefined } from '@ims/utils';

/** What the order says about each of its lines, by line number. Empty until the order of THIS receipt has arrived. */
export function orderLineContexts(
  order: PurchaseOrderDto | undefined,
  orderId: PublicId | undefined
): ReadonlyMap<number, OrderLineContext> {
  if (!order || order.id !== orderId) {
    return new Map();
  }
  return new Map(
    order.lines.map(line => [
      line.lineNo,
      {
        ordered: line.qty,
        received: line.receivedQty,
        remaining: line.remainingQty,
        uomCode: line.uomCode
      }
    ])
  );
}

/**
 * Where the order stands, in words: closed because everything arrived or closed short (`closeKind`), or still open with this many
 * lines left. The remaining quantity is NOT zeroed when an order is closed, so it says nothing once `closeKind` is set.
 */
export function orderProgress(
  order: PurchaseOrderDto
): { key: MessageKey; open: number } | undefined {
  // The API sends null, not an absent member, for an order that is still open.
  if (isDefined(order.closeKind)) {
    const received = order.closeKind === PurchaseOrderCloseKind.Received;
    return { key: received ? 'doc.order.closed' : 'doc.order.closedShort', open: 0 };
  }
  return order.status === PurchaseOrderStatus.Approved
    ? {
        key: 'doc.order.remaining',
        open: order.lines.filter(line => line.remainingQty > 0).length
      }
    : undefined;
}

export interface LineRefusal {
  readonly sku: string;
  readonly text: string;
}

const LINE_FIELD = /^lines\[(\d+)\]/;

/** The 400 sentences about one line ("lines[0].qty": Remaining 4 (ordered 10, received 6)), each with the line's SKU, once. */
export function lineRefusals(
  errors: readonly (ApiError | undefined)[],
  lines: readonly LineValue[]
): readonly LineRefusal[] {
  const seen = new Set<string>();
  const refusals: LineRefusal[] = [];
  for (const error of errors) {
    for (const [field, messages] of Object.entries(error?.fieldErrors ?? {})) {
      const index = LINE_FIELD.exec(field)?.[1];
      for (const text of index === undefined ? [] : messages) {
        const sku = lines[Number(index)]?.sku ?? '';
        if (!seen.has(`${sku}|${text}`)) {
          seen.add(`${sku}|${text}`);
          refusals.push({ sku, text });
        }
      }
    }
  }
  return refusals;
}
