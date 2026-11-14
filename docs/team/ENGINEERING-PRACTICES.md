# Engineering practices: TDD and DDD

Binding for every role that writes code under `src/` or `web/`. The owner asked for both, fully (2026-11-14).

## TDD (test first)

1. **Red, green, refactor.** A new rule, a fix or a behaviour change starts with a failing test that names the rule. See it fail for the right reason, write the least code that passes, then clean up with the tests green.
2. **Bug fix = regression test first.** Reproduce the bug as a failing test, then fix it. The test stays.
3. **Level of the first test:** a business rule starts in `tests/Ims.Domain.Tests` or the Application tests (fast, no database). A contract, lock, transaction or migration rule starts as an integration test against real SQL Server. A user flow starts as an e2e scenario. Pick the lowest level that can fail for the rule.
4. **The commit history shows it.** Where practical the test is committed before or with the code in the same pull request; the PR description says which test failed first (one line is enough).
5. **Tests leave nothing behind.** A test that writes to the shared test database restores it in `DisposeAsync` (module switches, approval rules, return links, open orders). Order of test classes must not matter.
6. **No timing-based assertions.** Make concurrency tests deterministic with locks, barriers or events, not sleeps.
7. **Exempt (say so in the PR):** pure documentation, formatting, dependency bumps, generated screenshots.

## DDD (tactical, inside the existing Clean Architecture)

1. **Ubiquitous language.** Names in code, tests, API and docs follow the PRD glossary (stock document, posting, reversal, return, approval tier, warehouse scope). One concept, one name. A rename is a refactor with tests, not a drive-by.
2. **Rules live in `Ims.Domain`.** Handlers orchestrate (load, call the domain, save, audit); they do not decide. A business `if` in a handler or controller is a defect to move into the domain.
3. **Value objects** for concepts that have rules and no identity: money, quantity with unit, document number, percentage, date range. Immutable, validated on creation, compared by value, no primitive obsession in domain signatures. Stored in the same columns as today (EF value converters); no schema or contract change comes with a value-object slice.
4. **Aggregates** protect invariants and are changed only through their root: stock document with its lines, sales order with its lines, price list with its items, approval rule set. Other aggregates are referenced by id. One transaction changes one aggregate unless a locked process (posting, reversal) says otherwise.
5. **Domain events** for facts other parts react to (document posted, order approved, return posted, module switched). Raised by the aggregate, handled after the save; audit and notifications subscribe instead of being called from handlers.
6. **Repositories and specifications** are interfaces in Domain or Application, implemented in Infrastructure. No `DbContext` or EF type in Domain.
7. **Anemic models are debt.** New code does not add a setter-only entity with the logic elsewhere. Existing ones move over slice by slice (the DDD roadmap lists the order).

## Guards

1. Architecture tests keep the dependency direction (Domain depends on nothing). Add one for each new rule above that can be checked by reflection (no public setters on value objects, no EF types in Domain).
2. Review checks both: `deep-reviewer` asks "which test failed first?" and "is any business decision outside the domain?".
3. Behaviour is preserved: a refactor slice changes no endpoint, error code, column or stored data, and the existing suites pass unchanged.
