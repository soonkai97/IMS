import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isNonEmptyString, isRecord } from '@ims/utils';
import { CONFLICT_CODE } from './stale-document';

/** One order line a posting would receive too much of, in the order line's unit (quantities only). */
export interface OverReceiptLine {
  readonly lineNo: number;
  readonly sku: string;
  readonly ordered: number;
  readonly received: number;
  readonly remaining: number;
  readonly requested: number;
}

/** The 409 `over_receipt`: posting a receipt from a purchase order asks for more of a line than the order has left. */
export function isOverReceiptError(error: ApiError | undefined): boolean {
  return (
    error?.kind === API_ERROR_KIND.Conflict &&
    error.extensions['code'] === CONFLICT_CODE.OverReceipt
  );
}

/** `lines` of an over_receipt answer (only for a caller who may read purchase orders); malformed entries are skipped. */
export function overReceiptLines(error: ApiError | undefined): readonly OverReceiptLine[] {
  const raw = error?.extensions['lines'];
  if (!isOverReceiptError(error) || !Array.isArray(raw)) {
    return [];
  }
  return raw.filter(isRecord).flatMap(entry => {
    const { lineNo, sku, ordered, received, remaining, requested } = entry;
    return typeof lineNo === 'number' &&
      isNonEmptyString(sku) &&
      typeof ordered === 'number' &&
      typeof received === 'number' &&
      typeof remaining === 'number' &&
      typeof requested === 'number'
      ? [{ lineNo, sku, ordered, received, remaining, requested }]
      : [];
  });
}
