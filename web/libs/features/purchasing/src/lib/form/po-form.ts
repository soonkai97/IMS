import { FormControl, FormGroup, Validators } from '@angular/forms';
import { PurchaseOrderDto, PurchaseOrderLineDto, PurchaseOrderSaveRequest } from '@ims/api';
import { LineValue } from '@ims/features/documents';
import { PublicId } from '@ims/models';
import { toNumber } from '@ims/utils';

/** The API refuses a longer remark; the form must not offer more than it accepts. */
export const MAX_REMARK_LENGTH = 500;
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
    ]
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
