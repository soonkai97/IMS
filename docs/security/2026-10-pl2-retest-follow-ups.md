# PL2.11 security retest: follow-ups F-1 to F-4

Source: the PL2.11 purchasing security retest (report `docs/reviews/pl2-security-retest/report.md` in PR #181, probes in `tests/security/pl2-purchasing/`). No High or Medium finding; four Low findings in the API. This note records how each one was closed.

| Id | Class | Finding | Status | Where |
| -- | ----- | ------- | ------ | ----- |
| F-1 | API8 | A malformed JSON type (`{"lines":{}}`, `{"username":{},"password":1}`) echoed CLR type names, the JSON path and byte positions in the 400 body | Fixed | `ProblemResponses.FromModelState` |
| F-2 | API1/API5 | `If-Match: *` switched off the required concurrency token, so a plain `POST .../approve` succeeded without a version | Fixed | `IfMatchHeader.Read` |
| F-3 | API8, ASVS 8.2 | Authenticated JSON that carries cost data had no `Cache-Control` | Fixed | `SecurityHeadersMiddleware` |
| F-4 | API2 | Login takes 15 to 50 ms longer for an existing username (failed-login write and audit) | **Accepted by the owner** | `LoginHandler`, unchanged |

## F-1: no framework text in a 400

A model error that carries an exception, or has no message, is now answered with `Messages.RequestNotValid`. The field key stays in `errors` (so a client can point at the field); the framework text never does. Messages written by our own validators are returned as before.

## F-2: `*` is not a version

`IfMatchHeader.Read(request, required)`:

| Header | `required: true` (documents, purchase orders, role permission replace) | `required: false` (role update) |
| ------ | ------------------------------------------------------------------- | ------------------------------- |
| missing, empty, only quotes | 428 | no check |
| `*` | **428** (was: no check) | no check (unchanged) |
| a token | checked, 409 when stale | checked, 409 when stale |

A client reads the entity first and sends its `concurrencyToken` (or the `ETag` header). `tests/smoke/smoke.mjs` does this now.

## F-3: API responses are not cacheable by a shared cache

`SecurityHeadersMiddleware` adds `Cache-Control: private, no-store` to every response under `/api` when the endpoint did not set the header itself, at the moment the response starts, so error responses are covered too. The accounting export and attachment downloads set the same value themselves (`SecurityHeaders.NoStore`). Swagger, `/openapi`, `/health` and the web app's static files keep their own caching.

## F-4: login timing, risk accepted

Decision (owner, 2026-10-22): do not change `LoginHandler`. A caller can tell an existing username from an unknown one by about 15 to 50 ms.

Why it is acceptable:

1. The login endpoint is rate limited to 20 requests a minute per client, and an account locks after 5 failures, so the difference cannot be measured at the volume it needs.
2. User names are not secret in this product: they are shown on documents and in the audit trail to every signed-in user.
3. Padding every failed login to a fixed time, or writing the same rows for an unknown user, adds cost and code to the most sensitive path for a small gain.

Revisit when the product is exposed to the public internet without the rate limit and lock in front of it, or when user names become private. The probe `tests/security/pl2-purchasing/p7b-timing.mjs` keeps measuring it.
