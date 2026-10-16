/** Why a sign-in did not work. Danger kinds are about the user's input or account; the others about the environment. */
export const LOGIN_FAILURE = {
  Invalid: 'invalid',
  Locked: 'locked',
  RateLimited: 'rate-limited',
  Network: 'network',
  Server: 'server'
} as const;

export type LoginFailureKind = (typeof LOGIN_FAILURE)[keyof typeof LOGIN_FAILURE];

/**
 * A failed sign-in kept as data, not as a sentence: the page turns it into text in the language in use, so a language change
 * rewrites the banner. `minutes` / `seconds` are the numbers the API sent.
 */
export interface LoginFailure {
  readonly kind: LoginFailureKind;
  readonly minutes?: number;
  readonly seconds?: number;
}
