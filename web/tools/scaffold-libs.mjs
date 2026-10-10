// Generates the per-library boilerplate from tools/libs.mjs and refreshes tsconfig.base.json path aliases.
// Idempotent: existing src/index.ts and source files are never touched.
//   node tools/scaffold-libs.mjs
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { LIBS } from './libs.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const UP = '../../../';

function write(rel, content, overwrite = true) {
  const file = join(root, rel);
  if (!overwrite && existsSync(file)) {
    return;
  }
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, content.endsWith('\n') ? content : `${content}\n`);
}

const json = value => `${JSON.stringify(value, null, 2)}\n`;

const jestConfig = l => `/* eslint-disable */
export default {
  displayName: '${l.project}',
  preset: '${UP}jest.preset.js',
  setupFilesAfterEnv: ['<rootDir>/src/test-setup.ts'],
  coverageDirectory: '${UP}coverage/${l.root}',
  transform: {
    '^.+\\\\.(ts|mjs|js|html)$': [
      'jest-preset-angular',
      { tsconfig: '<rootDir>/tsconfig.spec.json', stringifyContentPathRegex: '\\\\.(html|svg)$' }
    ]
  },
  transformIgnorePatterns: ['node_modules/(?!.*\\\\.mjs$)'],
  snapshotSerializers: [
    'jest-preset-angular/build/serializers/no-ng-attributes',
    'jest-preset-angular/build/serializers/ng-snapshot',
    'jest-preset-angular/build/serializers/html-comment'
  ]
};
`;

const testSetup = `import { setupZonelessTestEnv } from 'jest-preset-angular/setup-env/zoneless';

setupZonelessTestEnv({ errorOnUnknownElements: true, errorOnUnknownProperties: true });
`;

for (const l of LIBS) {
  write(
    `${l.root}/project.json`,
    json({
      name: l.project,
      $schema: `${UP}node_modules/nx/schemas/project-schema.json`,
      projectType: 'library',
      sourceRoot: `${l.root}/src`,
      prefix: 'app',
      tags: [`scope:${l.scope}`],
      implicitDependencies: ['shared-styles', 'shared-assets'].filter(p => p !== l.project),
      targets: {
        test: {
          executor: '@nx/jest:jest',
          outputs: [`{workspaceRoot}/coverage/${l.root}`],
          options: { jestConfig: `${l.root}/jest.config.ts`, passWithNoTests: true }
        },
        lint: {
          executor: '@nx/eslint:lint',
          options: { lintFilePatterns: [`${l.root}/src/**/*.ts`, `${l.root}/src/**/*.html`] }
        }
      }
    })
  );
  write(
    `${l.root}/tsconfig.json`,
    json({
      extends: `${UP}tsconfig.base.json`,
      files: [],
      include: [],
      references: [{ path: './tsconfig.lib.json' }, { path: './tsconfig.spec.json' }],
      compilerOptions: {
        forceConsistentCasingInFileNames: true,
        strict: true,
        noImplicitOverride: true,
        noPropertyAccessFromIndexSignature: true,
        noImplicitReturns: true,
        noFallthroughCasesInSwitch: true,
        moduleResolution: 'bundler'
      },
      angularCompilerOptions: {
        strictInjectionParameters: true,
        strictInputAccessModifiers: true,
        strictTemplates: true
      }
    })
  );
  write(
    `${l.root}/tsconfig.lib.json`,
    json({
      extends: './tsconfig.json',
      compilerOptions: {
        outDir: `${UP}dist/out-tsc`,
        declaration: true,
        declarationMap: true,
        inlineSources: true,
        types: []
      },
      exclude: ['src/test-setup.ts', '**/*.spec.ts', '**/*.test.ts', 'jest.config.ts'],
      include: ['**/*.ts']
    })
  );
  write(
    `${l.root}/tsconfig.spec.json`,
    json({
      extends: './tsconfig.json',
      compilerOptions: {
        outDir: `${UP}dist/out-tsc`,
        module: 'preserve',
        moduleResolution: 'bundler',
        types: ['jest', 'node']
      },
      files: ['src/test-setup.ts'],
      include: ['**/*.spec.ts', '**/*.test.ts', '**/*.d.ts', 'jest.config.ts']
    })
  );
  write(`${l.root}/jest.config.ts`, jestConfig(l));
  write(`${l.root}/src/test-setup.ts`, testSetup);
  write(
    `${l.root}/eslint.config.mjs`,
    `import baseConfig from '${UP}eslint.config.mjs';\n\nexport default [...baseConfig];\n`
  );
  write(`${l.root}/src/index.ts`, 'export {};\n', false);
}

const basePath = join(root, 'tsconfig.base.json');
const base = JSON.parse(readFileSync(basePath, 'utf8'));
base.compilerOptions.paths = Object.fromEntries(
  [...LIBS]
    .sort((a, b) => a.alias.localeCompare(b.alias))
    .map(l => [l.alias, [`${l.root}/src/index.ts`]])
);
writeFileSync(basePath, json(base));
console.log(`Scaffolded ${LIBS.length} libraries.`);
