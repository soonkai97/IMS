# Coding practices (TypeScript)

Enforced by ESLint where marked (lint).

1. Brace every control statement, even one-liners (lint: `curly`).
2. Guard clauses and early returns; at most two to three levels of nesting.
3. No nested ternaries (lint). Use a lookup object, a `switch` (three or more cases on one value) or a small function.
4. `const` over `let`, never `var` (lint). Immutable updates (`map`, `filter`, spread) over mutation.
5. `??` and `?.` for null safety. Mind `??` versus `||`: `0` and `''` are valid values.
6. Named constants instead of magic values, kept in the library's `constants/` folder (`MIN_REASON_LENGTH`, `DEFAULT_PAGE_SIZE`).
7. Type guards (`isApiError`, `isDocumentAction`) instead of `as` assertions (lint restricts assertions). No `any` (lint),
   no non-null `!` (lint).
8. Side effects in `tap` or dedicated methods, not in `subscribe()` callbacks. Stores expose signals; they do not hand out subjects.
9. Public API of a library = `src/index.ts` only; imports across libraries only through `@ims/*` aliases (lint).
10. Comments explain why (a server quirk, a rule from the PRD, a UX decision id such as D4), not what.

## Error handling

API failures arrive as `ApiError` (`@ims/models`): `detail` is the server sentence (shown as is), `fieldErrors` go to the form
(`applyFieldErrors`), `correlationId` is for support. 401 is handled by the auth interceptor, never by pages.

## Naming

Signals `$name`, observables `name$` (rare), event handlers `onSomething`, stores `*.store.ts`, constants `*.const.ts`,
files kebab-case with a type suffix (`document-lines.ts`, `auth.service.ts`, `login.component.ts`).
