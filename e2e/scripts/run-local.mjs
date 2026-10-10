// Local convenience: reads the dev-only demo passwords from the API's appsettings.Development.json (Seed section) into the
// IMS_E2E_PASSWORD_<ROLE> variables for this one run, then starts Playwright. The values are never printed or written.
// Variables you already set win. Usage: npm run test:local -- [playwright arguments]
import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const settingsFile = resolve(
  here,
  process.env.IMS_E2E_SEED_FILE ?? '../../src/Ims.Api/appsettings.Development.json'
);
const seed = JSON.parse(readFileSync(settingsFile, 'utf8')).Seed ?? {};

const env = { ...process.env };
for (const [role, key] of [
  ['ADMIN', 'AdminPassword'],
  ['OWNER', 'DemoPassword'],
  ['SUPERVISOR', 'DemoPassword'],
  ['STAFF', 'DemoPassword'],
  ['CLERK', 'DemoPassword']
]) {
  env[`IMS_E2E_PASSWORD_${role}`] ??= seed[key];
}

// Runs the Playwright CLI with the current Node (no shell, so arguments such as -g "a|b" arrive untouched).
const cli = createRequire(import.meta.url).resolve('@playwright/test/cli');
const child = spawn(process.execPath, [cli, 'test', ...process.argv.slice(2)], {
  stdio: 'inherit',
  env,
  cwd: resolve(here, '..')
});
child.on('exit', code => process.exit(code ?? 1));
