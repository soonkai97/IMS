import { API_ERROR_KIND, ApiError } from '@ims/models';

/** Extension member of the insufficient-stock 409 (docs: API contract); the stale-copy 409 has none. */
const STOCK_CONFLICT_MEMBER = 'productId';

/**
 * A 409 on a document command that is not the insufficient-stock answer means the document changed since the user
 * opened it (the If-Match token no longer matches): UX error E4, shown as a banner with a Reload button.
 */
export function isStaleDocumentError(error: ApiError | undefined): boolean {
  return (
    error?.kind === API_ERROR_KIND.Conflict && error.extensions[STOCK_CONFLICT_MEMBER] === undefined
  );
}

/** The insufficient-stock 409 (E5): says which product, how much is there and how much was asked for. */
export function isStockConflictError(error: ApiError | undefined): boolean {
  return (
    error?.kind === API_ERROR_KIND.Conflict && error.extensions[STOCK_CONFLICT_MEMBER] !== undefined
  );
}
