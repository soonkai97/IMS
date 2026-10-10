#!/usr/bin/env node
// pre-push hook: no force-push, no direct push to a protected branch, and a fast test run before anything leaves the machine.
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
import { gitOrNull, report, PROTECTED_BRANCHES } from './lib.mjs';

const ZERO = /^0+$/;
const problems = [];
// git passes: <local ref> <local sha> <remote ref> <remote sha> per line on stdin.
const lines = fs.readFileSync(0, 'utf8').split('\n').filter(Boolean);
for (const line of lines) {
  const [, localSha, remoteRef, remoteSha] = line.split(' ');
  const branch = remoteRef.replace('refs/heads/', '');
  if (ZERO.test(localSha)) {
    if (PROTECTED_BRANCHES.includes(branch)) problems.push(`Deleting the protected branch "${branch}" is not allowed.`);
    continue;
  }
  const isNewRemoteBranch = ZERO.test(remoteSha);
  if (!isNewRemoteBranch && gitOrNull('merge-base', '--is-ancestor', remoteSha, localSha) === null)
    problems.push(`${branch}: this push would rewrite history (not a fast-forward). Force-pushing is blocked; rebase onto the remote and push again.`);
  // Protected branches are changed through pull requests. The very first push (the branch does not exist remotely yet) is allowed.
  if (PROTECTED_BRANCHES.includes(branch) && !isNewRemoteBranch && process.env.ALLOW_PUSH_MAIN !== '1')
    problems.push(`${branch} is protected: push a branch and open a pull request. (Override for an emergency: ALLOW_PUSH_MAIN=1.)`);
}
if (report('Push blocked by the pre-push guard:', problems)) process.exit(1);

// Fast checks that need no database: architecture rules and domain unit tests (about 10 seconds).
if (process.env.SKIP_PREPUSH_TESTS !== '1' && lines.some((l) => !ZERO.test(l.split(' ')[1]))) {
  for (const project of ['tests/Ims.Architecture.Tests', 'tests/Ims.Domain.Tests']) {
    if (!fs.existsSync(project)) continue;
    console.log(`pre-push: dotnet test ${project}`);
    const r = spawnSync('dotnet', ['test', project, '--nologo', '-v', 'q'], { stdio: 'inherit', shell: process.platform === 'win32' });
    if (r.status !== 0) { console.error(`\nPush blocked: ${project} failed. Fix it, or SKIP_PREPUSH_TESTS=1 to push anyway (CI will still run them).`); process.exit(1); }
  }
}
