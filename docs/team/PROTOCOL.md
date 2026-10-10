# Team protocol

The agents in `.claude/agents/` form one delivery chain. They cannot message each other; they collaborate through
files in this repo, and the **orchestrator** (the main Claude session) routes work between them.

## Chain (who feeds whom)

```
product-manager -> business-analyst -> ui-ux-designer -> fe-designer -> fe-architect -> fe-framework
                         |                                                  ^              |
                         v                                                  | contract     v
                 be-system-designer -> be-architect -> be-dba -> be-framework        qa-novice / qa-power / qa-edge
                         |                                  |         |                    |
                         v                                  v         v                    v
                 data-flow-analyst              unit-test / integration-test       behavior-test
project-manager: reads HANDOFFS.md, keeps TASKS.md true, names the next role.
```

## Lanes (who may edit what)

| Role | May edit |
|---|---|
| product-manager, business-analyst, project-manager | docs/product, docs/ba, docs/pm, TASKS.md |
| ui-ux-designer, fe-designer | docs/design, web/src/styles |
| fe-architect | docs/architecture/frontend.md (review web/) |
| fe-framework | web/ |
| be-architect | docs/architecture/backend.md (review src/) |
| be-system-designer, data-flow-analyst | docs/design, docs/dataflow |
| be-dba | src/Ims.Infrastructure/Persistence, migrations |
| be-framework | src/ |
| unit-test-engineer, integration-test-engineer | tests/ |
| behavior-test-engineer | tests/features, web/e2e |
| security-engineer | docs/security, tests/security (reports and proof-of-concept scripts; never src/) |
| qa-* personas | docs/qa only |

Defects found outside your lane go to the owner via HANDOFFS.md; do not fix them yourself.

## Handoff log

Append one entry per finished piece of work to `docs/team/HANDOFFS.md` (newest at the bottom):

```
### <date> <from-role> -> <to-role(s)>
- Produced: <paths>
- Needs from <role>: <ask>
- Decisions others must respect: <list or none>
- Open conflicts: <list or none>
```

## Definition of done (any role)
1. Output is in a file, not only in chat.
2. Anything buildable was built and any test was run; the result is stated plainly.
3. A HANDOFFS.md entry exists naming the next role.

## Orchestrator rules
- Run roles in parallel only when their lanes and inputs are independent.
- Read HANDOFFS.md after every agent finishes and dispatch the named next role.
- Escalate to the user only for decisions listed in PRD 14.3 or anything destructive.
