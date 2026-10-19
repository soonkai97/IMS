# Frontend architecture (web/)

Owner: fe-architect (decisions), fe-framework (implementation). Source docs: `docs/prd-v0.2.txt` (3, 5, 9, 10), `docs/design/mvp-ux.md`,
`TASKS.md` T07/T19. Run and build commands: `web/README.md`. Conventions: `web/docs/`.

## 1. Shape

Angular 22 (zoneless, standalone components, signals, strict TypeScript) in an **Nx monorepo** (`web/`). One app, `apps/main`;
everything else is a library under `libs/`, imported **only through `@ims/*` path aliases** (`tsconfig.base.json`).

Projects are named `{directory}-{name}` (`core-identity`, `containers-documents`). The structure and conventions follow the
team's existing Nx/Angular practice (four tiers, scope tags, signal-first components, token-based SCSS); no code or assets were
copied from another project.

### Four tiers (enforced)

| Tier (tag)                                                 | Aliases                                                                                      | May import                                | Holds                                                                                                                            |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------------------- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| shared (`scope:shared`)                                    | `@ims/api`, `@ims/models`, `@ims/utils`, `@ims/ui`, `@ims/styles`, `@ims/assets`             | shared                                    | generated client, models and constants, helpers, `app-*` UI kit, design tokens, static assets                                    |
| core (`scope:core`)                                        | `@ims/core/{http,i18n,identity,navigation,notification,pwa,settings,theme}`                  | core, shared                              | system infrastructure: interceptors, i18n, auth + permissions, shell/menus, toasts/confirm, service worker, theme                |
| features (`scope:features`)                                | `@ims/features/{administration,approvals,audit,catalog,documents,reports,stock,warehousing}` | features, core, shared                    | business logic and data access (stores). **No pages.**                                                                           |
| containers (`scope:containers`, `scope:containers-shared`) | `@ims/containers/{...}`                                                                      | containers-shared, features, core, shared | routed UI pages per area, lazy-loaded. `*-shared` containers hold UI several containers reuse (`catalog-shared`: product picker) |

Dependencies point downward only. A higher tier never reaches sideways into a peer container: extract to a lower tier or a
`*-shared` container. `apps/main` (`scope:app`) may use everything and wires tiers together (`NAV_BADGE_SOURCES` lets features feed menu badges
without core depending on features). The rules live in `tools/libs.mjs` (`SCOPE_RULES`) and are enforced by
`@nx/enforce-module-boundaries`: a violation fails `npm run lint`. Cross-library relative imports are rejected by the same rule.

## 2. Data flow and state (signals, no NGXS/NgRx)

```
Page (container)  ->  feature store (signals)  ->  NSwag client (@ims/api)  ->  HttpClient + interceptors  ->  API
        ^                    |
        +---- $data / $operation signals (idle | loading | success | failure + ApiError)
```

- Every feature has small `@Injectable({ providedIn: 'root' })` stores that wrap the generated client. Reads use
  `QueryLoader<Query, Result>` (latest-wins, exposes `$data`, `$operation`, `$isLoading`); writes use `ApiCommand<Arg, Result>`
  (`run(arg)` returns an observable that emits once and completes **empty** on failure, so callers never need an error callback).
  Both live in `@ims/utils`; the operation model (`ApiOperation`, `API_STATUS`) in `@ims/models`. Every call therefore has the same
  uniform status for loading / error / empty UI.
- Pages build a `computed()` query from filter signals and call `loadOnChange(query, load)`; `createPageState()` resets the page
  number whenever a filter changes; `debouncedSignal()` debounces search boxes (300 ms). No `subscribe()` callbacks with logic:
  side effects go in `tap`.
- Paging is server-side everywhere a list can grow (`PagedQuery { page, pageSize, sortBy?, desc? }`); `app-table` shows server
  sort, `app-pager` the range. Seek paging (opaque `cursor` / `nextCursor`, never parsed) exists for ledger / documents / audit and is not needed for the current grids.
