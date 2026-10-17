# Refresh token in an HttpOnly cookie (SEC-34)

Status: done. The web app is migrated (section 5, steps 1 to 5 done in `web/`): it stores no refresh token, sends credentials and the CSRF
header, and restores the session with one refresh call. Step 6 is done in code: `Auth:ReturnRefreshTokenInBody` now defaults to **false**
(both in `AuthOptions` and in `appsettings.json`), so login and refresh answer `"refreshToken": null` unless an operator switches the
deprecated body delivery back on. The switch stays for a rollback or for an old non-browser client.

## 1. What changed and why

The 7-day refresh token used to live in `localStorage`, readable by any script on the page (XSS, a compromised dependency). The API now
also hands it to the browser as a cookie that scripts cannot read. Rotation (every refresh issues a new token and revokes the old one),
the 401 for a replayed token and the 7-day lifetime are unchanged.

| | |
|---|---|
| Cookie name | `__Secure-ims-refresh` over HTTPS, `ims-refresh` only on a Development server reached over plain http (the product part follows the assembly name, see `AuthCookie.RefreshName`) |
| Attributes | `HttpOnly; Secure; SameSite=Strict; Path=/api/auth; Max-Age=604800` (no `Domain`, so it is host-only) |
| Why not `__Host-` | `__Host-` demands `Path=/`. The cookie must travel only with login, refresh and logout, so it is scoped to `/api/auth` and uses the `__Secure-` prefix, which still makes the browser refuse it unless it was set over HTTPS with `Secure`. |
| `Secure` | On in every environment except Development, whatever scheme the API itself sees. Behind a TLS-terminating proxy the API may be reached over http while the browser is on https; the flag does not depend on `X-Forwarded-Proto`. Only a developer on `http://localhost` gets the cookie without `Secure`. |
| Read | Only the name that matches the same decision is read, so a planted look-alike without the `__Secure-` prefix is ignored in production. |

## 2. Endpoints

| Endpoint | Cookie behaviour | Body behaviour (DEPRECATED, still works) |
|---|---|---|
| `POST /api/auth/login` | Sets the cookie. | Response JSON has `"refreshToken": null`; it carries the token only while `Auth:ReturnRefreshTokenInBody` is switched on (off by default). |
| `POST /api/auth/refresh` | Reads the cookie when the body has no token, rotates it, sets the new cookie. A dead cookie token answers 401 **and** clears the cookie. | `{ "refreshToken": "..." }` is still accepted and wins over the cookie. The response sets the cookie too. |
| `POST /api/auth/logout` | Revokes the cookie's token and clears the cookie (always 204). | A body token is revoked as before; the cookie is cleared as well. |

No token in the body and no cookie: refresh answers 401 (nobody is signed in), logout answers 204. A body with an explicitly empty
`refreshToken` string is still a 400, as before.

The OpenAPI document marks `AuthResult.refreshToken` and `RefreshRequest.refreshToken` as deprecated and describes the cookie flow on the three
operations.

## 3. Configuration

| Key | Default | Meaning |
|---|---|---|
| `Auth:ReturnRefreshTokenInBody` | `false` | `false` (default): login and refresh answer `"refreshToken": null`; the token exists only in the cookie. `true`: the deprecated body delivery is back (rollback, or a client that cannot use cookies). |
| `Cors:Origins` | required outside Development | Now also the list of origins allowed to send the cookie (section 4). Wildcards are refused at start-up. **Even a same-origin deployment (web and `/api` behind one proxy host) must list the web app's own origin here**, because the CSRF check compares the request's `Origin` with this list. |

CORS now sends `Access-Control-Allow-Credentials: true` for the listed origins, so a cross-origin page (the Angular dev server on
`http://localhost:4200` calling the API on another port) can use the cookie with `credentials: 'include'`.

## 4. CSRF defence for cookie-authenticated calls

A cookie is attached by the browser on its own, so a refresh or logout that rides on it needs proof that our own page made the call.
Three independent layers; each alone stops a forged cross-site request:

1. `SameSite=Strict`: the browser does not send the cookie on any cross-site request.
2. **Required custom header** `X-Requested-With: XMLHttpRequest` (`AuthCookie.CsrfHeaderName`). A form post or `<img>` cannot add it; a script on another origin can only add it after a CORS preflight that only the listed origins pass.
3. **Origin check**: the `Origin` header (or, when absent, the origin of `Referer`) must be one of `Cors:Origins`. `Origin: null`, a missing Origin and Referer, a prefix or look-alike (`http://localhost:4200.evil.example`) are refused.

