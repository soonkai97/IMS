import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isNonEmptyString } from '@ims/utils';

/** `code` of the 409 that says the order changed since it was opened (the If-Match token no longer matches). */
const DOCUMENT_CHANGED = 'document_changed';

/**
 * A 409 that names `document_changed`. Unlike the document screens this does NOT take a 409 without a code for a stale copy: the other
 * 409 sentences of an order (cancel refused while a receipt is open, close refused, a busy lock) have no code and must be shown as
 * what they are, not as "changed since you opened it".
 */
export function isStaleOrderError(error: ApiError | undefined): boolean {
  return error?.kind === API_ERROR_KIND.Conflict && error.extensions['code'] === DOCUMENT_CHANGED;
}

export interface StaleOrderDetails {
  readonly name: string;
  /** UTC instant, as the API sends it. */
  readonly at: string;
}

/** Who changed the order and when, when the API says so. */
export function staleOrderDetails(error: ApiError | undefined): StaleOrderDetails | undefined {
  const name = error?.extensions['changedBy'];
  const at = error?.extensions['changedAt'];
  return isStaleOrderError(error) && isNonEmptyString(name) && isNonEmptyString(at)
    ? { name, at }
    : undefined;
}
