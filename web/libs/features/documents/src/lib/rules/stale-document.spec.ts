import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isStaleDocumentError, isStockConflictError } from './stale-document';

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

  it('ignores other errors and no error', () => {
    expect(isStaleDocumentError(undefined)).toBe(false);
    expect(isStaleDocumentError({ ...conflict(), kind: API_ERROR_KIND.Validation })).toBe(false);
  });
});
