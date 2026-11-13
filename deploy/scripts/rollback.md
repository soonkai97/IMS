# Rollback

Read `docs/dba/README.md` section 17 first: the schema is rolled back by hand and, after real use, usually NOT at all (restore the backup instead).

## 1. The code only (no schema change in the release being undone)

```sh
deploy/scripts/deploy.sh --tag <previous-tag> --migrated
```

`deploy.sh` printed the previous tag at the end of the last deploy and keeps it in `deploy/.state/previous-tag`. `--migrated` is right here because no migration is run.
Check with `deploy/scripts/smoke.sh` (it runs at the end of `deploy.sh`). Users reload the page; the service worker serves the old bundle on the next load.

## 2. The release added a migration

Order, no step is skipped:

1. Stop the API so nothing writes: `docker compose --env-file deploy/.env.prod -f deploy/compose.prod.yml stop api proxy`.
2. Take a backup now (`deploy/scripts/backup.sh full`): the state you are about to change.
3. Run the `Down` of the migrations of the release, newest first, by hand, as the DBA script (`dotnet ef migrations script <to> <from>` gives it), with `sqlcmd -I -b`. Many `Down` steps REFUSE while data of the feature exists (purchase orders, links, price lists, returns: error 50006 to 50010 and the like). A refusal is a stop sign, not something to work around: go to section 3.
4. Deploy the previous tag: `deploy/scripts/deploy.sh --tag <previous-tag> --migrated`.

`RemoveAccountingModule`: its `Down` does nothing. To go back to a build older than the tax-fields removal run `Down` of `RemoveTaxReadyFields` first (`dotnet ef database update 20261022122230_RemoveAccountingModule`), then the older binary.

## 3. Restore from backup (the safe path after real use)

1. Stop the API and the proxy (step 1 above).
2. Restore the newest full backup taken BEFORE the migration, then the log backups up to the moment you want (point in time), in a database named `Ims`:
   ```sh
   docker compose --env-file deploy/.env.prod -f deploy/compose.prod.yml exec -T db sh -c \
     '/opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "RESTORE DATABASE [Ims] FROM DISK = N'"'"'/var/opt/mssql/backup/<full>.bak'"'"' WITH REPLACE, NORECOVERY;"'
   # repeat RESTORE LOG ... WITH NORECOVERY for each .trn in order, then:  RESTORE DATABASE [Ims] WITH RECOVERY;
   ```
3. The attachment files are not in the SQL backup: restore the `files` volume from its own copy if the incident touched it.
4. Deploy the previous tag (`deploy.sh --tag <previous-tag> --migrated`), run `smoke.sh`, and check the ledger against the balances (`docs/dba/08-reconcile-drill.sql`).

## 4. What a restore test is

Once before go-live and after every change of the backup setup: restore the newest full backup under another name (`RESTORE DATABASE [Ims_Restore] ... WITH MOVE`), run `DBCC CHECKDB` on it, compare the document and ledger row counts with `Ims`, drop it. `docs/dba/05-backup-restore.sql` is the timed script for it.
