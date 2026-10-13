/** Lifecycle of one API call, used by every store so the UI renders loading / error / success the same way. */
export const API_STATUS = {
  Idle: 'idle',
  Loading: 'loading',
  Success: 'success',
  Failure: 'failure'
} as const;

export const API_ERROR_KIND = {
  Network: 'network',
  Validation: 'validation',
  Unauthorized: 'unauthorized',
  Forbidden: 'forbidden',
  NotFound: 'not-found',
  Conflict: 'conflict',
  Server: 'server',
  Unknown: 'unknown'
} as const;

/** HTTP status codes the client reacts to. */
export const HTTP_STATUS = {
  BadRequest: 400,
  Unauthorized: 401,
  Forbidden: 403,
  NotFound: 404,
  Conflict: 409,
  TooManyRequests: 429,
  ServerError: 500
} as const;

/** HTTP methods the client tells apart (a read is not the user's next action). */
export const HTTP_METHOD = {
  Get: 'GET'
} as const;
