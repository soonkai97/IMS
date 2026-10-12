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
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@angular/animations*', '@angular/platform-browser/animations*'],
              message:
                'Use CSS transitions, animate.enter and animate.leave (docs/conventions/scss-design.md).'
            }
          ]
        }
      ],
      '@angular-eslint/no-input-rename': 'off',
      '@angular-eslint/no-output-rename': 'off',
      '@angular-eslint/prefer-on-push-component-change-detection': 'error'
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
    files: ['**/*.spec.ts', '**/test-setup.ts'],
    rules: {
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/consistent-type-assertions': 'off'
    }
  },
  {
    files: ['**/*.html'],
    rules: {
      '@angular-eslint/template/prefer-control-flow': 'error'
    }
  },
  prettier
];
