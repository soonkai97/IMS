import nx from '@nx/eslint-plugin';
import prettier from 'eslint-config-prettier';
import { SCOPE_RULES } from './tools/libs.mjs';

// Layer rules live in tools/libs.mjs (SCOPE_RULES) and are enforced here: a violation fails `npm run lint`.
const depConstraints = Object.entries(SCOPE_RULES).map(([sourceTag, onlyDependOnLibsWithTags]) => ({
  sourceTag,
  onlyDependOnLibsWithTags
}));

// Role names are API data in these files only: the definitions, the user form's role select, the approval-rule form
// and `seesAllWarehouses` (until the API sends it). Specs may name a role to sign in as.
const ROLE_NAME_ALLOWED_FILES = [
  '**/*.spec.ts',
  '**/*.messages.ts', // display text and labels, not logic
  '**/permissions.const.ts',
  '**/permission.service.ts',
  '**/users.component.ts',
  '**/settings.component.ts'
];

// Every call to the API goes through a feature store action (`QueryLoader` for reads, `ApiCommand` for writes; docs/architecture/frontend.md
// section 2). So a generated `*Client` (and Angular's `HttpClient`) may only be imported by a store or a spec (the generated lib
// itself is ignored above and imports no client). This list is the whole allow-list: a new entry is a visible diff and needs a reason.
// Patterns start with `**/`: each lib lints with its own eslint.config.mjs, so a path from the repo's web/ folder would not match.
const API_CALL_ALLOWED_FILES = [
  '**/*.store.ts', // the only place that injects a generated client
  '**/*.spec.ts' // specs provide fakes for the clients
];

// The one `fetch` in the app: the bootstrap config.json read, which runs before the app (and its HttpClient) exists.
const RAW_HTTP_ALLOWED_FILES = ['**/*.spec.ts', '**/config/app-config.ts'];

const ANIMATION_IMPORT_PATTERN = {
  group: ['@angular/animations*', '@angular/platform-browser/animations*'],
  message: 'Use CSS transitions, animate.enter and animate.leave (docs/conventions/scss-design.md).'
};

const API_CALL_IMPORT_RESTRICTIONS = {
  paths: [
    {
      name: '@angular/common/http',
      importNames: ['HttpClient'],
      message:
        'Do not call the API from here: put the call in a feature store action (QueryLoader or ApiCommand in a *.store.ts).'
    }
  ],
  patterns: [
    ANIMATION_IMPORT_PATTERN,
    {
      group: ['@ims/api'],
      importNamePattern: '^[A-Z][A-Za-z0-9]*Client$',
      allowTypeImports: false,
      message:
        'Do not call the API from here: put the call in a feature store action (QueryLoader or ApiCommand in a *.store.ts) and use the store. Only *.store.ts may import a generated *Client.'
    }
  ]
};

// Decorator inputs, outputs, queries and host members: use input(), output(), model(), viewChild() and the host metadata.
const LEGACY_DECORATOR_SELECTORS = [
  {
    selector:
      'Decorator[expression.callee.name=/^(Input|Output|ViewChild|ViewChildren|ContentChild|ContentChildren|HostBinding|HostListener)$/]',
    message:
      'Use the signal API: input(), output(), model(), viewChild(), contentChild() or the host metadata.'
  }
];

