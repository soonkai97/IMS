import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isStaleDocumentError } from './stale-document';
import { isOverReceiptError, overReceiptLines } from './over-receipt';

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
