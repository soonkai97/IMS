import { PurchaseOrderCloseKind, PurchaseOrderDto } from '@ims/api';
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

/** Where the order stands: closed because everything arrived (the API says so in `closeKind`), or how many lines still have quantity to receive. */
export function orderProgress(order: PurchaseOrderDto): { closed: boolean; open: number } {
  return {
    // The API sends null, not an absent member, for an order that is still open.
    closed: isDefined(order.closeKind) && order.closeKind === PurchaseOrderCloseKind.Received,
    open: order.lines.filter(line => line.remainingQty > 0).length
  };
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