- Table toolbar contract (TB). `app-pager`: First / Previous / Next / Last and, with `pageSizes` (`PAGE_SIZES` 25/50/100) plus
  `(pageSizeChange)`, a "Rows per page" select; First, Last and the select appear only when the pager's own box is 45rem or wider.
  `createPageState` owns page and size (a new size goes back to page 1). The focus never moves on a page or size change: a button the
  new page cannot use is `aria-disabled` (still focusable), so a keyboard user pages on from the button, and a polite live region
  announces "Page 2 of 14, 26-50 of 333" once (the join is the `ui.pageAnnouncement` message). `app-table` `columnMenu` (opt-in): a
  "Columns" button opens a native `popover` with one checkbox per column except the `primary` column and `type: 'action'` columns
  (give every button column that type). Hidden columns apply to the wide table only; the card list shows all. Not remembered yet
  (persistence belongs to the saved-state slice). Browser floor for the popover: Chrome 114, Safari 17, Firefox 125; below it the
  toolbar is hidden (`@supports selector(:popover-open)`), so every column shows and nothing breaks.
- Documents: the server sends `allowedActions[]`; `app-document-actions` renders **only** those buttons (D3). The UI never
  re-implements who may do what. `DocumentEditorViewService` holds the typed reactive form (header + `FormArray` of line groups),
  totals and shortfall maths (`features-documents/lines`). Insufficient stock: E1 inline per line (blocked) or E2 (override needed when the
  system setting allows negative stock); the server re-checks and a 409 with `productId/available/requested` is shown as E5.

## 3. Cross-cutting (core)

| Concern             | Where                                    | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| ------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Auth                | `core-identity`                          | access token in memory only; the refresh token is an **HttpOnly cookie** (SEC-34, `docs/security/refresh-cookie.md`) the app never reads, stores or sends; **single-flight refresh** with rotation; `restoreSession()` = one `POST /api/auth/refresh` at start-up (401 = signed out, silently); 401 -> refresh -> replay once -> else sign out with return URL. A leftover localStorage refresh token of an older release is deleted once                     |
| Permissions         | `core-identity` `PermissionService`      | built from `GET /api/auth/me`; route guards `requirePermission(...)`, menus filter by permission; unknown permission -> `/forbidden`, unknown URL -> not-found page                                                                                                                                                                                                                                                                                           |
| HTTP                | `core-http`                              | interceptor order: error mapping (ProblemDetails -> `ApiError`, toast of `detail`), correlation id, credentials (`withCredentials` on API calls), CSRF header (`X-Requested-With` on refresh and logout), If-Match (from the call's `HttpContext`), loading bar, timeout (35 s, 125 s for export/import), auth. Field errors (`errors`) go to the form, never a toast; a 409 on a call that sent If-Match is explained by the screen (banner), not by a toast |
| Config              | `core-http` `AppConfig`                  | `assets/config.json` overrides the build default at start-up; AuthService is created only after it loads                                                                                                                                                                                                                                                                                                                                                      |
| i18n                | `core-i18n`                              | runtime language switch (en bundled, zh/ms lazy), typed keys (`MessageKey`), ICU-lite plural, impure `t` pipe, `enumLabel`, date/qty/money pipes (Asia/Kuala_Lumpur, <= 4 decimals)                                                                                                                                                                                                                                                                           |
| UI text for the kit | `UI_TEXT` token                          | shared/ui cannot depend on core, so i18n provides the kit's words                                                                                                                                                                                                                                                                                                                                                                                             |
| Theme               | `core-theme` + `libs/shared/styles`      | light/dark/system, role-named tokens (`accent`, `canvas`, ...) as CSS custom properties, old names kept as aliases (`$aliases`) until R24, 162 WCAG AA checks by `npm run contrast`                                                                                                                                                                                                                                                                           |
| PWA                 | `core-pwa`, `apps/main/ngsw-config.json` | installable; offline app shell; the worker never caches or answers an API call (decision 18); update prompt toast; leftover data caches removed at start and at session end (best effort). No offline reads of data, no offline writes                                                                                                                                                                                                                                                                                                     |
| Notifications       | `core-notification`                      | `ToastService` (aria-live; errors persist), `ConfirmService.ask()` returns an observable that emits the reason on confirm                                                                                                                                                                                                                                                                                                                                     |

## 4. API contract

`nswag.json` + `scripts/fetch-openapi.mjs` generate `libs/shared/api/src/lib/api-client.ts` (Angular `HttpClient`, `API_BASE_URL`
injection token, interfaces, string enums, `FileResponse` for downloads with the `Content-Disposition` name, a trailing `HttpContext`
parameter on every call). The server's OpenAPI document is correct at the source since the API-contract change (string enums,
`Tag_action` operation ids, binary download responses, inline multipart files, UTC `Z` timestamps); the only rewrite left in the
script is OpenAPI 3.1 nullability to the 3.0 flavour NSwag 14 reads (plus `additionalProperties: false` so generated interfaces have
no index signature). The generated file is committed and never edited.

