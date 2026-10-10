# Upgrading Angular

Notes from the 21 to 22 upgrade (Angular 22.2, Nx 23.3, TypeScript 6.0). Use them as a checklist next time.

## Procedure

1. `npm view @angular/core version` and `npm view @nx/angular peerDependencies` to pick versions that fit together.
2. `npx nx migrate <nx-version>`. Nx pins the Angular packages to the version in its package group, which can be
   behind npm `latest`; bump the `@angular/*`, `@angular-devkit/*`, `@schematics/angular` and `@angular/build` ranges
   to the newest patch yourself before installing.
3. `npm install`, then `npx nx migrate --run-migrations --create-commits=false`.
4. Review the diff of every migration before keeping it (see below), delete `migrations.json` and
   `tools/ai-migrations/`, then run the full check set: lint, format:check, test, contrast, build:prod, audit.

## Gotchas found in 21 to 22

- Angular 22 requires TypeScript 6.0 (`>=6.0 <6.1`) and Node `^22.22.3 || ^24.15.0 || >=26`. `package.json`
  `engines` follow it; the CI workflows in `.github` must move from Node 20 to Node 22 (22.22.3 or newer).
- TypeScript 6 deprecates `baseUrl`. The Nx migration silences it with `ignoreDeprecations: "6.0"` in every tsconfig.
  We removed `baseUrl` instead and prefixed the path aliases with `./`. `tools/scaffold-libs.mjs` writes them that way.
- The Nx TypeScript 6 migration also pins `strict: false`, `types: ["*"]` and `noUncheckedSideEffectImports: false` in
  `tsconfig.base.json`. Do not keep them: `strict: false` would silently weaken the project, and build and tests
  pass with the TypeScript 6 defaults.
- The Angular `http-xhr-backend` migration adds `withXhr()` to `provideHttpClient`, because the default backend is now
  `fetch`. We kept `withXhr()` to preserve behaviour (HTTP interceptors, upload and download progress). Moving to
  `fetch` is a separate, testable change.
- The `safe-optional-chaining` migration wraps `a?.b` template bindings in `$safeNavigationMigration(...)` to keep the
  old `null` result (`?.` now returns `undefined`, like JavaScript). Where the receiving input is typed
  `T | undefined` (as in `app-page-header` and `app-toast`), revert the wrapper and use the plain expression.
- The Nx migration also writes `suppress` entries for two extended diagnostics into `tsconfig.app.json` and reformats
  `nx.json`. We dropped both; the build is clean without them.
- The `@nx/jest:jest` executor is deprecated and removed in Nx 24. Plan a move to the inferred `@nx/jest/plugin`
  targets (`nx g @nx/jest:convert-to-inferred`) before the next Nx major.
- `npm run format:check` on Windows with `core.autocrlf=true` reports every file; that is a line-ending artefact,
  CI (Linux) is the reference. `npx prettier --check --end-of-line auto ...` confirms it locally.
