import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isNonEmptyString, isRecord } from '@ims/utils';
import { CONFLICT_CODE } from './stale-document';

/** One original line a return would take back too much of, in the original line's own unit (quantities only). */
export interface OverReturnLine {
  readonly lineNo: number;
  readonly sku: string;
  readonly uomCode: string;
  readonly original: number;
  readonly returned: number;
  readonly remaining: number;
  readonly requested: number;
}

/**
 * The 409 `over_return`: posting or saving a return asks for more of an original line than can still be returned. The code is
 * stable; an older API had no code and only its sentence, which names every line, so that sentence is the fallback.
 */
export function isOverReturnError(error: ApiError | undefined): boolean {
  return (
    error?.kind === API_ERROR_KIND.Conflict && error.extensions['code'] === CONFLICT_CODE.OverReturn
  );
}

/**
 * `lines` of an over_return answer (line number = the position on the original, sku, unit, original, returned, remaining and
 * requested quantities); a malformed entry is skipped, and none at all leaves the sentence of the API to say it.
 */
export function overReturnLines(error: ApiError | undefined): readonly OverReturnLine[] {
  const raw = error?.extensions['lines'];
  if (!isOverReturnError(error) || !Array.isArray(raw)) {
    return [];
  }
  return raw.filter(isRecord).flatMap(entry => {
    const { lineNo, sku, uom, original, returned, remaining, requested } = entry;
    return typeof lineNo === 'number' &&
      isNonEmptyString(sku) &&
      isNonEmptyString(uom) &&
      typeof original === 'number' &&
      typeof returned === 'number' &&
      typeof remaining === 'number' &&
      typeof requested === 'number'
      ? [{ lineNo, sku, uomCode: uom, original, returned, remaining, requested }]
      : [];
  });
}
