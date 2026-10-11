# Pull request review

Every role in `.claude/agents/` can review a pull request or a branch from its own point of view.
Reviewers are **read-only**: they report, they do not fix. The orchestrator (main session) picks the reviewers,
collects the reports and tells the author what to change.

## 1. Pick the reviewers

```bash
node scripts/pr-roles.mjs main            # roles for the current branch vs main
node scripts/pr-roles.mjs main feature/x  # explicit head
node scripts/pr-roles.mjs --files a.cs b.ts
```

The script maps changed paths to roles (table in the script). Always include `be-architect` or `fe-architect` for code,
and `qa-*` personas only when the UI changed.

## 2. What a reviewer receives

- The PR number or branch name, the base branch, and the PR description.
- The diff: `git diff <base>...<head>` (or `gh pr diff <n>` when GitHub CLI is available).
- A reviewer reads the *whole* changed files, not only the hunks, and follows references into other files when needed.

## 3. How to review (all roles)

1. Stay in your lane. Report what you are qualified to judge. A concern in another lane goes under "Cross-lane notes" naming the role.
2. Verify, do not guess. If a claim depends on behaviour, build or run it (`dotnet build`, `dotnet test`, `npm test`, run the API, open the UI). Say what you ran and the result.
3. Check the change against its source of truth: the PRD requirement ID, the task in `TASKS.md`, the acceptance criteria.
4. Prefer few, concrete findings over many vague ones. Every finding names `file:line`, what is wrong, the failing case, and a suggested fix.
5. Do not restyle. Skip taste. Style is enforced by `.editorconfig`, analyzers and lint.
6. Never post to GitHub (comments, reviews, approvals) unless the user asked for it in this session.

## 4. Severity and verdict

| Severity | Meaning | Blocks merge |
|---|---|---|
| BLOCKER | Wrong behaviour, data loss or corruption, security hole, broken build/test, rule from the PRD violated | yes |
| MAJOR | Likely bug, missing test for a rule, layering or contract violation, performance risk against a stated target | yes unless the author explains |
| MINOR | Maintainability, naming, small gaps | no |
| NIT | Optional polish | no |

Verdict: **APPROVE** (no BLOCKER/MAJOR), **REQUEST CHANGES** (any BLOCKER/MAJOR), or **COMMENT** (questions only).

## 5. Report format

Write to `docs/reviews/<pr-or-branch>/<role>.md` and return the same content (under 300 words) to the orchestrator:

```
# Review: <pr/branch> by <role>
Verdict: APPROVE | REQUEST CHANGES | COMMENT
Ran: <commands and results, or "read only">

| # | Sev | Where | Finding | Suggested fix |
|---|-----|-------|---------|---------------|

Cross-lane notes: <role>: <concern>
Questions: ...
```

Then append one entry to `docs/team/HANDOFFS.md` (see `PROTOCOL.md`).

## 6. Checklists by role

**be-architect** - Dependencies point inward (Domain has none, Application only Domain, Api uses Infrastructure only for DI). Handlers depend on interfaces. One use case per folder, one type per file. No logic in controllers. Constants in the constants files, no magic numbers or strings. Exceptions use `Messages.*`. New config keys are validated at start-up. No blocking async calls. DI registrations by convention, lifetimes sensible.

**be-system-designer** - Transactions and retries around state changes (`IUnitOfWork.ExecuteAsync`). Lock order respected (sequence, document, balances by ProductId, ledger). Idempotency and double-submit behaviour. Failure modes named, each with a test.

**be-dba** - Migration is additive and reversible, indexes match the new queries, decimal precision, rowversion where concurrency matters, no cascade that destroys history, triggers intact, seed idempotent. Query plan or timing for any new hot query against the 100k-row target.

**unit-test-engineer** - Every new domain rule or branch has a deterministic test; tests assert behaviour, not implementation; no sleeps, clocks or randomness; failing-case coverage for each guard.

