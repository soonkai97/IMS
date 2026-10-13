# Public-id retest scripts (PR-10, security part)

Proof-of-concept scripts for `docs/security/2026-10-public-id-retest.md`. Node 20+, no dependencies. They talk only to a throw-away API on
`localhost`; `lib.mjs` refuses any other host. Credentials are read at run time from `src/Ims.Api/appsettings.Development.json` (override the path
with `IMS_DEV_SETTINGS`); the passwords of the extra users the setup creates are random and live only in the state file.

Environment: `IMS_BASE` (default `http://localhost:5710`), `IMS_STATE` (path of the state file, keep it outside the repository).

Run order against a Release build started in Development against a new database (seeded demo users):

1. `node setup.mjs`: second and third warehouse, scoped users, products, documents, attachments.
2. `node softdelete.mjs | sqlcmd -I -S "(localdb)\ImsDev" -E -d <your throw-away database>`: marks the "gone" rows soft-deleted.
3. `node idor.mjs out.json`: path-id matrix (20 routes x 10 actors x id variants).
4. `node idor-fields.mjs`: ids in query strings, JSON bodies and the upload form.
5. `node client-ids.mjs`: no client-minted ids, mass assignment.
6. `node cursors.mjs`: cursor tamper, replay across lists and users.
7. `node enum.mjs`, `node counters.mjs`: integer ids and counters that still leave the API.
8. `node timing.mjs`: interleaved timing of "missing / not yours / soft-deleted" answers.
9. `shortage-fallback.mjs` (STEP=1, soft-delete the printed product by SQL, STEP=2): the insufficient-stock message after a soft delete.

`reupload.mjs` re-creates the attachment that a matrix run may have deleted. Drop the database when finished.
