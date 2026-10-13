# Git workflow and guards

**One-time setup per clone (30 seconds):** `node scripts/setup-git-hooks.mjs`

## The flow

1. `git switch -c feature/<short-name>` (types: feature, fix, chore, docs, refactor, test, ci, perf, hotfix, release)
2. Commit small, with Conventional Commits: `feat(stock): add low-stock alert`
3. `git push` (first push sets upstream automatically), open a PR to `main` using the PR template.
4. CI and the PR guard must be green; resolve every review conversation; **squash-merge**. The PR title becomes the commit on `main`, so it follows the same format.

## What is enforced, and where

| Rule | Local hook | CI (`pr-guard.yml`) | GitHub ruleset (`.github/rulesets/main.json`) |
|---|---|---|---|
| Conventional commit subject (<= 72 chars) | commit-msg | PR title and every commit | squash-only merge |
| No AI `Co-Authored-By` trailer | commit-msg | yes | |
| Branch name `<type>/<name>` | pre-commit | yes | |
| No commits / pushes straight to `main` | pre-commit, pre-push | | PR required |
| No force-push, no branch deletion of `main` | pre-push | | yes |
| No secrets, keys, backups, build output, files over 2 MB | pre-commit | yes | |
| Fast tests before push (architecture + domain) | pre-push | full suite | required checks |
| Linear history | `merge.ff=false`, `pull.ff=only` | | yes |
| Web files formatted and linted | pre-commit (lint-staged) | `web` job | required check |

The rules are written once in `scripts/guard/lib.mjs`; hooks and CI import the same file.

Exemption: GitHub's squash-merge appends ` (#123)` to the PR title, which the author does not control. For commits that come into a PR range (for example an integration branch holding squash-merged work) that one trailing suffix is not counted towards the 72 characters. The PR title and local commits are always checked in full. Tests: `node --test scripts/guard`.

## Overrides (emergencies only; CI and the ruleset still apply)

- `ALLOW_MAIN_COMMIT=1`, `ALLOW_PUSH_MAIN=1`, `SKIP_PREPUSH_TESTS=1`
- A test value that looks like a secret: add `guard:allow-secret` on that line.

## Turn the ruleset on (once, after the first push)

GitHub -> Settings -> Rules -> Rulesets -> New ruleset -> **Import a ruleset** -> pick `.github/rulesets/main.json`.
Required checks only appear after the workflows have run once on GitHub. Private repos need GitHub Pro/Team for rulesets.
Also enable: Settings -> Code security -> Secret scanning + push protection, Dependabot alerts.
