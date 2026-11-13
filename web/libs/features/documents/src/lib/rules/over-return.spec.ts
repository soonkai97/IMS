import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isStaleDocumentError } from './stale-document';
import { isOverReturnError, overReturnLines } from './over-return';

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

describe('over-return lines', () => {
  const entry = {
    lineNo: 2,
    sku: 'DEMO-001',
    uom: 'BOX',
    original: 10,
    returned: 4,
    remaining: 6,
    requested: 8
  };

  it('reads each line with its unit and figures', () => {
    expect(overReturnLines(conflict({ code: 'over_return', lines: [entry] }))).toEqual([
      {
        lineNo: 2,
        sku: 'DEMO-001',
        uomCode: 'BOX',
        original: 10,
        returned: 4,
        remaining: 6,
        requested: 8
      }
    ]);
  });

  it('skips a malformed entry and ignores lines of another answer', () => {
    const lines = [entry, { ...entry, uom: '' }, { ...entry, remaining: '6' }, 'x'];

    expect(overReturnLines(conflict({ code: 'over_return', lines }))).toHaveLength(1);
    expect(overReturnLines(conflict({ code: 'over_return' }))).toEqual([]);
    expect(overReturnLines(conflict({ code: 'over_delivery', lines: [entry] }))).toEqual([]);
    expect(overReturnLines(undefined)).toEqual([]);
  });
});
