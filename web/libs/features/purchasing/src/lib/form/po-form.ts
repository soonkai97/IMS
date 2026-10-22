import { FormControl, FormGroup, Validators } from '@angular/forms';
import { PurchaseOrderDto, PurchaseOrderLineDto, PurchaseOrderSaveRequest } from '@ims/api';
import { LineValue, isValidQuantity } from '@ims/features/documents';
import { MAX_REMARK_LENGTH, PublicId } from '@ims/models';
import { toNumber } from '@ims/utils';

/** A link that opens the editor with lines (the reorder screen) names at most this many; the rest is ignored. */
export const MAX_PREFILL_LINES = 50;
/** Separates the product code from the quantity in one `line` query parameter (`line=SKU-1|12`). */
const PREFILL_SEPARATOR = '|';

export type PoHeaderGroup = FormGroup<{
  supplierId: FormControl<PublicId | null>;
  warehouseId: FormControl<PublicId | null>;
  remark: FormControl<string>;
}>;

export type PoHeaderValue = ReturnType<PoHeaderGroup['getRawValue']>;

export function createPoHeader(): PoHeaderGroup {
  return new FormGroup({
    supplierId: new FormControl<PublicId | null>(null, [Validators.required]),
    warehouseId: new FormControl<PublicId | null>(null, [Validators.required]),
    remark: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_REMARK_LENGTH)]
    })
  });
}

export function headerFromOrder(order: PurchaseOrderDto): PoHeaderValue {
  return {
    supplierId: order.supplierId,
    warehouseId: order.warehouseId,
    // The API sends null, not an absent member, for what the order does not have.
    remark: order.remark ?? ''
  };
}

/** A saved line as the form holds it. The unit it was entered in is the only one it offers (the product is not read again). */
export function lineFromOrderLine(line: PurchaseOrderLineDto): LineValue {
  return {
    productId: line.productId,
    sku: line.sku,
    productName: line.productName,
    qty: line.qty,
    uomId: line.uomId,
    unitCost: line.unitPrice ?? null,
    remark: line.remark ?? '',
    available: null,
    baseUomCode: line.uomCode,
    uomOptions: [
      {
        uomId: line.uomId,
        code: line.uomCode,
        factorToBase: line.qty > 0 ? line.baseQty / line.qty : 1
      }
    ],
    // An order line is never a receipt line.
    purchaseOrderLineNo: null
  };
}

/**
 * The whole body of a save: every field is sent every time (there is no "absent = unchanged"), a blank price is 0.00 (a draft may
 * hold it, submitting refuses it in words) and a remark that is only blanks is sent as none.
 */
export function toSaveRequest(
  header: PoHeaderValue,
  lines: readonly LineValue[]
): PurchaseOrderSaveRequest {
  const text = (value: string): string | undefined =>
    value.trim() === '' ? undefined : value.trim();
  return {
    warehouseId: header.warehouseId ?? '',
    supplierId: header.supplierId ?? '',
    remark: text(header.remark),
    lines: lines.map(line => ({
      productId: line.productId,
      qty: toNumber(line.qty),
      uomId: line.uomId,
      unitPrice: toNumber(line.unitCost),
      remark: text(line.remark)
    }))
  };
}

export interface PrefillLine {
  readonly sku: string;
  readonly qty: number;
}

/**
 * The lines of an entry link, `?line=SKU-1|12&line=SKU-2|3` (one parameter or a list): a product code and a quantity (a bare code is
 * one). A part that is empty, has a quantity the API would refuse or repeats a code is dropped; at most MAX_PREFILL_LINES are kept.
 */
export function parsePrefillLines(raw: string | readonly string[] | undefined): PrefillLine[] {
  const parts: readonly string[] = typeof raw === 'string' ? [raw] : (raw ?? []);
  const seen = new Set<string>();
  const lines: PrefillLine[] = [];
  for (const part of parts) {
    const at = part.lastIndexOf(PREFILL_SEPARATOR);
    const sku = (at < 0 ? part : part.slice(0, at)).trim();
    const text = at < 0 ? '1' : part.slice(at + 1).trim();
    // Digits and at most four decimals: not 0x10, 1e2 or a sign (Number() would take them).
    const qty = /^\d+(\.\d{1,4})?$/.test(text) ? Number(text) : 0;
    if (sku !== '' && isValidQuantity(qty) && !seen.has(sku)) {
      seen.add(sku);
      lines.push({ sku, qty });
    }
  }
  return lines.slice(0, MAX_PREFILL_LINES);
}
