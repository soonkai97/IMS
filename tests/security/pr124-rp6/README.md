# PR 124 (RP-6, administrability) probes

Node 20 scripts, no dependencies; they drive a local Release API and print `ok` / `FAIL`. They also read and edit the throw-away database with `sqlcmd` (`RV_DB`, default `Ims_Rv124`, instance `(localdb)\ImsDev`, files on D:).

Setup: publish the head in Release, create the database, start the API on loopback (`ASPNETCORE_ENVIRONMENT=Development`, `ConnectionStrings__Default`, made-up `Seed__AdminPassword` / `Seed__DemoPassword`, `Jwt__AccessMinutes=30`); export `RV_BASE`, `RV_ADMIN_PW`, `RV_DEMO_PW`, `RV_OUT` (scratch folder on D:).

Order: `s0` (fixtures), `p1` sequential lock-out matrix, `p2` parallel (`ROUNDS`), `p4` users of an inactive / deleted role, `p5` approval-rule write (P04), `p6` roles lock held while a password is hashed, `p7` two API instances (`RV_BASE2`), `p8` self-approval per tier. `p3` needs another session holding `sp_getapplock 'roles'` first (lock timeout). A T9 line in `p1` that expects 409 gets 403 (containment answers first): script expectation, not a defect.
No secret is stored; created users share one made-up throw-away password.
