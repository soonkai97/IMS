import { API_ERROR_KIND, ApiError } from '@ims/models';
import { isNonEmptyString, isRecord } from '@ims/utils';

/** Extension member of the insufficient-stock 409 of older APIs; newer ones also send `code`. */
const STOCK_CONFLICT_MEMBER = 'productId';
const CODE_MEMBER = 'code';

/** `code` of the 409 answers a document command can get (ProblemDetails extension). */
export const CONFLICT_CODE = {
  DocumentChanged: 'document_changed',
  InsufficientStock: 'insufficient_stock',
  ReversalBlocked: 'reversal_blocked'
} as const;

function codeOf(error: ApiError): unknown {
  return error.extensions[CODE_MEMBER];
}

function isConflict(error: ApiError | undefined): error is ApiError {
  return error?.kind === API_ERROR_KIND.Conflict;
}

/**
 * A 409 that says the document changed since the user opened it (the If-Match token no longer matches): UX error E4,
 * a banner with a Reload button. The API names it (`code`); an API without codes answers a 409 that has no product.
 * A blocked reversal and an insufficient-stock answer are 409s too, but they are not this.
 */
export function isStaleDocumentError(error: ApiError | undefined): boolean {
  if (!isConflict(error)) {
    return false;
  }
  const code = codeOf(error);
  if (code !== undefined) {
    return code === CONFLICT_CODE.DocumentChanged;
  }
  return error.extensions[STOCK_CONFLICT_MEMBER] === undefined;
}

/** The insufficient-stock 409 (E5): says which product, how much is there and how much was asked for. */
export function isStockConflictError(error: ApiError | undefined): boolean {
  if (!isConflict(error)) {
    return false;
  }
  const code = codeOf(error);
  if (code !== undefined) {
    return code === CONFLICT_CODE.InsufficientStock;
  }
  return error.extensions[STOCK_CONFLICT_MEMBER] !== undefined;
}

/** The reversal would make stock negative (E6): it names EVERY product that would go below zero. */
export function isReversalBlockedError(error: ApiError | undefined): boolean {
  return isConflict(error) && codeOf(error) === CONFLICT_CODE.ReversalBlocked;
}

export interface ReversalShortage {
  readonly sku: string;
  readonly uomCode: string;
  /** The negative balance the product would be left with, in the base unit. */
  readonly balance: number;
}

/** `shortages` of a reversal_blocked answer; malformed entries are skipped. */
export function reversalShortages(error: ApiError | undefined): readonly ReversalShortage[] {
  const raw = error?.extensions['shortages'];
  if (!Array.isArray(raw)) {
    return [];
  }
  return raw.filter(isRecord).flatMap(entry =>
    isNonEmptyString(entry['sku']) && typeof entry['balance'] === 'number'
      ? [
          {
            sku: entry['sku'],
            uomCode: isNonEmptyString(entry['uomCode']) ? entry['uomCode'] : '',
            balance: entry['balance']
          }
        ]
      : []
  );
}

export interface StaleDetails {
  readonly name: string;
  /** UTC instant, as the API sends it. */
  readonly at: string;
}

/** Who changed the document and when (E4), when the API says so; undefined otherwise. */
export function staleDetails(error: ApiError | undefined): StaleDetails | undefined {
  if (!isStaleDocumentError(error)) {
    return undefined;
  }
  const name = error?.extensions['changedBy'];
  const at = error?.extensions['changedAt'];
  return isNonEmptyString(name) && isNonEmptyString(at) ? { name, at } : undefined;
}
