# IMS web

Angular 22 PWA for the Inventory Management System (Phase 1 MVP). Nx monorepo, one app (`apps/main`), strict four-tier libraries
under `libs/`. Standalone components, signals, zoneless, lazy routes, strict TypeScript.

Architecture and decisions: [`../docs/architecture/frontend.md`](../docs/architecture/frontend.md). Conventions: [`docs/`](docs).

## Run it

Needs Node 22.22.3+ (or 24.15+) and the API on http://localhost:5109. Its `Cors:Origins` must list http://localhost:4200: the
refresh cookie only travels to listed origins.

```bash
npm install
npm run dev            # http://localhost:4200
```

Sign in with a demo user (admin, owner, supervisor, staff, clerk). The dev-only passwords live in
`src/Ims.Api/appsettings.Development.json` (Seed section); nothing is hard-coded in this repo.

Start the API (separate shell):

```bash
cd src/Ims.Api
set ASPNETCORE_ENVIRONMENT=Development && set ASPNETCORE_URLS=http://localhost:5109 && dotnet run --no-build
```

The API URL is runtime configuration: `libs/shared/assets/src/assets/config.json` (`{"apiBaseUrl": "..."}`, empty = same
origin). Edit the deployed `assets/config.json` to repoint a build without rebuilding.

## Commands

| Command                                | What it does                                                            |
| -------------------------------------- | ----------------------------------------------------------------------- |
| `npm run dev`                          | dev server on port 4200                                                 |
| `npm run build` / `npm run build:prod` | development / production build to `dist/main/browser`                   |
| `npm run serve:prod`                   | serve the production build on :4200 (needed to try the service worker)  |
| `npm test`                             | Jest (jest-preset-angular + Spectator) for every project                |
| `npm run coverage`                     | same with coverage (`coverage/`)                                        |
| `npm run lint`                         | ESLint (module boundaries, conventions) + Stylelint                     |
| `npm run format` / `format:check`      | Prettier                                                                |
| `npm run api:generate`                 | download `/openapi/v1.json`, normalise it, regenerate the NSwag client  |
| `npm run contrast`                     | WCAG AA gate: 162 palette checks (81 pairs x light, dark) + alias check |
| `npm run dep-graph`                    | Nx project graph                                                        |
| `node tools/scaffold-libs.mjs`         | (re)generate library boilerplate and path aliases from `tools/libs.mjs` |
| `node tools/scan-i18n-keys.mjs`        | list message keys used in code but missing in the English catalog       |

Single project: `npx nx test core-identity`, `npx nx lint shared-ui`, `npx nx test core-identity -t "refresh"`.

## Layout

```
apps/main                 bootstrap, routes (lazy), providers, NAV badge wiring
libs/shared/{api,assets,models,styles,ui,utils}      -> @ims/api  @ims/ui  ...      (shared only)
libs/core/{http,i18n,identity,navigation,notification,pwa,settings,theme}  -> @ims/core/*
libs/features/{administration,approvals,audit,catalog,documents,reports,stock,warehousing} -> @ims/features/*
libs/containers/{administration,approvals,audit,authentication,catalog,catalog-shared,documents,home,reports,stock,system,warehousing}
                                                                                    -> @ims/containers/*
```

Dependencies point downward only; `npm run lint` fails on a violation. Details: [`docs/library-creation.md`](docs/library-creation.md).

## API client

`nswag.json` + `scripts/fetch-openapi.mjs`. The script downloads the OpenAPI document, saves it as
`openapi/openapi.raw.json`, converts the OpenAPI 3.1 nullability to the 3.0 flavour NSwag 14 reads (nothing else: string enums,
`Tag_action` operation ids, binary downloads, inline multipart files and UTC `Z` timestamps come right from the server) into
`openapi/openapi.normalized.json`, and NSwag writes `libs/shared/api/src/lib/api-client.ts`. The generated client is committed and
never edited. Wrap it in a feature store. With the API running: `OPENAPI_URL=http://localhost:5109/openapi/v1.json npm run api:generate`
(the default URL is `http://localhost:5109/openapi/v1.json`).

`includeHttpContext` is on, so every generated call takes a trailing `HttpContext`. `withIfMatch(document.concurrencyToken)` from
`@ims/core/http` puts the token there and an interceptor sends it as `If-Match` (a stale document answers 409, UX error E4).

