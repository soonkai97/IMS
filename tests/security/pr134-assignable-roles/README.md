# PR 134 (assignable roles for the user form) probes

Node 20 scripts, no dependencies, against a local Release API (same setup as `../pr118-roles-rp5/README.md`; `env.sh` shows the variables, with made-up values for a throw-away database). Order: `s0` (custom roles and callers), `p1` (authorization matrix, and for every caller the list compared with what `POST /api/users` accepts), `p2` (information leak, mass assignment, exact role code, targets whose role is not assignable), `p3`/`p4` (a user with no warehouse and `allWarehouses=false`: nothing must be returned), `tokens` (re-issue tokens for a second instance, e.g. a mutant).
No secret is stored; users the scripts create share one made-up throw-away password.
