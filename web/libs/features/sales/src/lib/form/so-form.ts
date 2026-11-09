import { FormControl, FormGroup, Validators } from '@angular/forms';
import { SalesOrderDto, SalesOrderLineDto, SalesOrderSaveRequest } from '@ims/api';
import { LineValue } from '@ims/features/documents';
import { MAX_REMARK_LENGTH, PublicId } from '@ims/models';
import { toNumber } from '@ims/utils';

export type SoHeaderGroup = FormGroup<{
  customerId: FormControl<PublicId | null>;
  warehouseId: FormControl<PublicId | null>;
  remark: FormControl<string>;
}>;

export type SoHeaderValue = ReturnType<SoHeaderGroup['getRawValue']>;

export function createSoHeader(): SoHeaderGroup {
  return new FormGroup({
    customerId: new FormControl<PublicId | null>(null, [Validators.required]),
    warehouseId: new FormControl<PublicId | null>(null, [Validators.required]),
    remark: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_REMARK_LENGTH)]
    })
  });
}

export function headerFromOrder(order: SalesOrderDto): SoHeaderValue {
  return {
    customerId: order.customerId,
    warehouseId: order.warehouseId,
    // What the order does not have is null or absent.
    remark: order.remark ?? ''
  };
}

/** A saved line as the form holds it. The unit it was entered in is the only one it offers (the product is not read again). */
export function lineFromOrderLine(line: SalesOrderLineDto): LineValue {
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
    purchaseOrderLineNo: null,
    salesOrderLineNo: null
  };
}

/**
 * The whole body of a save: every field is sent every time (there is no "absent = unchanged"), a blank price is 0.00 (a draft may
 * hold it, submitting refuses it in words) and a remark that is only blanks is sent as none.
 */
export function toSaveRequest(
  header: SoHeaderValue,
  lines: readonly LineValue[]
): SalesOrderSaveRequest {
  const text = (value: string): string | undefined =>
    value.trim() === '' ? undefined : value.trim();
  return {
    warehouseId: header.warehouseId ?? '',
    customerId: header.customerId ?? '',
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
