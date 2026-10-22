import { ReorderSuggestion } from '@ims/api';
import { isValidQuantity } from '@ims/features/documents';
import { PublicId } from '@ims/models';
import { roundMoney, roundQuantity } from '@ims/utils';
import { MAX_PREFILL_LINES } from '../form/po-form';
import { PURCHASE_ORDER_ROUTE } from '../constants/purchasing.const';

/** A line chosen on the reorder screen: the product code and the quantity in the box (the person may have changed it). */
export interface ReorderLine {
  readonly sku: string;
  readonly qty: number;
}

export interface ReorderEntryLink {
  readonly commands: readonly string[];
  readonly queryParams: {
    readonly warehouseId: PublicId;
    readonly supplierId: PublicId;
    readonly line: readonly string[];
  };
  /** Chosen lines that did not fit (the editor takes MAX_PREFILL_LINES). */
  readonly omitted: number;
}

/** The quantity rule of the editor (above 0, up to 4 decimals, capped); a blank or unreadable box is null. */
export const isValidReorderQty = (qty: number | null | undefined): qty is number =>
  qty !== null && qty !== undefined && isValidQuantity(qty);

/**
 * `/purchase-orders/new?warehouseId=..&supplierId=..&line=SKU|12&line=..` for the editor. The router encodes every value (a code
 * with `&`, `#`, `%` or a space stays one code); lines that are not valid quantities are left out, at most MAX_PREFILL_LINES go.
 */
export function buildReorderEntryLink(
  warehouseId: PublicId,
  supplierId: PublicId,
  lines: readonly ReorderLine[]
): ReorderEntryLink {
  const valid = lines.filter(line => line.sku.trim() !== '' && isValidReorderQty(line.qty));
  const kept = valid.slice(0, MAX_PREFILL_LINES);
  return {
    commands: ['/', PURCHASE_ORDER_ROUTE, 'new'],
    queryParams: {
      warehouseId,
      supplierId,
      line: kept.map(line => `${line.sku}|${roundQuantity(line.qty)}`)
    },
    omitted: valid.length - kept.length
  };
}

/**
 * The value shown beside a row: the server's figure for the suggested quantity, scaled to the quantity in the box (same average
 * cost). Null when the server sent none (no cost.view, never stocked) or the box holds no valid quantity.
 */
export function reorderValue(
  row: ReorderSuggestion,
  qty: number | null | undefined
): number | null {
  const value = row.suggestedValue ?? null;
  if (value === null || !isValidReorderQty(qty)) {
    return null;
  }
  if (qty === row.suggestedQty) {
    return value;
  }
  return row.suggestedQty > 0 ? roundMoney((value * qty) / row.suggestedQty) : null;
}