**integration-test-engineer** - New endpoints have status-code, permission, scope and validation tests on real SQL Server; concurrency-sensitive paths have a parallel test.

**behavior-test-engineer** - Acceptance criteria of the linked requirement are covered by named scenarios; scenarios use requirement IDs.

**data-flow-analyst** - Trace each new or changed field UI to API to entity to ledger/report; flag places where values can diverge, double count or leak across warehouse scope.

**business-analyst** - The change satisfies the requirement and its acceptance criteria, edge cases handled, no silent scope change, glossary terms used consistently.

**product-manager** - Scope matches the phase and priority; nothing P2 sneaks into MVP; success metric impact; user-visible copy and behaviour match the intent.

**project-manager** - PR links a task, description says what/why/how tested, task status and `TASKS.md` updated, size is reviewable (split if large), risks and follow-ups recorded.

**ui-ux-designer** - Flow matches `docs/design/mvp-ux.md`; phone-first; empty, loading and error states; copy; error prevention; confirmation only where it protects data.

**fe-designer** - Uses design tokens, no hard-coded colours or spacing, contrast AA, 44px targets, dark mode, component variants as specified.

**fe-architect** - Nx tiers and boundaries respected (shared, core, features, containers), signal-first components, OnPush, no logic in templates, state and API access in the right layer, i18n keys not literals, lazy routes, tests present.

**fe-framework** - Build, lint (including boundary rules) and unit tests pass; generated API client untouched; permission-driven menus and `allowedActions` used instead of role checks in components.

**security-engineer** - Every new endpoint has an authorization policy and a warehouse-scope check; ids from the client are never trusted (BOLA); request DTOs cannot over-post; inputs are length- and range-bounded; no secrets, keys or connection strings in code or config; errors expose no internals; file uploads validate type by content, size, name and path; no new dependency with known vulnerabilities; CI changes keep least-privilege permissions; logs contain no secrets or personal data beyond what is needed.

**qa-novice-warehouse-staff / qa-power-supervisor / qa-edge-accessibility** - Run the PR's UI (built branch) as that persona and report defects with steps, expected and actual. Accessibility persona checks keyboard, labels, contrast, 360px and 1440px, wrong-role access.

## 7. GitHub side

`.github/pull_request_template.md` collects the same information from the author. `.github/CODEOWNERS` assigns the human owner;
agent roles are not GitHub users, so their reviews are posted by the orchestrator on request (`gh pr review`), never automatically.

## 8. Review gate before merge (decided by the product owner)

Passing CI is necessary, not sufficient: agents author most changes and their own tests share their blind spots. Before the orchestrator merges
any non-trivial pull request an independent reviewer reads it:

- **Applies to:** every PR over about 200 changed lines, and every PR, whatever the size, that touches money (costing, rounding, totals), permissions
  or cost visibility, authentication or security headers, a database migration, posting or reversal logic, or CI/CD and the PR guard.
  Pure documentation, dependency bumps that pass CI and one-line fixes are exempt.
- **Who:** an agent that did not write the change and has not seen the author's reasoning. It gets the diff and the repository, not a summary of intent.
  Money, permissions, migrations and security changes also get the matching specialist role from section 6.
- **What it checks:** logic errors and missed edge cases, money and rounding correctness, authorization and ownership checks, injection and logging of user input,
  data loss on partial input (for example PUT with missing fields), concurrency and idempotency, migrations (Up, Down, existing data), behaviour that
  differs between local and CI (Linux, incremental builds), and whether the tests would actually fail if the change were wrong.
- **Output:** findings ranked Blocker / Major / Minor with file, line and a concrete failing scenario; no style nitpicks that the linters cover.
- **Rule:** a Blocker or Major is fixed (or explicitly accepted by the product owner) before merge; Minor findings become TASKS.md items.
  The orchestrator records the verdict in the PR as a comment.
