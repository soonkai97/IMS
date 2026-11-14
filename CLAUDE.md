# IMS: instructions for Claude and the agents

- Read `docs/team/PROTOCOL.md` (roles, lanes, handoffs, definition of done) and `docs/team/GIT-WORKFLOW.md` before changing anything.
- Work **test first** (TDD) and model the business in the **domain** (DDD): `docs/team/ENGINEERING-PRACTICES.md` is binding.
- Pull requests are small and use Conventional Commits; no Co-Authored-By trailer (the PR guard rejects it).
- The repository is public: the PRD stays in `docs/private/` (gitignored). Never copy PRD text into tracked files, commits or PRs.
- Never create scratch files, folders, worktrees or databases on drive C: beyond the harness scratchpad; clean up what a test run creates.