## Session (refresh cookie, SEC-34)

The access token lives in memory only. The refresh token is an HttpOnly cookie the page cannot read: the app never stores or sends
it. `credentialsInterceptor` sends cookies to the API, `csrfHeaderInterceptor` adds `X-Requested-With: XMLHttpRequest` to refresh
and logout, and start-up makes exactly one `POST /api/auth/refresh` (401 = signed out, no notice). A refresh token that an older
release left in localStorage is deleted once. Production serves the web app and `/api` from one host (`apiBaseUrl: ''`); the strict
CSP in [`../docs/security/web-headers.md`](../docs/security/web-headers.md) works because the production build sets
`inlineCritical: false` (no inline style or `onload` handler in `index.html`).

## Cost and value visibility

A cost or value shows only to a user who holds the permission, never by role name (`docs/design/mvp-ux.md` 1.4): `cost.view`
(unit cost, line cost, document totals, approval amounts, the movements Amount column) and `valuation.view` (stock value, the
stock total on Home, sorting stock by value). Both come in the `permissions` list of the sign-in and `me` answers
(`PERMISSION.CostView`, `PERMISSION.ValuationView`). The API sends `null` for a field the caller may not see, and the screens do
not render it at all (no `0.00`, no `-`, no empty column):

- templates: `<ng-container *appCan="'cost.view'">...</ng-container>` (`CanDirective`, `@ims/core/identity`);
- table columns: `...columnWhen(permissions.has(PERMISSION.CostView), { key: 'amount', ... })` (`@ims/ui`).

The e2e scenario `13-cost-visibility` checks Warehouse staff, Clerk and Supervisor on the real screens.

## Colour palette and contrast gate

The palette (2026 refresh, `docs/design/2026-refresh-visual.md` 2.2) is two maps of role-named tokens, `$light` and `$dark`, in
`libs/shared/styles/src/variables/_palette.var.scss`: `canvas`, `surface`, `surface-raised`, `surface-sunken`, `surface-strong`,
`text`, `text-muted`, `accent`, and the roles `success`, `warning`, `danger`, `info` (each with `on-<role>`, `<role>-soft`,
`on-<role>-soft`), `focus`, `chart-1..6`. It is the only file that may hold a hex value (stylelint `color-no-hex`); stylelint also
rejects named colours and `rgb()` / `hsl()` calls (the three shadows in `variables/_shape.var.scss` are exempt until slice R05
moves them into theme custom properties).

The names used before the refresh (`bg`, `surface-2`, `surface-3`, `primary*`, `warning-border`, `status-*`) still work through the
`$aliases` map (old name to canonical name; no value is copied, so an alias cannot drift). `color('primary')` compiles to
`var(--ims-color-accent)`. A new component uses the canonical names; the aliases are removed in slice R24.

`npm run contrast` (`scripts/check-contrast.mjs`) runs the 81 pairs of the visual spec in light and dark (162 checks: 4.5 text,
3 UI components, borders, focus ring, chart series) plus a few call-site pairs, and exits 1 when a pair misses its ratio, when
`$light` and `$dark` differ in keys, when a value is not a plain 6-digit hex, when an alias differs from the mapping of spec 6.2
pinned in the script, or when a token is in no pair (only `border` and `overlay` are exempt, with their reason in the script). A
new token enters the palette together with the pairs that use it. `--all` prints every ratio. `scripts/check-contrast.test.mjs`
(node:test, run by the same command) damages copies of the palette and proves each of those failures exits 1. CI runs it before
the production build.

## Internationalisation

English, Chinese (zh), Malay (ms); switch at runtime (login page or account menu), stored in localStorage.
Catalogs: `libs/core/i18n/src/lib/messages/{en,zh,ms}.messages.ts`. English is the source; zh and ms must contain every
key (compile-time check + a unit test that also compares placeholders).

> TODO-review: the zh and ms texts are machine-quality drafts. A native speaker should review them before release
> (marked with a `TODO-review` comment at the top of each file).

## PWA

`@angular/service-worker` (production builds): installable (manifest, icons), offline app shell, master-data GETs
(products, categories, units, warehouses, partners, settings) are cached with a refresh-ahead (stale-while-revalidate)
policy (`apps/main/ngsw-config.json`). No offline writes in Phase 1. The cached API data is dropped on sign-out.
