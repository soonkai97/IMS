// Shared rules for the git guards (local hooks and the PR guard workflow). Plain Node, no dependencies.
import { execFileSync } from 'node:child_process';

export const git = (...args) => execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
export const gitOrNull = (...args) => { try { return git(...args); } catch { return null; } };

export const PROTECTED_BRANCHES = ['main'];
export const BRANCH_PATTERN = /^(feature|fix|chore|docs|refactor|test|ci|perf|hotfix|release|dependabot)\/[A-Za-z0-9._\-/]+$/;
export const COMMIT_TYPES = ['feat', 'fix', 'docs', 'style', 'refactor', 'perf', 'test', 'build', 'ci', 'chore', 'revert'];
export const CONVENTIONAL = new RegExp(`^(${COMMIT_TYPES.join('|')})(\\([a-z0-9._/-]+\\))?!?: \\S.{0,70}$`);
export const MAX_SUBJECT = 72;
export const MAX_FILE_BYTES = 2 * 1024 * 1024;
export const MAX_PR_CHANGED_LINES = 1500;
export const LARGE_CHANGE_LABEL = 'large-change';

/** Messages git generates itself are not rewritten by people. */
export const isGeneratedMessage = (subject) => /^(Merge |Revert "|fixup! |squash! |amend! )/.test(subject);

/** The project rule: no AI co-author trailers in commit messages. Human co-authors are fine. */
export const AI_COAUTHOR = /^co-authored-by:.*(claude|anthropic|noreply@anthropic\.com)/im;

/** GitHub's squash-merge appends " (#123)" to the PR title; the author does not control it. */
const SQUASH_SUFFIX = / \(#\d+\)$/;

/**
 * `fromTargetHistory`: the commit was squash-merged on GitHub, so one trailing " (#123)" does not count towards the
 * subject limit. Only for commits that arrive through a PR range; the PR title and local commits are checked in full.
 */
export function checkCommitMessage(message, { fromTargetHistory = false } = {}) {
  const problems = [];
  const lines = message.replace(/\r/g, '').split('\n');
  const trimmed = (lines[0] ?? '').trim();
  const subject = fromTargetHistory ? trimmed.replace(SQUASH_SUFFIX, '') : trimmed;
  if (isGeneratedMessage(subject)) return problems;
  if (!CONVENTIONAL.test(subject))
    problems.push(`Subject must follow Conventional Commits: "type(scope): summary" with type one of ${COMMIT_TYPES.join(', ')} (max ${MAX_SUBJECT} chars). Got: "${subject}"`);
  if (subject.length > MAX_SUBJECT) problems.push(`Subject is ${subject.length} characters; the limit is ${MAX_SUBJECT}.`);
  if (/[.]$/.test(subject)) problems.push('Subject must not end with a full stop.');
  if (lines.length > 1 && lines[1].trim() !== '') problems.push('Leave a blank line between the subject and the body.');
  if (AI_COAUTHOR.test(message)) problems.push('Remove the AI "Co-Authored-By" trailer: commits in this project carry no such trailer.');
  return problems;
}

export function checkBranchName(name) {
  if (PROTECTED_BRANCHES.includes(name)) return [];
  return BRANCH_PATTERN.test(name) ? [] : [`Branch "${name}" must be named <type>/<short-description>, type one of feature, fix, chore, docs, refactor, test, ci, perf, hotfix, release. Example: feature/stock-low-alert`];
}

const FORBIDDEN_PATHS = [
  /(^|\/)\.env(\.|$)(?!example)/, /\.(pfx|p12|pem|key|jks|keystore)$/i, /\.(bak|trn|mdf|ldf)$/i,
  /(^|\/)appsettings\.Production\.json$/i, /(^|\/)(bin|obj|node_modules|storage|logs)\//, /(^|\/)artifacts\//, /\.local\.json$/i,
];
export const forbiddenPath = (file) => FORBIDDEN_PATHS.some((re) => re.test(file.replaceAll('\\', '/')));

const SECRET_PATTERNS = [
  ['private key block', /-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----/],
  ['AWS access key id', /\bAKIA[0-9A-Z]{16}\b/],
  ['GitHub token', /\bgh[pousr]_[A-Za-z0-9]{36,}\b/],
  ['Slack token', /\bxox[baprs]-[A-Za-z0-9-]{10,}\b/],
  ['JWT', /\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\b/],
  ['connection string with a password', /\b(?:Password|Pwd)\s*=\s*(?!\$\{|<|\{|%|;|["']?\s*$)[^;"'\s]{6,}/i],
];
/** Files that may legitimately contain throw-away values (documented in the file itself). */
const SECRET_ALLOW = [/(^|\/)appsettings\.Development\.json$/, /^docs\//, /^\.github\/workflows\/ci\.yml$/, /^scripts\/guard\//, /^tests\//, /\.md$/];
export function findSecrets(file, text) {
  const f = file.replaceAll('\\', '/');
  if (SECRET_ALLOW.some((re) => re.test(f))) return [];
  const found = [];
  text.split('\n').forEach((line, i) => {
    if (line.includes('guard:allow-secret')) return;
    for (const [name, re] of SECRET_PATTERNS) if (re.test(line)) found.push(`${f}:${i + 1}  looks like a ${name}`);
  });
  return found;
}

export function report(title, problems) {
  if (problems.length === 0) return 0;
  console.error(`\n${title}`);
  for (const p of problems) console.error(`  - ${p}`);
  console.error('');
  return 1;
}
