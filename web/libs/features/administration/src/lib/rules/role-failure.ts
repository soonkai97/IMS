import { API_ERROR_KIND, ApiError, HTTP_STATUS } from '@ims/models';

/**
 * What went wrong with a role write, as the screen explains it:
 * - `stale`: the role changed since it was opened (a 409 and a different concurrency token on the server);
 * - `containment`: a 403; the API has no code for its three causes (grant beyond your own, a role that holds more, a role held by someone who sees more warehouses), so the server's sentence is shown;
 * - `inUse`: the role still has people (a 409 with their number in the sentence: retiring or deleting a role they hold);
 * - `conflict`: any other 409, such as a locked or standard role, the person's own role, or a name that is taken;
 * - `invalid`: a 400; `notFound`: a 404; `other`: anything else.
 */
export type RoleFailureKind =
  'stale' | 'containment' | 'inUse' | 'conflict' | 'invalid' | 'notFound' | 'other';

export interface RoleFailure {
  readonly kind: RoleFailureKind;
  /** The server's sentence, shown when the screen has no sentence of its own for the kind. */
  readonly message: string;
  /** For `inUse`: how many people hold the role, as the server's sentence says. */
  readonly count?: number;
}

/** The API sentence is "{n} user(s) still have this role. ...": the count is its only number. */
const IN_USE_COUNT = /^(\d+)\s+user/;

/**
 * The API gives no machine code for these 409s, so they are told apart by what the screen knows: `roleChangedSince` is true when
 * a second read of the role shows another concurrency token than the one the edit started from (someone changed it).
 */
export function classifyRoleFailure(
  error: ApiError,
  options: { readonly roleChangedSince: boolean }
): RoleFailure {
  const message = error.message ?? error.detail;
  switch (error.status) {
    case HTTP_STATUS.Forbidden:
      // The error interceptor puts its own generic sentence in `message`; the server's says which rule was hit.
      return { kind: 'containment', message: error.detail || message };
    case HTTP_STATUS.NotFound:
      return { kind: 'notFound', message };
    case HTTP_STATUS.BadRequest:
      return { kind: 'invalid', message };
    case HTTP_STATUS.Conflict:
      return classifyConflict(error, message, options.roleChangedSince);
    default:
      return { kind: error.kind === API_ERROR_KIND.Validation ? 'invalid' : 'other', message };
  }
}

function classifyConflict(
  error: ApiError,
  message: string,
  roleChangedSince: boolean
): RoleFailure {
  if (roleChangedSince) {
    return { kind: 'stale', message };
  }
  const count = IN_USE_COUNT.exec(error.detail)?.[1];
  return count === undefined
    ? { kind: 'conflict', message }
    : { kind: 'inUse', message, count: Number(count) };
}
