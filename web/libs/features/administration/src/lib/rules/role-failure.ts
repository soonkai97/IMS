import { API_ERROR_KIND, ApiError, HTTP_STATUS } from '@ims/models';

/**
 * What went wrong with a role write, as the screen explains it:
 * - `stale`: the role changed since it was opened (a 409 and a different concurrency token on the server);
 * - `containment`: a 403, the caller may not give or change what they do not hold (the server sentence says which);
 * - `conflict`: any other 409, such as a locked or standard role, the person's own role, or a name that is taken;
 * - `invalid`: a 400; `notFound`: a 404; `other`: anything else.
 */
export type RoleFailureKind =
  'stale' | 'containment' | 'conflict' | 'invalid' | 'notFound' | 'other';

export interface RoleFailure {
  readonly kind: RoleFailureKind;
  /** The server's sentence, shown when the screen has no sentence of its own for the kind. */
  readonly message: string;
}

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
      return { kind: 'containment', message };
    case HTTP_STATUS.NotFound:
      return { kind: 'notFound', message };
    case HTTP_STATUS.BadRequest:
      return { kind: 'invalid', message };
    case HTTP_STATUS.Conflict:
      return classifyConflict(message, options.roleChangedSince);
    default:
      return { kind: error.kind === API_ERROR_KIND.Validation ? 'invalid' : 'other', message };
  }
}

function classifyConflict(message: string, roleChangedSince: boolean): RoleFailure {
  return { kind: roleChangedSince ? 'stale' : 'conflict', message };
}