Contract features the screens rely on:

- **Concurrency (E4)**: every document response carries `concurrencyToken` (= the `ETag`). Commands (`save` of an existing draft,
  submit, approve, reject, cancel, post, reverse) send it back with `withIfMatch(token)`; a stale copy answers 409 and the editor shows
  the "changed since you opened it" banner with Reload. Coming back to the tab compares the token of the list item with the opened
  copy, so the banner can appear before any save.
- **Counts and flags**: Home and the approvals badge read `GET /api/documents/counts`; the approvals inbox is one list call
  (`status=Submitted&requiresApproval=true`) built from the list item flags (`isLarge`, `requiresOverride`, `isReturned`,
  `submittedById`); Receipts and Issues use `mine=true` for "Mine" (default Mine for warehouse staff and clerks, remembered per user).
- **Override reason at Approve**: `requiresOverride` documents ask for the reason in the Approve dialog; Post has no body.
- **Lines carry `available`** (stock on hand now), so opening a document needs no stock query; stock rows, low-stock alerts and ledger
  rows carry `warehouseName`.
- **Product lookup** answers `{ found, product }` (a miss is a quiet message, not an error); import rows carry `New` / `Update` /
  `Error` and the preview fills the counts.
- **Partners**: `partners.create` shows New (Clerk, Supervisor, Admin); `masterdata.manage` (Supervisor, Admin) shows edit and
  deactivate; a 409 duplicate shows the server's sentence on the name field.

## 5. Decisions (others must respect)

1. State is signal based (`QueryLoader` / `ApiCommand`); no NGXS/NgRx.
2. `allowedActions` decides document buttons; Approve is shown disabled with a reason only when self-approval is the sole blocker.
3. Menus per role come from permissions (`MENU_ITEMS`), phone tab bar has four role-specific tabs + More (docs/design/mvp-ux.md 1.2; supervisor "Docs" tab = Receipts).
4. "Submit & post" is one button for roles with `document.post`; if the server says approval is required it only submits (D4).
5. Import is all-or-nothing: Confirm is disabled until the preview has 0 error rows (D6).
6. Every timestamp from the API is UTC with a trailing `Z` and is shown in Asia/Kuala_Lumpur (`new Date(value)` is enough; no
   "assume UTC" patching). Date-only filters (`from`, `to`) are Kuala Lumpur calendar days. They are typed in `app-date-field` (text `dd/MM/yyyy` in every language, owner decision; also reads `yyyy-MM-dd`, `dd MMM yyyy` with month names from `Intl`, and `2026年10月15日`). Every date and time shown (tables, details, timelines) goes through `formatDateText` / `formatTimestampText` in `@ims/utils` (`dd/MM/yyyy`, `dd/MM/yyyy HH:mm` 24 hour, business time zone); never format a date in a page; its form value is still the ISO day, and a visible native `<input type="date">` is not used (it shows the browser's format, not the app's). The calendar button calls `showPicker()` on a hidden date input.
