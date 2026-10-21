// Single source of truth for the library layout. `node tools/scaffold-libs.mjs` generates/refreshes the boilerplate
// (project.json, tsconfig*, jest, eslint, path aliases) for every entry so the library-creation checklist
// (docs/library-creation.md) is applied the same way everywhere.
//
// scope:  shared | core | features | containers | containers-shared
// dir:    first folder under libs/ (shared | core | features | containers)
// alias:  import path used by other projects (never relative paths across libraries)

const aliasOf = (dir, name) => (dir === 'shared' ? `@ims/${name}` : `@ims/${dir}/${name}`);

const lib = (dir, name, scope = dir) => ({
  dir,
  name,
  scope,
  alias: aliasOf(dir, name),
  project: `${dir}-${name}`,
  root: `libs/${dir}/${name}`
});

export const LIBS = [
  // shared: may depend only on shared
  lib('shared', 'api'),
  lib('shared', 'assets'),
  lib('shared', 'models'),
  lib('shared', 'styles'),
  lib('shared', 'ui'),
  lib('shared', 'utils'),
  // core: core + shared
  lib('core', 'http'),
  lib('core', 'i18n'),
  lib('core', 'identity'),
  lib('core', 'navigation'),
  lib('core', 'notification'),
  lib('core', 'pwa'),
  lib('core', 'settings'),
  lib('core', 'theme'),
  // features: features + core + shared (business logic and data access, no pages)
  lib('features', 'administration'),
  lib('features', 'approvals'),
  lib('features', 'audit'),
  lib('features', 'catalog'),
  lib('features', 'documents'),
  lib('features', 'purchasing'),
  lib('features', 'reports'),
  lib('features', 'stock'),
  lib('features', 'warehousing'),
  // containers: routed pages. *-shared containers hold UI that several containers reuse.
  lib('containers', 'administration'),
  lib('containers', 'approvals'),
  lib('containers', 'audit'),
  lib('containers', 'authentication'),
  lib('containers', 'catalog'),
  lib('containers', 'catalog-shared', 'containers-shared'),
  lib('containers', 'documents'),
  lib('containers', 'home'),
  lib('containers', 'purchasing'),
  lib('containers', 'reports'),
  lib('containers', 'stock'),
  lib('containers', 'system'),
  lib('containers', 'warehousing')
];

// Which scopes a scope may import (enforced by @nx/enforce-module-boundaries, see eslint.config.mjs).
export const SCOPE_RULES = {
  'scope:shared': ['scope:shared'],
  'scope:core': ['scope:core', 'scope:shared'],
  'scope:features': ['scope:features', 'scope:core', 'scope:shared'],
  'scope:containers-shared': [
    'scope:containers-shared',
    'scope:features',
    'scope:core',
    'scope:shared'
  ],
  'scope:containers': ['scope:containers-shared', 'scope:features', 'scope:core', 'scope:shared'],
  'scope:app': [
    'scope:containers',
    'scope:containers-shared',
    'scope:features',
    'scope:core',
    'scope:shared'
  ]
};
