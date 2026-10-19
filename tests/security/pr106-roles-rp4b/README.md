# PR 106 (RP-4b) probes

Node 20 scripts (no dependencies) that drive a locally running Release API and print what they saw. Run order: `p0` (writes `tokens.json` for the five seeded users), then `p1`, `p1b` (approval tiers and corrections), `p2` (role code shapes), `p3` (allWarehouses guard and escalation), `p4` (last administrator and its race), `p5` (rule API), `p7` (claims and refresh), `p8`/`p9` (inactive or soft-deleted role), `p10`/`p11` (over-posting, no partial save).

Assumptions: API on `http://127.0.0.1:5316` in Development against a throw-away database `Ims_Rv106` on `(localdb)\ImsDev`; `lib.mjs` reads the seed passwords from the published `appsettings.Development.json` (paths on D: at the top of `lib.mjs`); `p3`, `p8`, `p9` expect custom roles `Delegate`, `Peek`, `Dormant`, `Gone` inserted by SQL. Adjust the paths before reuse. Seed passwords are read at run time, never stored; the scripts use one made-up throw-away password for the users they create.