export default [
  ...nx.configs['flat/base'],
  ...nx.configs['flat/typescript'],
  ...nx.configs['flat/javascript'],
  ...nx.configs['flat/angular'],
  ...nx.configs['flat/angular-template'],
  {
    ignores: [
      '**/dist',
      '**/coverage',
      '**/.nx',
      '**/node_modules',
      '**/*.generated.ts',
      'libs/shared/api/src/lib/api-client.ts'
    ]
  },
  {
    files: ['**/*.ts', '**/*.js', '**/*.mjs'],
    rules: {
      '@nx/enforce-module-boundaries': [
        'error',
        {
          enforceBuildableLibDependency: false,
          allow: ['^.*/eslint(\\.base)?\\.config\\.[cm]?[jt]s$', '^.*/tools/libs\\.mjs$'],
          depConstraints
        }
      ]
    }
  },
  {
    files: ['**/*.ts'],
    rules: {
      // Conventions: see docs/conventions/coding-practices.md
      curly: 'error',
      'no-nested-ternary': 'error',
      'prefer-const': 'error',
      'no-var': 'error',
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',
      '@typescript-eslint/no-unused-vars': [
        'error',
        { varsIgnorePattern: '^_', argsIgnorePattern: '^_' }
      ],
      '@typescript-eslint/consistent-type-assertions': [
        'error',
        { assertionStyle: 'as', objectLiteralTypeAssertions: 'never' }
      ],
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' }
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' }
      ],
      // @angular/animations is deprecated in Angular 22 and not a dependency (it is only a test-time peer of Spectator).
      // Stores, specs and the generated lib are outside the API-call block below, so the animations ban is all they get here.
      'no-restricted-imports': ['error', { patterns: [ANIMATION_IMPORT_PATTERN] }],
      '@angular-eslint/no-input-rename': 'off',
      '@angular-eslint/no-output-rename': 'off',
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      // Signal-era guards: every one has zero hits today and keeps it so. None needs type information (the type-aware ones,
      // no-uncalled-signals, prefer-signals and prefer-signal-model, are left out on purpose: lint stays syntax-only).
      '@angular-eslint/computed-must-return': 'error',
      '@angular-eslint/prefer-output-readonly': 'error',
      '@angular-eslint/no-implicit-take-until-destroyed': 'error',
      '@angular-eslint/no-attribute-decorator': 'error',
      '@angular-eslint/no-lifecycle-call': 'error',
      '@angular-eslint/no-duplicates-in-metadata-arrays': 'error',
      eqeqeq: 'error',
      'no-console': ['error', { allow: ['warn', 'error'] }],
      // The decorator members are gone (input(), output(), model(), viewChild(), host: {}); this keeps them gone without type information.
      'no-restricted-syntax': ['error', ...LEGACY_DECORATOR_SELECTORS]
    }
  },
  {
    // Decide by permission, never by role name (docs/design/2026-refresh-plan.md D5): use `PermissionService.$profile()`.
    // The files below still hold role names the API itself speaks (listed so a new one is a visible diff);
    // RP01/RP05/RP11 remove them.
    files: ['**/*.ts'],
    ignores: ROLE_NAME_ALLOWED_FILES,
    rules: {
      'no-restricted-syntax': [
        'error',
        ...LEGACY_DECORATOR_SELECTORS,
        {
          selector: 'Identifier[name=/^(ROLE|ALL_ROLES|ALL_WAREHOUSE_ROLES)$/]',
          message:
            'Do not branch on a role name. Ask PermissionService.$profile() (permission codes).'
        },
        {
          selector: 'Literal[value=/^(Admin|Owner|Supervisor|WarehouseStaff|Clerk)$/]',
          message:
            'Do not branch on a role name. Ask PermissionService.$profile() (permission codes).'
        }
      ]
    }
  },
  {
    // No direct API call from a component, container, guard, resolver, view-service or plain service: only a store action calls the
    // API (docs/architecture/frontend.md section 2). Proven by scripts/lint-guards.test.mjs.
    files: ['**/*.ts'],
    ignores: API_CALL_ALLOWED_FILES,
    rules: {
      'no-restricted-imports': ['error', API_CALL_IMPORT_RESTRICTIONS]
    }
  },
  {
    files: ['**/*.ts'],
    ignores: RAW_HTTP_ALLOWED_FILES,
    rules: {
      'no-restricted-globals': [
        'error',
        {
          name: 'fetch',
          message:
            'Do not call the API with fetch: put the call in a feature store action (QueryLoader or ApiCommand in a *.store.ts).'
        },
        {
          name: 'XMLHttpRequest',
          message:
            'Do not call the API with XMLHttpRequest: put the call in a feature store action (QueryLoader or ApiCommand in a *.store.ts).'
        }
      ]
    }
  },
  {
    files: ['**/*.spec.ts', '**/test-setup.ts'],
    rules: {
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/consistent-type-assertions': 'off'
    }
  },
  {
    files: ['**/*.html'],
    rules: {
      '@angular-eslint/template/prefer-control-flow': 'error',
      '@angular-eslint/template/prefer-contextual-for-variables': 'error',
      '@angular-eslint/template/no-empty-control-flow': 'error',
      '@angular-eslint/template/prefer-class-binding': 'error',
      '@angular-eslint/template/prefer-style-binding': 'error',
      // Every raw <button> already names its type (a bare one inside a form would submit it), and no @for guards its list with length === 0.
      '@angular-eslint/template/button-has-type': 'error',
      '@angular-eslint/template/prefer-at-empty': 'error'
    }
  },
  prettier
];
