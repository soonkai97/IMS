import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isStaleDocumentError } from './stale-document';
import { isOverReturnError } from './over-return';

function conflict(extensions: Record<string, unknown>): ApiError {
  return {
    isApiError: true,
    kind: API_ERROR_KIND.Conflict,
    status: 409,
    title: 'Conflict',
    detail: 'Over',
    message: 'Over',
    fieldErrors: {},
    extensions
  };
}

describe('over-return answer', () => {
  it('is named by its code and is neither a stale copy nor another 409', () => {
    const over = conflict({ code: 'over_return' });

    expect(isOverReturnError(over)).toBe(true);
    expect(isStaleDocumentError(over)).toBe(false);
    expect(isOverReturnError(conflict({}))).toBe(false);
    expect(isOverReturnError(conflict({ code: 'over_delivery' }))).toBe(false);
    expect(isOverReturnError(undefined)).toBe(false);
  });
});