7. The document editor reserves a tab list so an Attachments panel (photos, PDFs stored as server files, never base64) can be added without restructuring.
8. No token is ever written to web storage. A new call that needs the concurrency check passes `withIfMatch(...)`; it never builds the header by hand.
9. `new` and a document id are ONE route (`/receipts/:id`, `NEW_DOCUMENT_ID`), so the editor lives on when a save moves `/new` to `/<id>`: the form is marked clean when the first save returns and the editor follows the saved document, which makes Save, Submit and a repeat after Back in the Post dialog idempotent (no "Discard changes?", no second document).
10. A 409 is told apart by its `code` (`document_changed`, `insufficient_stock`, `reversal_blocked`); an API without codes falls back to the old product-id test (`libs/features/documents/src/lib/rules/stale-document.ts`).
11. Routes carry `title: '<message key>'`; `PageTitleStrategy` translates it, `PageTitleService` adds a detail (the document number) and feeds the live region of the shell that announces route changes.
12. A dialog focuses the native control marked `data-autofocus` once its content exists (`app-search-input focusOnOpen`); the real `autofocus` attribute is banned by lint. A busy `app-button` keeps the focus (it is `aria-disabled`, not natively disabled).
13. Sticky and fixed bars become part of the page flow on windows shorter than 500px (`media-short`), and `html` has `scroll-padding` for the top bar and the bottom bars (WCAG 1.4.10, 2.4.11).
14. No UI component library, no bake-off (product owner, 2026-10-11). Angular CDK may be used for behaviour only (overlay, listbox, scrolling, drag-drop, a11y, clipboard), only inside `libs/shared/ui`, never exposed through our public API, never with its styles. Rules and slices: `docs/design/2026-refresh-plan.md` (N1, N2, programme A).
15. Components adapt to their container, not the window; viewport media queries are for the shell, the page frame and user-environment features (N5, programme L). Ids are opaque strings in new code (N13). Every slice passes an independent review before merge (`docs/team/PR-REVIEW.md` section 8).
16. Every entity id on the wire is a GUID string (`PublicId` in `@ims/models`; ADR guid-keys): never parsed, compared numerically or sorted for meaning, and an id that comes from outside the API (a link, a value saved on the device) is read through `normalizePublicId` (`@ims/utils`: a GUID in the API's lower case, or undefined); a route id that is not `new` and not a GUID is a not-found page with no request (receipt and issue editor, product editor, product stock). `LedgerRow.id` and `AuditLogDto.id` stay numbers and are only row keys. Document lines have no id (`track $index`). The audit `entityId` of a document is its NUMBER. Device state keyed by a user id (`ims.documents.scope.<userId>`) simply starts fresh after the flip.
17. Overlays (R13): `app-dialog` is one native `<dialog>`: a dialog from 600px, a bottom sheet below (16px top corners, safe-area padding, no swipe gesture). Entrance only, by `@starting-style`; the exit is instant because the panel leaves the DOM with the close. `app-toast` fades (the toast host owns the animation). Banner, card and empty state are still pictures: no entrance, no disclosure. Every duration, easing, distance and scale is a token (`duration()`, `easing()`, `move('md')`, `pop-scale()`, `slide-distance()`); reduced motion and `data-motion="off"` set the distances to 0 and the scales to 1, so only the opacity changes (`motion.spec.ts` fails on a literal). The scrim is `--ims-scrim` (overlay colour at 50% light, 60% dark). Free text in an overlay uses `wrap-anywhere`, so a 500-character value never widens it.

18. Service worker and API data (QA round S1 and defect 2, 2026-10-19). `ngsw-config.json` has an app-shell `assetGroups` and **no `dataGroups`**: no API call is cached or answered by the worker, so a signed-out page can read nothing from a cache and a product created elsewhere shows at once. The earlier `master-data` group (cache-first for 12 h) did both wrong, and a page cannot fix that from outside: the worker keeps its own handle on a Cache and keeps answering from it after `caches.delete`, so clearing at sign-out never worked. `PwaService` still deletes `:data:` caches at start (leftovers of an older release) and when the session ends, as a best effort. **What this costs.** The pickers, lists and the Add item lookup no longer answer from a cache: every read needs the network, so they do not work offline any more (the installed app still starts and shows its shell and the sign-in page, but the session cannot be restored offline and no data shows), and the 12 h cache speed-up on repeat visits is gone. That was accepted because the cache served signed-in data to anyone, signed out, and hid new data for up to 12 h. If offline reads are ever wanted they need a network-first, per-user, short-lived design and a security review, not a long cache-first group. Pinned by `apps/main/src/app/ngsw-config.spec.ts` and the service-worker scenarios in `e2e/tests/34-service-worker.spec.ts` (production build only).
    **Release note (upgrade window).** `ngsw-worker.js` is byte-identical between the old and the new build, so no new worker is installed: the browser moves a tab to the new version at the next `ngsw.json` check plus one navigation. A tab or installed-PWA window that stays open and is not reloaded across the deploy keeps the old version in memory and goes on answering cached API addresses (up to 12 h old) with 200 after sign-out, until it is closed; no code in a release can reach it. Tell every user once to close or reload all IMS tabs and windows after this release. **Kill switch.** The build emits `safety-worker.js` (Angular's self-destroying worker). If a worker ever has to be removed in the field (a bad cache, a data exposure like S1): serve the bytes of `safety-worker.js` at the URL `/ngsw-worker.js` (proxy rewrite or file copy, keep `no-cache`); at its next update check (on a navigation, within about a day at the latest) every client unregisters its worker and deletes every `ngsw:` cache, and the app then runs with no worker (online only, no installed-shell offline start). Remove the rewrite and redeploy normally to bring the worker back. `ngsw-bypass` (a header or query parameter) only skips the worker for one request and is for diagnosis, not a fix.
19. A session that ends by expiry (a refresh the server refuses) is not a "Discard changes?" question: the page can no longer save, and "Keep editing" only repeats the failure. `AuthService.endSession(true)` first emits `sessionExpiring$` (the user is still known) so a screen can keep its unsent work through `SessionDraftService` (sessionStorage, this tab only, key `ims.draft.<userId>.<screen key>`, stored as `{ savedAt, value }`, valid for 15 minutes (`DRAFT_TTL_MS`), at most 1,000,000 characters, restored once), sets `$sessionExpired` (the unsaved-changes guard then lets the navigation through) and sends the user to `/login?returnUrl=<page>&reason=expired-kept` (`expired` when nothing was kept) where the notice, tied to the focused field by `aria-describedby`, says plainly whether the changes are kept or lost. Drafts are removed at an explicit sign-out and at a sign-in of another user. The document editor keeps and restores its form (header and lines; `version: 1`, an unknown version is refused); it restores a saved draft only if the document is the version the changes were made on and still editable, otherwise it says they were not restored. What comes back from storage is untrusted (script or DevTools on the origin can write anything): `parseEditorDraft` rebuilds it field by field, ids through `normalizePublicId` (decision 16), at most `MAX_DOCUMENT_LINES` lines, text and numbers within the editor's own limits, and a draft that fails is dropped with the same "not restored" notice. Storing a draft in sessionStorage is an exception to the R26 ban (docs/design/2026-refresh-plan.md); R26 (sign in again in place) stays the preferred long-term fix. Other forms (product, partner, user, role) keep nothing yet: their notice says unsaved work is lost.

## 6. Testing

Jest + `jest-preset-angular` (zoneless) + Spectator per project. Covered: auth refresh and cookie session logic, permission service,
auth / error / correlation / credentials / CSRF / If-Match interceptors, document action visibility from `allowedActions`, the
document store commands (If-Match, no body on post), stale-document detection, approvals inbox and its visibility rule, Home tiles per
role, Mine / All lists, editor flows (override reason at approve, E4/E5 banners, returned banner), product lookup miss, import preview
statuses, partner permissions and the 409 duplicate, status chip, line totals and shortfall maths, navigation per role,
`QueryLoader` / `ApiCommand`, message format and catalog completeness. E2E (Playwright) belongs to
behavior-test-engineer (`apps/main-e2e`, not created yet).

## 7. Known gaps / follow-ups

See the 2026-10-09 fe-framework entry in `docs/team/HANDOFFS.md` (API gaps) and the "not done" list there.
