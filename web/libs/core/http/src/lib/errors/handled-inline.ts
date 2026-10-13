import { HttpContext, HttpContextToken } from '@angular/common/http';

/** `all`: the screen shows every failure of the call in place. A list of statuses: only those. */
export type InlineFailures = 'all' | readonly number[];

/**
 * Set on a call whose failure the screen itself shows (a banner, a message at a field). The error interceptor then raises no
 * toast for it: one failure, one announcement. The screen must show every failure the flag covers.
 */
export const HANDLED_INLINE = new HttpContextToken<InlineFailures | undefined>(() => undefined);

/**
 * Context for a generated client call: `client.list(..., handledInline())`. Pass statuses when the screen shows only those
 * (`handledInline(HTTP_STATUS.Conflict)`); every other failure of the call still raises the toast.
 */
export function handledInline(...statuses: readonly number[]): HttpContext {
  return new HttpContext().set(HANDLED_INLINE, statuses.length === 0 ? 'all' : statuses);
}

export function isHandledInline(context: HttpContext, status: number): boolean {
  const failures = context.get(HANDLED_INLINE);
  return failures === 'all' || (failures?.includes(status) ?? false);
}
