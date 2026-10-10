## What and why
<!-- One or two sentences. Link the task (TASKS.md id) and the requirement (PRD id, e.g. IN-02). -->

## How it was tested
<!-- Commands run and results: dotnet test, node tests/smoke/smoke.mjs, npm test, manual steps. -->

## Checklist
- [ ] Builds without warnings (`dotnet build src/Ims.Api -c Release -warnaserror`, `npm run build:prod`)
- [ ] New or changed behaviour has tests (unit for domain rules, integration for endpoints)
- [ ] No magic numbers or strings; constants and `Messages.*` used
- [ ] Layering respected (Domain <- Application <- Api/Infrastructure; web tiers)
- [ ] Migration reviewed (additive, reversible) if the schema changed
- [ ] UI: loading, empty and error states; 360px and desktop; permissions via `allowedActions`/permission service
- [ ] Docs updated (README, `docs/`, `TASKS.md`)

## Reviewers
<!-- Run `node scripts/pr-roles.mjs main` and list the roles that should review (see docs/team/PR-REVIEW.md). -->
