import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isStaleOrderError, staleOrderDetails } from './stale-order';

function conflict(extensions: Record<string, unknown>): ApiError {
  return {
    isApiError: true,
    kind: API_ERROR_KIND.Conflict,
    status: 409,
    title: 'Conflict',
    detail: 'x',
    fieldErrors: {},
    extensions
  };
}

describe('isStaleOrderError', () => {
  it('is true only for the 409 that names document_changed', () => {
    expect(isStaleOrderError(conflict({ code: 'document_changed' }))).toBe(true);
  });

  it('is false for a 409 without a code (cancel refused while a receipt is open): it is shown as what it is', () => {
    expect(isStaleOrderError(conflict({}))).toBe(false);
    expect(isStaleOrderError(conflict({ code: 'over_receipt' }))).toBe(false);
  });

  it('is false for no error and for another kind', () => {
    expect(isStaleOrderError(undefined)).toBe(false);
    expect(
      isStaleOrderError({ ...conflict({ code: 'document_changed' }), kind: API_ERROR_KIND.Server })
    ).toBe(false);
  });
});

describe('staleOrderDetails', () => {
  it('names who changed the order and when', () => {
    expect(
      staleOrderDetails(
        conflict({ code: 'document_changed', changedBy: 'Sam', changedAt: '2026-10-21T01:00:00Z' })
      )
    ).toEqual({ name: 'Sam', at: '2026-10-21T01:00:00Z' });
  });

  it('is undefined when the API does not say, or the error is not a stale one', () => {
    expect(staleOrderDetails(conflict({ code: 'document_changed' }))).toBeUndefined();
    expect(staleOrderDetails(conflict({ changedBy: 'Sam', changedAt: 'x' }))).toBeUndefined();
  });
});
