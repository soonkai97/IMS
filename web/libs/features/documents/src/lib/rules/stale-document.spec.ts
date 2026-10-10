import { API_ERROR_KIND, ApiError } from '@ims/models';
import {
  isReversalBlockedError,
  isStaleDocumentError,
  isStockConflictError,
  reversalShortages,
  staleDetails
} from './stale-document';

function conflict(extensions: Record<string, unknown> = {}): ApiError {
  return {
    isApiError: true,
    kind: API_ERROR_KIND.Conflict,
    status: 409,
    title: 'Conflict',
    detail: '',
    fieldErrors: {},
    extensions
  };
}

describe('stale document detection', () => {
  it('treats a plain 409 as a stale copy', () => {
    expect(isStaleDocumentError(conflict())).toBe(true);
    expect(isStockConflictError(conflict())).toBe(false);
  });

  it('treats a 409 with a product as the insufficient-stock answer, not as stale', () => {
    const stock = conflict({ productId: 5, available: 2, requested: 9 });

    expect(isStaleDocumentError(stock)).toBe(false);
    expect(isStockConflictError(stock)).toBe(true);
  });

  it('reads the code of newer APIs first: document_changed is stale, insufficient_stock is E5', () => {
    expect(isStaleDocumentError(conflict({ code: 'document_changed' }))).toBe(true);
    expect(isStaleDocumentError(conflict({ code: 'insufficient_stock', productId: 5 }))).toBe(
      false
    );
    expect(isStockConflictError(conflict({ code: 'insufficient_stock', productId: 5 }))).toBe(true);
    expect(isStockConflictError(conflict({ code: 'document_changed' }))).toBe(false);
  });

  it('a blocked reversal is neither a stale copy nor the post-time stock conflict', () => {
    const blocked = conflict({
      code: 'reversal_blocked',
      shortages: [{ productId: 1, sku: 'A', uomCode: 'PCS', balance: -3 }]
    });

    expect(isReversalBlockedError(blocked)).toBe(true);
    expect(isStaleDocumentError(blocked)).toBe(false);
    expect(isStockConflictError(blocked)).toBe(false);
    expect(isReversalBlockedError(conflict())).toBe(false);
  });

  it('lists every product of a blocked reversal and skips malformed entries', () => {
    const blocked = conflict({
      code: 'reversal_blocked',
      shortages: [
        { productId: 1, sku: 'A', uomCode: 'PCS', balance: -3 },
        { sku: 'B', uomCode: 'BOX', balance: -6 },
        { sku: '', balance: 1 },
        'nonsense'
      ]
    });

    expect(reversalShortages(blocked)).toEqual([
      { sku: 'A', uomCode: 'PCS', balance: -3 },
      { sku: 'B', uomCode: 'BOX', balance: -6 }
    ]);
    expect(reversalShortages(conflict())).toEqual([]);
  });

  it('says who changed a stale document and when, only when the API tells', () => {
    const changed = conflict({
      code: 'document_changed',
      changedBy: 'Raj',
      changedAt: '2026-10-10T06:42:00Z'
    });

    expect(staleDetails(changed)).toEqual({ name: 'Raj', at: '2026-10-10T06:42:00Z' });
    expect(staleDetails(conflict({ code: 'document_changed' }))).toBeUndefined();
    expect(
      staleDetails(conflict({ code: 'reversal_blocked', changedBy: 'x', changedAt: 'y' }))
    ).toBeUndefined();
  });

  it('ignores other errors and no error', () => {
    expect(isStaleDocumentError(undefined)).toBe(false);
    expect(isStaleDocumentError({ ...conflict(), kind: API_ERROR_KIND.Validation })).toBe(false);
  });
});
