# IMS web

Angular 21 PWA for the Inventory Management System (Phase 1 MVP). Nx monorepo, one app (`apps/main`), strict four-tier libraries
under `libs/`. Standalone components, signals, zoneless, lazy routes, strict TypeScript.

Architecture and decisions: [`../docs/architecture/frontend.md`](../docs/architecture/frontend.md). Conventions: [`docs/`](docs).

## Run it

Needs Node 20.19+ and the API on http://localhost:5109 (CORS allows http://localhost:4200).

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
| `npm run contrast`                     | WCAG AA check of the colour palette (light and dark)                    |
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
`openapi/openapi.raw.json`, applies the workarounds listed in the script header (OpenAPI 3.1 to 3.0 types, enum values,
operation ids, file downloads) into `openapi/openapi.normalized.json`, and NSwag writes
`libs/shared/api/src/lib/api-client.ts`. The generated client is committed and never edited. Wrap it in a feature store.

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
