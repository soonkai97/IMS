# Creating a library

Libraries live in `libs/{directory}/{name}` with project name `{directory}-{name}` and import path `@ims/{directory}/{name}`
(shared libraries drop the directory: `@ims/ui`). Directory = tier: `shared`, `core`, `features`, `containers`.

## 1. Register it

Add one line to `tools/libs.mjs`:

```js
lib('features', 'transfers'),                       // scope = tier
lib('containers', 'transfers'),
lib('containers', 'transfers-shared', 'containers-shared'),   // a shared container: other containers may import it
```

Then run `node tools/scaffold-libs.mjs`. It writes (idempotently) `project.json`, `tsconfig.json` / `.lib.json` / `.spec.json`,
`jest.config.ts`, `src/test-setup.ts`, `eslint.config.mjs`, an empty `src/index.ts` and the path alias in `tsconfig.base.json`.
The Nx generator (`nx g @nx/angular:library ...`) works too, but then apply the checklist below by hand.

## 2. Checklist (what the scaffold guarantees)

- [ ] `tsconfig.json` extends `../../../tsconfig.base.json`, empty `files`/`include`, references lib + spec, `strict`,
      `noImplicitOverride`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noFallthroughCasesInSwitch`,
      `moduleResolution: bundler`, Angular `strictTemplates` / `strictInjectionParameters` / `strictInputAccessModifiers`.
- [ ] `tsconfig.lib.json` extends the local `tsconfig.json`, excludes specs and `test-setup.ts`.
- [ ] `tsconfig.spec.json` extends the local one, `module: preserve`, `types: ["jest", "node"]`.
- [ ] `project.json`: name `{directory}-{name}`, `tags: ["scope:{scope}"]`, `implicitDependencies: ["shared-styles", "shared-assets"]`,
      `test` (`@nx/jest:jest`) and `lint` (`@nx/eslint:lint`, `.ts` and `.html`) targets.
- [ ] `eslint.config.mjs` re-exports the root config (selector prefix `app`, boundary rules, conventions).
- [ ] Path alias `@ims/...` -> `libs/.../src/index.ts` (alphabetical in `tsconfig.base.json`).
- [ ] `src/index.ts` is the only public surface (alphabetical `export *`). Everything else is private to the library.
- [ ] A library whose barrel the app shell imports but whose parts a lazy screen needs (today only `shared/ui`) carries a
      `package.json` with `{ "sideEffects": false }`. Without it the bundler keeps every `export *` module in the initial
      chunk (the whole kit, about 60 kB, was there); with it, a component only a lazy route uses moves to that route's chunk.
      So a module in such a library must have no top-level side effect (no registration, no `document` or `window` access).

## 3. Folder layout

```
libs/containers/{name}/src/
  index.ts                      exports the routes (and shared components for *-shared)
  lib/{name}.routes.ts          Routes array, guarded by requirePermission(...)
  lib/{page}/{page}.component.{ts,html,scss,spec.ts}
  lib/components|services|constants|types/   as needed, each with an index.ts
libs/features/{name}/src/
  lib/services/*.store.ts       signal stores wrapping the NSwag client
  lib/constants|types/
```

## 4. After creating

1. `npx nx lint {directory}-{name}` and `npx nx test {directory}-{name}`.
2. Lazy-load containers from `apps/main/src/app/app.routes.ts` (`loadChildren: () => import('@ims/containers/{name}')...`).
3. A new boundary violation fails lint: move the code down a tier or into a `*-shared` container instead of adding an exception.
