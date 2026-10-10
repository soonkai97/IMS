#!/usr/bin/env node
// Suggests which review roles (see docs/team/PR-REVIEW.md) should look at a change.
//   node scripts/pr-roles.mjs <base> [head]     roles for `git diff base...head` (head defaults to HEAD)
//   node scripts/pr-roles.mjs --files a b c      roles for an explicit file list
import { execFileSync } from 'node:child_process';

/** Ordered rules: every matching rule adds its roles. Paths use forward slashes. */
const RULES = [
  { match: /^src\/[^/]+\.Domain\//, roles: ['be-architect', 'unit-test-engineer', 'business-analyst'] },
  { match: /^src\/[^/]+\.Application\//, roles: ['be-architect', 'unit-test-engineer', 'integration-test-engineer'] },
  { match: /^src\/[^/]+\.Application\/.*(Post|Reverse|Number|Balance|Unit|Transaction)/i, roles: ['be-system-designer', 'data-flow-analyst'] },
  { match: /^src\/[^/]+\.Infrastructure\/Persistence\/Migrations\//, roles: ['be-dba', 'be-system-designer'] },
  { match: /^src\/[^/]+\.Infrastructure\//, roles: ['be-architect', 'be-dba', 'integration-test-engineer'] },
  { match: /^src\/[^/]+\.Api\//, roles: ['be-architect', 'integration-test-engineer'] },
  { match: /^web\/(libs|apps)\/.*\/(styles|assets)\//, roles: ['fe-designer'] },
  { match: /^web\//, roles: ['fe-architect', 'fe-framework'] },
  { match: /^web\/.*\.(html|scss|css)$/, roles: ['ui-ux-designer', 'qa-novice-warehouse-staff', 'qa-edge-accessibility'] },
  { match: /^web\/.*(approval|document|settings|users|audit)/i, roles: ['qa-power-supervisor'] },
  { match: /^tests\/.*(Domain|Architecture)/, roles: ['unit-test-engineer'] },
  { match: /^tests\/.*Api\.Tests/, roles: ['integration-test-engineer'] },
  { match: /^(tests\/features|web\/e2e|tests\/smoke)/, roles: ['behavior-test-engineer'] },
  { match: /^docs\/(ba|prd)/, roles: ['business-analyst', 'product-manager'] },
  { match: /^docs\/design\//, roles: ['ui-ux-designer', 'fe-designer'] },
  { match: /^docs\/(pm|team)\/|^TASKS\.md$/, roles: ['project-manager'] },
  { match: /^(\.github\/|Dockerfile|docker-compose|scripts\/)/, roles: ['be-architect', 'project-manager', 'security-engineer'] },
  { match: /(Auth|Identity|Security|Users|Attachment|Upload|Import|Program\.cs|Composition|ExceptionHandler|appsettings|\.env)/i, roles: ['security-engineer'] },
  { match: /^web\/.*(auth|identity|interceptor|guard|upload|token)/i, roles: ['security-engineer'] },
];

const args = process.argv.slice(2);
let files;
if (args[0] === '--files') {
  files = args.slice(1);
} else {
  const base = args[0] ?? 'main';
  const head = args[1] ?? 'HEAD';
  try {
    files = execFileSync('git', ['diff', '--name-only', `${base}...${head}`], { encoding: 'utf8' }).split('\n').filter(Boolean);
  } catch (e) {
    console.error(`git diff failed: ${e.message}`);
    process.exit(2);
  }
}

const roles = new Map();
for (const f of files.map(x => x.replaceAll('\\', '/'))) {
  for (const rule of RULES) {
    if (!rule.match.test(f)) continue;
    for (const role of rule.roles) {
      if (!roles.has(role)) roles.set(role, new Set());
      roles.get(role).add(f);
    }
  }
}

// Every code change gets a scope check from the product role when it touches more than a handful of files.
if (files.length > 15) roles.set('product-manager', new Set(['(large change)']));
if (files.length > 0) roles.set('project-manager', roles.get('project-manager') ?? new Set(['(PR hygiene)']));

if (roles.size === 0) {
  console.log('No review roles matched. Changed files:', files.length);
  process.exit(0);
}
console.log(`Changed files: ${files.length}\nSuggested reviewers:`);
for (const [role, set] of [...roles].sort((a, b) => a[0].localeCompare(b[0]))) {
  const sample = [...set].slice(0, 2).join(', ');
  console.log(`  ${role.padEnd(30)} ${set.size} file(s)  e.g. ${sample}`);
}
