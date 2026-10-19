import { API_ERROR_KIND, ApiError, HTTP_STATUS } from '@ims/models';

/**
 * What went wrong with a role write, as the screen explains it:
 * - `stale`: the role changed since it was opened (a 409 and a different concurrency token on the server);
 * - `containment`: a 403, the caller may not give or change what they do not hold (the server sentence says which);
 * - `inUse`: the role still has people (a 409 with the count);
 * - `conflict`: any other 409, such as a locked or standard role, the person's own role, or a name that is taken;
 * - `invalid`: a 400; `notFound`: a 404; `other`: anything else.
 */
export type RoleFailureKind =
  'stale' | 'containment' | 'inUse' | 'conflict' | 'invalid' | 'notFound' | 'other';

export interface RoleFailure {
  readonly kind: RoleFailureKind;
  /** The server's sentence, shown when the screen has no sentence of its own for the kind. */
  readonly message: string;
  /** For `inUse`: how many people hold the role (read from the server's sentence, else the count the screen knows). */
  readonly count?: number;
}

/** The API sentence is "{n} user(s) still have this role. ...": the count is its only number. */
const IN_USE_COUNT = /^(\d+)\s+user/;

/**
 * The API gives no machine code for these 409s, so they are told apart by what the screen knows:
 * `roleChangedSince` is true when a second read of the role shows another concurrency token than the one the edit started
 * from (someone changed it); `removing` is true for the calls that fail because people hold the role (delete, retire).
 */
export function classifyRoleFailure(
  error: ApiError,
  options: {
    readonly roleChangedSince: boolean;
    readonly removing: boolean;
    readonly knownUserCount?: number;
  }
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
      return classifyConflict(error, message, options);
    default:
      return { kind: error.kind === API_ERROR_KIND.Validation ? 'invalid' : 'other', message };
  }
}

function classifyConflict(
  error: ApiError,
  message: string,
  options: {
    readonly roleChangedSince: boolean;
    readonly removing: boolean;
    readonly knownUserCount?: number;
  }
): RoleFailure {
  if (options.roleChangedSince) {
    return { kind: 'stale', message };
  }
  const count = IN_USE_COUNT.exec(error.detail)?.[1];
  if (count !== undefined || (options.removing && (options.knownUserCount ?? 0) > 0)) {
    return {
      kind: 'inUse',
      message,
      count: count === undefined ? options.knownUserCount : Number(count)
    };
  }
  return { kind: 'conflict', message };
}
