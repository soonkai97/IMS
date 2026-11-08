import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isStaleDocumentError } from './stale-document';
import {
  isOverDeliveryError,
  isOverReceiptError,
  overDeliveryLines,
  overReceiptLines
} from './over-receipt';

function conflict(extensions: Record<string, unknown>): ApiError {
  return {
    isApiError: true,
    kind: API_ERROR_KIND.Conflict,
    status: 409,
    title: 'Conflict',
    detail: 'Over',
    fieldErrors: {},
    extensions
  };
}

describe('over-receipt answer', () => {
  const figures = { lineNo: 2, sku: 'A-1', ordered: 10, received: 6, remaining: 4, requested: 5 };

  it('is named by its code and is neither a stale copy nor another 409', () => {
    const over = conflict({ code: 'over_receipt', lines: [figures] });

    expect(isOverReceiptError(over)).toBe(true);
    expect(isStaleDocumentError(over)).toBe(false);
    expect(isOverReceiptError(conflict({}))).toBe(false);
  });

  it('reads the figures of every line and skips malformed entries', () => {
    const over = conflict({
      code: 'over_receipt',
      lines: [figures, { ...figures, sku: '' }, { ...figures, remaining: '4' }, 'x']
    });

    expect(overReceiptLines(over)).toEqual([figures]);
  });

  it('has no figures for the general sentence of a caller who may not read orders', () => {
    expect(overReceiptLines(conflict({}))).toEqual([]);
  });
});

describe('over-delivery answer', () => {
  const figures = { lineNo: 2, sku: 'A-1', ordered: 10, delivered: 6, remaining: 4, requested: 5 };

  it('is named by its code, is not an over-receipt, and is not a stale copy', () => {
    const over = conflict({ code: 'over_delivery', lines: [figures] });

    expect(isOverDeliveryError(over)).toBe(true);
    expect(isOverReceiptError(over)).toBe(false);
    expect(isStaleDocumentError(over)).toBe(false);
    expect(isOverDeliveryError(conflict({}))).toBe(false);
  });

  it('reads the figures in the shape of the receipt (received = delivered) and skips malformed entries', () => {
    const over = conflict({
      code: 'over_delivery',
      lines: [figures, { ...figures, remaining: 'x' }, null, 'junk']
    });

    expect(overDeliveryLines(over)).toEqual([
      { lineNo: 2, sku: 'A-1', ordered: 10, received: 6, remaining: 4, requested: 5 }
    ]);
    expect(overDeliveryLines(conflict({ code: 'over_receipt', lines: [figures] }))).toEqual([]);
    expect(overDeliveryLines(undefined)).toEqual([]);
  });
});
