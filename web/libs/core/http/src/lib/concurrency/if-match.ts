import { HttpContext, HttpContextToken, HttpInterceptorFn } from '@angular/common/http';

export const IF_MATCH_HEADER = 'If-Match';

/** Concurrency token the caller read earlier (the `concurrencyToken` of a document); sent back as `If-Match`. */
export const IF_MATCH = new HttpContextToken<string | undefined>(() => undefined);

/**
 * Context for a generated client call: `client.submit(id, withIfMatch(document.concurrencyToken))`. The server then
 * refuses the call with 409 when someone changed the resource after the token was read.
 */
export function withIfMatch(concurrencyToken: string | undefined): HttpContext {
  return new HttpContext().set(IF_MATCH, concurrencyToken);
}

/** The token travels as a quoted entity tag, like the ETag header the server sends. */
export function toEntityTag(token: string): string {
  return token.startsWith('"') ? token : `"${token}"`;
}

/** Turns the IF_MATCH context value into the `If-Match` request header. */
export const ifMatchInterceptor: HttpInterceptorFn = (request, next) => {
  const token = request.context.get(IF_MATCH);
  return token
    ? next(request.clone({ setHeaders: { [IF_MATCH_HEADER]: toEntityTag(token) } }))
    : next(request);
};
