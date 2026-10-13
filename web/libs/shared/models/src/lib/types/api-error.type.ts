import { API_ERROR_KIND } from '../constants';

export type ApiErrorKind = (typeof API_ERROR_KIND)[keyof typeof API_ERROR_KIND];

/** RFC 9457 ProblemDetails of the IMS API, mapped to the shape the UI works with. */
export interface ApiError {
  readonly isApiError: true;
  readonly kind: ApiErrorKind;
  readonly status: number;
  readonly title: string;
  /** Human sentence from the server (shown to the user as is). */
  readonly detail: string;
  /** The one sentence to show for this failure (mapped and translated by the error interceptor). */
  readonly message?: string;
  readonly correlationId?: string;
  /** Field name (camelCase) -> messages, from the `errors` extension of a 400 response. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
  /** Other extension members, e.g. productId / available / requested of an insufficient-stock 409. */
  readonly extensions: Readonly<Record<string, unknown>>;
}

export function isApiError(value: unknown): value is ApiError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'isApiError' in value &&
    value.isApiError === true
  );
}
