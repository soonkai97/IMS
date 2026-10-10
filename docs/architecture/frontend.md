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

| Tier (tag) | Aliases | May import | Holds |
|---|---|---|---|
| shared (`scope:shared`) | `@ims/api`, `@ims/models`, `@ims/utils`, `@ims/ui`, `@ims/styles`, `@ims/assets` | shared | generated client, models and constants, helpers, `app-*` UI kit, design tokens, static assets |
| core (`scope:core`) | `@ims/core/{http,i18n,identity,navigation,notification,pwa,settings,theme}` | core, shared | system infrastructure: interceptors, i18n, auth + permissions, shell/menus, toasts/confirm, service worker, theme |
| features (`scope:features`) | `@ims/features/{administration,approvals,audit,catalog,documents,reports,stock,warehousing}` | features, core, shared | business logic and data access (stores). **No pages.** |
| containers (`scope:containers`, `scope:containers-shared`) | `@ims/containers/{...}` | containers-shared, features, core, shared | routed UI pages per area, lazy-loaded. `*-shared` containers hold UI several containers reuse (`catalog-shared`: product picker) |

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

* Every feature has small `@Injectable({ providedIn: 'root' })` stores that wrap the generated client. Reads use
  `QueryLoader<Query, Result>` (latest-wins, exposes `$data`, `$operation`, `$isLoading`); writes use `ApiCommand<Arg, Result>`
  (`run(arg)` returns an observable that emits once and completes **empty** on failure, so callers never need an error callback).
  Both live in `@ims/utils`; the operation model (`ApiOperation`, `API_STATUS`) in `@ims/models`. Every call therefore has the same
  uniform status for loading / error / empty UI.
* Pages build a `computed()` query from filter signals and call `loadOnChange(query, load)`; `createPageState()` resets the page
  number whenever a filter changes; `debouncedSignal()` debounces search boxes (300 ms). No `subscribe()` callbacks with logic:
  side effects go in `tap`.
* Paging is server-side everywhere a list can grow (`PagedQuery { page, pageSize, sortBy?, desc? }`); `app-table` shows server
  sort, `app-pager` the range. Seek paging (`afterId`) exists for ledger / documents / audit and is not needed for the current grids.
* Documents: the server sends `allowedActions[]`; `app-document-actions` renders **only** those buttons (D3). The UI never
  re-implements who may do what. `DocumentEditorViewService` holds the typed reactive form (header + `FormArray` of line groups),
  totals and shortfall maths (`features-documents/lines`). Insufficient stock: E1 inline per line (blocked) or E2 (override needed when the
  system setting allows negative stock); the server re-checks and a 409 with `productId/available/requested` is shown as E5.

## 3. Cross-cutting (core)

| Concern | Where | Notes |
|---|---|---|
| Auth | `core-identity` | access token in memory, refresh token in localStorage, **single-flight refresh** with rotation, `restoreSession()` at start-up, 401 -> refresh -> replay once -> else sign out with return URL |
| Permissions | `core-identity` `PermissionService` | built from `GET /api/auth/me`; route guards `requirePermission(...)`, menus filter by permission; unknown permission -> `/forbidden`, unknown URL -> not-found page |
| HTTP | `core-http` | interceptor order: error mapping (ProblemDetails -> `ApiError`, toast of `detail`), correlation id, loading bar, timeout (35 s, 125 s for export/import), auth. Field errors (`errors`) go to the form, never a toast |
| Config | `core-http` `AppConfig` | `assets/config.json` overrides the build default at start-up; AuthService is created only after it loads |
| i18n | `core-i18n` | runtime language switch (en bundled, zh/ms lazy), typed keys (`MessageKey`), ICU-lite plural, impure `t` pipe, `enumLabel`, date/qty/money pipes (Asia/Kuala_Lumpur, <= 4 decimals) |
| UI text for the kit | `UI_TEXT` token | shared/ui cannot depend on core, so i18n provides the kit's words |
| Theme | `core-theme` + `libs/shared/styles` | light/dark/system, tokens as CSS custom properties, WCAG AA verified by `npm run contrast` |
| PWA | `core-pwa`, `apps/main/ngsw-config.json` | installable; offline shell; master-data GETs cached with refresh-ahead (SWR); update prompt toast; cached API data deleted on sign-out. No offline writes |
| Notifications | `core-notification` | `ToastService` (aria-live; errors persist), `ConfirmService.ask()` returns an observable that emits the reason on confirm |

## 4. API contract

`nswag.json` + `scripts/fetch-openapi.mjs` generate `libs/shared/api/src/lib/api-client.ts` (Angular `HttpClient`, `API_BASE_URL`
injection token, interfaces, string enums, `FileResponse` for downloads with the `Content-Disposition` name). The server's OpenAPI
document needs workarounds (script header; reported to be-framework): OpenAPI 3.1 type arrays, enums published as bare integers,
no operation ids, empty file schemas, downloads without a body. The generated file is committed and never edited.

## 5. Decisions (others must respect)

1. State is signal based (`QueryLoader` / `ApiCommand`); no NGXS/NgRx.
2. `allowedActions` decides document buttons; Approve is shown disabled with a reason only when self-approval is the sole blocker.
3. Menus per role come from permissions (`MENU_ITEMS`), phone tab bar has four role-specific tabs + More (docs/design/mvp-ux.md 1.2; supervisor "Docs" tab = Receipts).
4. "Submit & post" is one button for roles with `document.post`; if the server says approval is required it only submits (D4).
5. Import is all-or-nothing: Confirm is disabled until the preview has 0 error rows (D6).
6. Dates from the API without a zone designator are treated as UTC and shown in Asia/Kuala_Lumpur.
7. The document editor reserves a tab list so an Attachments panel (photos, PDFs stored as server files, never base64) can be added without restructuring.

## 6. Testing

Jest + `jest-preset-angular` (zoneless) + Spectator per project. Covered: auth refresh logic, permission service, auth / error /
correlation interceptors, document action visibility from `allowedActions`, status chip, line totals and shortfall maths,
navigation per role, `QueryLoader` / `ApiCommand`, message format and catalog completeness. E2E (Playwright) belongs to
behavior-test-engineer (`apps/main-e2e`, not created yet).

## 7. Known gaps / follow-ups

See the 2026-10-09 fe-framework entry in `docs/team/HANDOFFS.md` (API gaps) and the "not done" list there.
