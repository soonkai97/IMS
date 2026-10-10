# Inventory Management System (IMS)

Warehouse-workflow-centred inventory for SMEs: receive, issue, stock enquiry, approvals and reports, with the phone as the scanner.
Angular 22 PWA, .NET 10, SQL Server. Requirements: the product requirements document (PRD v0.2), kept locally in `docs/private/` and not published. Task list: [TASKS.md](TASKS.md).
Scope built so far: Phase 0 (foundation) and Phase 1 (MVP).

## Layout

| Path | What |
|---|---|
| `src/Ims.Domain` | Data model, behaviour, business services and rules (no dependencies) |
| `src/Ims.Application` | Use cases (CQRS), specifications, validators, ports |
| `src/Ims.Infrastructure` | EF Core + migrations, repositories, JWT, Excel, seed |
| `src/Ims.Api` | REST API, auth, Swagger, logging |
| `web/` | Angular PWA |
| `tests/` | Domain, architecture, integration and smoke tests |
| `docs/` | PRD, architecture, design, QA notes, team protocol |

Architecture details: [docs/architecture/backend.md](docs/architecture/backend.md).

## Run it locally

Prerequisites: .NET 10 SDK, Node 20.19+, SQL Server (LocalDB works).

```bash
# 1. API (applies migrations and seeds demo data on first start)
cd src/Ims.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run     # http://localhost:5109, Swagger at /swagger

# 2. Web
cd web
npm install
npm start                                          # http://localhost:4200
```

Development settings (LocalDB connection, demo users and their dev-only passwords) are in
`src/Ims.Api/appsettings.Development.json`. They only work against a throwaway local database.
Never reuse them elsewhere; production secrets come from environment variables (see `.env.example`).

## Test

```bash
dotnet test Ims.sln                                           # unit + architecture (+ integration with IMS_TEST_DB set)
IMS_ADMIN_PASSWORD=... IMS_DEMO_PASSWORD=... node tests/smoke/smoke.mjs   # end-to-end API flow against a running API
cd web && npm test
```

## CI/CD (GitHub Actions)

| Workflow | When | Does |
|---|---|---|
| `ci.yml` | push, PR | build (warnings as errors), unit + architecture + integration tests on a SQL Server container, API smoke test, web lint/test/build, dependency audit |
| `release.yml` | push to `main`, tags `v*` | builds the API container image and pushes it to GitHub Container Registry |
| `deploy.yml` | manual | environment-gated deploy skeleton. The target is still open (PRD 14.3), so the final step is a marked placeholder |

## Docker

```bash
cp .env.example .env   # fill in the values
docker compose up --build
```

## Team

Delivery roles (product, BA, design, architecture, QA personas, testers) are defined as agents in `.claude/agents/`;
the hand-off protocol is in `docs/team/PROTOCOL.md`.
