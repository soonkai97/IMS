# Using this repository as a base project

The solution is built so a new client project can start from it: a reusable **base** (architecture, cross-cutting code, auth,
CI/CD) plus a **sample domain** (inventory) that you delete or replace.

## 1. Start a new project

```bash
git clone <this repo> acme && cd acme && rm -rf .git          # fresh copy, no history
node scripts/rename-project.mjs --name Acme                   # dry run: lists what changes
node scripts/rename-project.mjs --name Acme --apply           # Ims -> Acme, IMS -> ACME, ims -> acme (contents and file names)
dotnet build                                                  # must pass
```

The product name is one token (`Ims` / `IMS` / `ims`) used only for solution, project and namespace names, database names,
Docker image names, config keys and environment-variable prefixes. Generic code never hard-codes it:
titles, log file names and log properties are derived from the assembly name at runtime (`AppIdentity`).

Then:
1. Delete `src/Acme.Infrastructure/Persistence/Migrations`, remove the sample entities (step 2), create a new `Initial` migration.
2. Set real values: `appsettings*.json` (issuer, audience, CORS origins), the `.env` file (see `.env.example`), GitHub environment secrets.
3. Replace the PRD (`docs/prd-*.txt`), `TASKS.md` and the design docs with the new project's.
4. Adjust roles and permissions (`Domain/Identity/Services/Roles.cs`, `Permissions.cs`) to the client's matrix.

## 2. What is base and what is sample

| Area | Base (keep) | Sample inventory domain (replace) |
|---|---|---|
| Domain | `Common/` (`Entity`, rules pattern, constants structure), `Identity/` (User, RefreshToken, Roles, Permissions pattern), `Auditing/`, `Configuration/` (settings), `Approvals/` (rule engine, if you need approvals) | `Catalog/`, `Warehousing/`, `Stock/`, `Approvals/ApprovalRule` thresholds for stock documents, stock entries in `Common/Constants` (messages, limits, audit actions for documents) |
| Application | `Common/` (CQRS dispatcher, behaviours, ports, paging, persistence interfaces), `Features/Auth`, `Users`, `Settings`, `Audit`, import/export plumbing (`IExcelReader/Writer`) | `Features/Catalog`, `Warehouses`, `Partners`, `Documents`, `Stock`, `Reports`, `Import` handlers |
| Infrastructure | `Persistence` (`AppDbContext` base mapping style, `Repository<T>`, `UnitOfWork` with deadlock/retry, audit + append-only enforcement), `Security`, `Excel`, DI composition | entity configurations for sample entities, `SeedDefaults` demo data |
| Api | everything under `Composition/`, `Common/` (exception handler, correlation id, current user), `Program.cs`, controllers for Auth/Users/Settings/Audit | controllers for the sample features |
| Web | `libs/shared/*`, `libs/core/*` (identity, http, i18n, theme, pwa, navigation, notification), app shell | `libs/features/*` and `libs/containers/*` for the sample features |
| Ops | `.github/` (CI, release, deploy skeleton, PR template, dependabot), `Dockerfile`, `docker-compose.yml`, `.editorconfig`, `scripts/` | the smoke test scenarios in `tests/smoke` |
| Team | `.claude/agents/` roles, `docs/team/` protocol and PR review | the product-specific docs |

A good way to see the shape: the `Features/Catalog` slice (list/get/create/update with specification, validator, handler, controller)
is the smallest complete example of a feature. Copy it for each new aggregate.

## 3. Conventions the base enforces

- Clean Architecture dependency direction, checked by architecture tests.
- CQRS: controllers only send commands and queries through `IDispatcher`.
- Vertical slices, one public type per file.
- No magic numbers or strings (constants files, `Messages.*`).
- Handlers depend on interfaces only; DI by convention with start-up validation.
- Deadlock policy and optimistic concurrency in `UnitOfWork`.
- Structured logging with correlation ids; ProblemDetails for every error.

## 4. Extracting a shared framework

Once a second project exists, the generic parts (CQRS, persistence, exception mapping, logging, auth setup, Angular core/shared tiers)
move into versioned packages (`Framework.Domain`, `.Application`, `.Infrastructure`, `.AspNetCore`, plus an npm workspace
library). See task T23 in `TASKS.md`. Until then, this repository is the template.
