// Fixture tests for the lint guard "every API call goes through a feature store action" (docs/architecture/frontend.md section 2).
// Each case lints a snippet as if it lived at a path; the guard must fire outside the allow-list and stay quiet inside it.
// Run with `node --test scripts/lint-guards.test.mjs` (npm run lint runs it after eslint).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';

const cwd = fileURLToPath(new URL('..', import.meta.url));
const eslint = new ESLint({
  cwd,
  overrideConfigFile: fileURLToPath(new URL('../eslint.config.mjs', import.meta.url))
});

const GUARDED_RULES = new Set(['no-restricted-imports', 'no-restricted-globals']);

/** The guard messages (not the other lint rules) for a snippet linted at `filePath`. */
async function guard(code, filePath) {
  const [result] = await eslint.lintText(code, { filePath: `${cwd}${filePath}` });
  return result.messages.filter(m => GUARDED_RULES.has(m.ruleId)).map(m => m.message);
}

const COMPONENT = 'libs/containers/documents/src/lib/some/some.component.ts';
const SERVICE = 'libs/core/identity/src/lib/services/some.service.ts';
const GUARD = 'libs/core/identity/src/lib/guards/some.guard.ts';
const STORE = 'libs/features/documents/src/lib/services/some.store.ts';

test('a component importing a generated client is refused and told to use a store action', async () => {
  const messages = await guard(
    `import { StockClient } from '@ims/api';\nexport const x = StockClient;\n`,
    COMPONENT
  );
  assert.equal(messages.length, 1);
  assert.match(messages[0], /store action/i);
});

test('a plain service importing a generated client is refused (the AuthService case)', async () => {
  const messages = await guard(
    `import { AuthClient, UserInfo } from '@ims/api';\nexport type U = UserInfo;\nexport const c = AuthClient;\n`,
    SERVICE
  );
  assert.equal(messages.length, 1);
});

test('a guard importing a generated client is refused', async () => {
  assert.equal(
    (
      await guard(
        `import { DocumentsClient } from '@ims/api';\nexport const c = DocumentsClient;\n`,
        GUARD
      )
    ).length,
    1
  );
});

test('a type-only import and a re-export of a client are refused too', async () => {
  assert.equal(
    (
      await guard(
        `import type { StockClient } from '@ims/api';\nexport type T = StockClient;\n`,
        COMPONENT
      )
    ).length,
    1
  );
  assert.equal((await guard(`export { StockClient } from '@ims/api';\n`, COMPONENT)).length, 1);
});

test('a namespace import of the generated api is refused', async () => {
  assert.equal(
    (
      await guard(
        `import * as api from '@ims/api';\nexport const c = api.StockClient;\n`,
        COMPONENT
      )
    ).length,
    1
  );
});

test('models, tokens and DTOs from @ims/api stay importable anywhere', async () => {
  const code = `import { API_BASE_URL, UserInfo, DocumentDto } from '@ims/api';\nexport const x = [API_BASE_URL, {} as UserInfo, {} as DocumentDto];\n`;
  assert.deepEqual(await guard(code, COMPONENT), []);
});

test('a store file may import and inject a generated client', async () => {
  const code = `import { inject } from '@angular/core';\nimport { StockClient } from '@ims/api';\nexport const c = () => inject(StockClient);\n`;
  assert.deepEqual(await guard(code, STORE), []);
});

test('a spec file may import a client', async () => {
  const code = `import { StockClient } from '@ims/api';\nexport const c = StockClient;\n`;
  assert.deepEqual(
    await guard(code, 'libs/features/documents/src/lib/services/some.store.spec.ts'),
    []
  );
  assert.deepEqual(
    await guard(code, 'libs/containers/documents/src/lib/some/some.component.spec.ts'),
    []
  );
});

test('HttpClient is refused outside stores and specs', async () => {
  const code = `import { HttpClient } from '@angular/common/http';\nexport const c = HttpClient;\n`;
  assert.equal((await guard(code, SERVICE)).length, 1);
  assert.deepEqual(await guard(code, 'libs/core/identity/src/lib/services/some.spec.ts'), []);
});

test('HttpContext, interceptors and provideHttpClient stay importable', async () => {
  const code = `import { HttpContext, HttpInterceptorFn, provideHttpClient } from '@angular/common/http';\nexport const x = [HttpContext, provideHttpClient];\nexport type F = HttpInterceptorFn;\n`;
  assert.deepEqual(await guard(code, SERVICE), []);
});

test('fetch and XMLHttpRequest are refused except in the config bootstrap and specs', async () => {
  const code = `export const a = () => fetch('/x');\nexport const b = () => new XMLHttpRequest();\n`;
  assert.equal((await guard(code, COMPONENT)).length, 2);
  assert.deepEqual(await guard(code, 'libs/core/http/src/lib/config/app-config.ts'), []);
  assert.deepEqual(await guard(code, 'libs/core/http/src/lib/some.spec.ts'), []);
});

test('the allow-list also holds when a lib lints with its own eslint.config.mjs (npm run lint, nx lint)', async () => {
  const libCwd = `${cwd}libs/core/http/`;
  const lib = new ESLint({ cwd: libCwd });
  const lint = async (code, path) => {
    const [result] = await lib.lintText(code, { filePath: `${libCwd}${path}` });
    return result.messages.filter(m => GUARDED_RULES.has(m.ruleId));
  };
  const code = `export const a = () => fetch('/x');
`;
  assert.deepEqual(await lint(code, 'src/lib/config/app-config.ts'), []);
  assert.equal((await lint(code, 'src/lib/other.ts')).length, 1);
});

test('a member or parameter called fetch is not the global', async () => {
  const code = `export class A { constructor(private readonly fetch: () => void) {} run() { this.fetch(); } }\n`;
  assert.deepEqual(await guard(code, 'libs/shared/utils/src/lib/operations/some.ts'), []);
});

test('the animations ban still applies in every file, stores included', async () => {
  const code = `import { trigger } from '@angular/animations';\nexport const t = trigger;\n`;
  assert.equal((await guard(code, COMPONENT)).length, 1);
  assert.equal((await guard(code, STORE)).length, 1);
});