Failure: `403` ProblemDetails, the token is not consumed and the cookie is left as it is. The check applies only when the token comes from the
cookie. A token sent in the request body is an explicit credential that a forged request cannot supply, so the old clients need no header.
Login is not cookie-authenticated (it needs the password in a JSON body, which a cross-site form cannot send), so it carries no check.

Unchanged and still needed: HTTPS only (HSTS is sent outside Development) and CORS limited to the configured origins.

Rate limits (per client address, fixed one-minute window; `Caller` in `PresentationServices.cs`):

| Policy | Endpoint | Limit | Why |
|---|---|---|---|
| `login` | `POST /api/auth/login` | 20 (`Limits.LoginRateLimitPerMinute`) | Stops password guessing. Strict on purpose; the 5-strike account lockout is a second guard. |
| `refresh` | `POST /api/auth/refresh` | 300 (`Limits.RefreshRateLimitPerMinute`) | Not a guessing target (the token is 384 random bits). Every signed-in user refreshes every 5 minutes (access token life), and all of them at once after a deploy that changes the access claims, so an office behind one address needs room. A separate bucket: sign-in traffic can no longer use up refreshes, nor the reverse. |
| global | everything else | 600, or the signed-in user name instead of the address | Backstop; also counts login and refresh. |

A limited call answers 429 with `Retry-After` and `retryAfterSeconds` in the body. The web app does not treat that as a sign-out: a refresh
that fails with 429, a gateway error or no network is retried twice (after `Retry-After`, capped at 10 s, else 1 s then 2 s) and, if it still
fails, the user stays signed in and the next call that gets a 401 refreshes again. Only 401 and 403 from refresh end the session.
Logout has no dedicated limit (the global one applies).

Site rule: `SameSite=Strict` means the web app and the API must be the same site (same registrable domain, ports ignored). The supported
deployment is one host with `/api` proxied (`apiBaseUrl: ''` in the production web build). Web and API on unrelated domains cannot use the cookie.

## 5. Steps for the web app (fe-framework)

All in `web/`; none of it needs an API change.

1. `libs/core/http`: send credentials to the API. In `provideHttpClient(withFetch())` add an interceptor (or `withCredentials: true` on the NSwag
   `fetch` wrapper) so every call to the API origin uses `credentials: 'include'`. Same-origin production already sends cookies; the dev server on
   `:4200` calling `:5109` needs it.
2. Add the header `X-Requested-With: XMLHttpRequest` to `POST /api/auth/refresh` and `POST /api/auth/logout` (an interceptor for the `/api/auth/` path is enough).
3. `libs/core/identity` (`auth.service.ts`, `token-storage.ts`, `identity.const.ts`):
   - refresh: call `POST /api/auth/refresh` with an empty JSON body (`{}`); read only `accessToken`, `expiresAt` and `user`;
   - logout: call `POST /api/auth/logout` with `{}`; the server clears the cookie;
   - stop writing, reading and deleting the refresh token in `localStorage`; remove its key; on start-up delete a leftover `localStorage` refresh entry once, so old tokens do not linger;
   - "is there a session?" can no longer be answered from storage: on app start try `POST /api/auth/refresh` once; 401 means signed out.
4. Regenerate the API client (`npm run api:generate`): `refreshToken` is now optional/deprecated on `AuthResult` and `RefreshRequest`.
5. Update the Jest specs of `AuthService` (no storage of the refresh token; refresh and logout send the header and credentials).
6. Done in code: the default is `false`. Verify in the browser dev tools: login response has no `refreshToken`, the cookie shows `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth`, and `document.cookie` does not list it. An environment that still sets `Auth__ReturnRefreshTokenInBody=true` (older compose files, QA stacks) must remove it.
7. Deployment: `Cors__Origins` must contain the web app's public origin (for example `https://ims.example.com`), see section 3.

Rollback is a configuration change: set `Auth__ReturnRefreshTokenInBody=true` and the old body delivery returns without a redeploy of the API.

## 6. Tests

`tests/Ims.Api.Tests/Tests/Auth/RefreshCookieTests.cs` (attributes, rotation, replay, logout, CSRF refusals, CORS credentials, switch off, wildcard origin),
`RefreshTokenCookieEnvironmentTests.cs` (when `Secure` may be dropped), `Tests/Contract/SecurityHeadersTests.cs` (OpenAPI deprecation, `no-store`).
